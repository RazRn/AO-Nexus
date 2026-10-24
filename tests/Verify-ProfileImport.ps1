param([string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'))
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') {
    throw 'Run using 32-bit Windows PowerShell -NoProfile -STA.'
}
Add-Type -AssemblyName System.Web.Extensions
Add-Type -AssemblyName System.Security
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$static = [Reflection.BindingFlags]'NonPublic,Static'
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$bootstrap = $assembly.GetType('AOLauncher.LauncherBootstrap')
$store = $assembly.GetType('AOLauncher.SettingsStore')
$credentials = $assembly.GetType('AOLauncher.CredentialStore')
$import = $bootstrap.GetMethod('ImportFromDirectory', $static)
$read = $store.GetMethod('ReadFromDirectory', $static)
$save = $store.GetMethod('SaveToDirectory', $static)
$savePassword = $credentials.GetMethod('SaveToDirectory', $static)
$readPassword = $credentials.GetMethod('ReadFromDirectory', $static)
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$testName = 'AONexus-ProfileImport-' + [Guid]::NewGuid().ToString('N')
$testRoot = [IO.Path]::GetFullPath((Join-Path $tempParent $testName))
$script:checks = 0
function Assert([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    $script:checks++
}
function Make([string]$Name) { [Activator]::CreateInstance($assembly.GetType('AOLauncher.' + $Name)) }
function Import([string]$Source, [string]$Destination) { $import.Invoke($null, [object[]]@($Source, $Destination)) }
function Reject([scriptblock]$Action, [string]$Message) {
    $failure = $null
    try { & $Action | Out-Null } catch { $failure = $_.Exception.GetBaseException() }
    Assert ($null -ne $failure) $Message
}
function Snapshot([string]$Path) {
    if (![IO.Directory]::Exists($Path)) { return 'absent' }
    $items = @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName | ForEach-Object {
        ($_.FullName.Substring($Path.Length)) + '|' + (Get-FileHash -LiteralPath $_.FullName).Hash + '|' + $_.LastWriteTimeUtc.Ticks
    })
    return ($items -join "`n")
}
try {
    [IO.Directory]::CreateDirectory($testRoot) | Out-Null
    [string]$source = Join-Path $testRoot 'original'
    [string]$destination = Join-Path $testRoot 'nexus'
    Assert (!(Import $source $destination)) 'Absent original profile must be a no-op.'
    Assert (![IO.Directory]::Exists($destination)) 'A no-op import must not create the destination.'
    Reject { Import $source $source } 'Identical source and destination must be rejected.'

    [IO.Directory]::CreateDirectory($source) | Out-Null
    $settings = Make 'LauncherSettings'; $settings.Language = 'da'
    $account = Make 'AccountConfig'
    $account.Id = 'synthetic-import-account'; $account.Label = 'Only a fixture'
    $account.Username = 'fixture-user'; $account.GamePath = 'C:\SyntheticGames\NexusFixture'
    $account.Server = '192.0.2.5'; $account.Port = 7005; $account.ServerProfileId = $null
    $account.CharacterSlots = 18; $account.Collapsed = $true
    $character = Make 'CharacterConfig'; $character.CharacterId = 44; $character.Name = 'FixtureChar'; $character.Selected = $true
    $account.Characters.Add($character); $settings.Accounts.Add($account)
    [IO.File]::WriteAllText((Join-Path $source 'AOLauncher.json'), $serializer.Serialize($settings), [Text.UTF8Encoding]::new($false))
    [string]$credentialSource = Join-Path $source 'Credentials'
    $savePassword.Invoke($null, [object[]]@($account, 'inert-fixture-password', $credentialSource)) | Out-Null
    [IO.File]::WriteAllText((Join-Path $credentialSource 'unrelated.bin'), 'not an account credential')
    $originalSnapshot = Snapshot $source
    Assert (Import $source $destination) 'First import must copy an existing valid original profile.'
    Assert ((Snapshot $source) -ceq $originalSnapshot) 'Import must preserve all original bytes and modification timestamps.'
    Assert ([IO.File]::Exists((Join-Path $destination 'AONexus.json'))) 'New profile must use the independent Nexus filename.'
    Assert (![IO.File]::Exists((Join-Path $destination 'AOLauncher.json'))) 'Old product filename must not be introduced into the new profile.'
    $imported = $read.Invoke($null, [object[]]@($destination))
    Assert ($serializer.Serialize($imported) -ceq $serializer.Serialize($settings)) 'First import must preserve accounts, language, selection, capacity and collapse state.'
    [string]$credentialDestination = Join-Path $destination 'Credentials'
    $copiedFiles = @(Get-ChildItem -LiteralPath $credentialDestination -File)
    Assert ($copiedFiles.Count -eq 1 -and $copiedFiles[0].Name -ne 'unrelated.bin') 'Only credentials belonging to imported accounts may be copied.'
    $credentialFile = @(Get-ChildItem -LiteralPath $credentialSource -File | Where-Object Name -ne 'unrelated.bin')[0]
    Assert ((Get-FileHash -LiteralPath $credentialFile.FullName).Hash -ceq (Get-FileHash -LiteralPath $copiedFiles[0].FullName).Hash) 'Imported credentials must be unchanged encrypted bytes.'
    $password = $readPassword.Invoke($null, [object[]]@($imported.Accounts[0], $credentialDestination))
    Assert ($password -ceq 'inert-fixture-password') 'Imported encrypted credential must remain usable by the current Windows user.'
    $imported.Accounts[0].Port = 7006
    Assert ($null -eq $readPassword.Invoke($null, [object[]]@($imported.Accounts[0], $credentialDestination))) 'Changing the new account endpoint must not reuse a password bound to another server.'
    Assert ((Snapshot $source) -ceq $originalSnapshot) 'Reading credentials and changing the new profile must not modify the original.'

    $imported.Accounts[0].Label = 'Independent Nexus edits'
    $save.Invoke($null, [object[]]@($imported, $destination)) | Out-Null
    $independentSnapshot = Snapshot $destination
    Assert (!(Import $source $destination)) 'Repeat import must be an idempotent no-op.'
    Assert ((Snapshot $destination) -ceq $independentSnapshot) 'Repeat import must preserve all new settings and encrypted credential bytes.'
    Assert ((Snapshot $source) -ceq $originalSnapshot) 'Repeat import must preserve the original profile.'

    [string]$badSource = Join-Path $testRoot 'invalid-original'; [string]$badTarget = Join-Path $testRoot 'invalid-target'
    [IO.Directory]::CreateDirectory($badSource) | Out-Null
    [IO.File]::WriteAllText((Join-Path $badSource 'AOLauncher.json'), '{ broken current json')
    [IO.File]::WriteAllText((Join-Path $badSource 'RebirthLauncher.json'), $serializer.Serialize($settings))
    $invalidSnapshot = Snapshot $badSource
    Reject { Import $badSource $badTarget } 'Corrupt current profile must fail rather than fall back to stale legacy settings.'
    Assert (![IO.Directory]::Exists($badTarget)) 'Invalid source settings must not write any target data.'
    Assert ((Snapshot $badSource) -ceq $invalidSnapshot) 'Failed import must leave corrupt source bytes untouched.'
    Assert (!(Import $badSource $destination)) 'Existing Nexus profile must take priority even if original settings later become corrupt.'
    Assert ((Snapshot $destination) -ceq $independentSnapshot) 'Existing Nexus data must remain untouched when the original becomes invalid.'

    [string]$legacySource = Join-Path $testRoot 'legacy-original'; [string]$legacyTarget = Join-Path $testRoot 'legacy-target'
    [IO.Directory]::CreateDirectory($legacySource) | Out-Null
    [IO.File]::WriteAllText((Join-Path $legacySource 'RebirthLauncher.json'), $serializer.Serialize($settings))
    $legacySnapshot = Snapshot $legacySource
    Assert (Import $legacySource $legacyTarget) 'Legacy original filename must remain importable.'
    Assert ((Snapshot $legacySource) -ceq $legacySnapshot) 'Legacy import must leave the old file untouched.'
    Assert ($read.Invoke($null, [object[]]@($legacyTarget)).Accounts[0].Id -ceq 'synthetic-import-account') 'Legacy import must preserve stable account identity.'

    [string]$existingCredentialTarget = Join-Path $testRoot 'existing-encrypted-target'
    [string]$existingCredentials = Join-Path $existingCredentialTarget 'Credentials'
    $savePassword.Invoke($null, [object[]]@($account, 'independent-target-secret', $existingCredentials)) | Out-Null
    $existingCredentialSnapshot = Snapshot $existingCredentials
    Assert (Import $source $existingCredentialTarget) 'Import can complete a profile with an already present credential directory.'
    Assert ((Snapshot $existingCredentials) -ceq $existingCredentialSnapshot) 'Existing target credentials must never be overwritten during import.'
    Assert ($readPassword.Invoke($null, [object[]]@($account, $existingCredentials)) -ceq 'independent-target-secret') 'The existing encrypted target secret remains authoritative.'
    Assert ((Snapshot $source) -ceq $originalSnapshot) 'All import scenarios must leave the original profile byte-for-byte intact.'

    "PASS: $script:checks isolated first-run import checks; only synthetic settings and synthetic DPAPI credentials used."
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if ([IO.Path]::GetDirectoryName($resolved) -cne $tempParent -or [IO.Path]::GetFileName($resolved) -cne $testName -or !$testName.StartsWith('AONexus-ProfileImport-')) { throw 'Unsafe profile fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
