param([string]$AssemblyPath = (Join-Path $PSScriptRoot '..\src\Launcher\bin\Release\AONexus.exe'))
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Run with 32-bit Windows PowerShell -NoProfile -STA.' }
Add-Type -AssemblyName System.Web.Extensions, System.Windows.Forms
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$serializer = [System.Web.Script.Serialization.JavaScriptSerializer]::new()
$apply = $assembly.GetType('AOLauncher.MainForm', $true).GetMethod('TryApplyCharacterEvent', $static)
$targets = $assembly.GetType('AOLauncher.AccountTargets', $true)
$script:checks = 0
function Assert([bool]$Condition, [string]$Description) {
    if (!$Condition) { throw ('FAIL: ' + $Description) }
    $script:checks++
}
function New-Model([string]$Name) { [Activator]::CreateInstance($assembly.GetType('AOLauncher.' + $Name, $true)) }
function New-Char([int]$Id, [string]$Name, [bool]$Selected = $true, [bool]$Pending = $false) {
    $char = New-Model 'CharacterConfig'; $char.CharacterId = $Id; $char.Name = $Name
    $char.Selected = $Selected; $char.NamePending = $Pending; return $char
}
function Fixture {
    $settings = New-Model 'LauncherSettings'
    $settings.Language = 'en'; $settings.Accounts.Clear(); $settings.Presets.Clear(); $settings.LoginServers.Clear()
    $account = New-Model 'AccountConfig'
    $account.Id = 'synthetic-created-account'; $account.Label = 'Synthetic account without Prefs'
    $account.Username = 'fixture-login'; $account.GamePath = 'C:\AO-Nexus-Synthetic\Client'
    $account.Server = '192.0.2.21'; $account.Port = 7500; $account.PrefsPath = ''
    $account.CharacterSlots = 2
    $settings.Accounts.Add($account)
    $request = New-Model 'LaunchRequest'
    $launch = New-Model 'LaunchCharacter'
    $launch.AccountId = $account.Id; $launch.Account = $account.Username
    $launch.GamePath = $account.GamePath; $launch.Server = $account.Server; $launch.Port = $account.Port
    $launch.CreateCharacter = $true; $launch.CharacterId = 0; $launch.CharacterName = 'Create new char'
    $launch.Password = 'synthetic-password-only'; $request.Characters.Add($launch)
    return [pscustomobject]@{ Settings = $settings; Account = $account; Request = $request; Launch = $launch }
}
function Notice($Fixture, [int]$Id = 980123, [string]$Name = 'Createdhero') {
    return @{ AccountId = $Fixture.Account.Id; CharacterId = $Id; CharacterName = $Name; Created = $true }
}
function Apply($Fixture, $Notification) {
    $line = if ($Notification -is [string]) { $Notification } else { 'NEXUS_CHARACTER ' + $serializer.Serialize($Notification) }
    $arguments = [object[]]@($line, $Fixture.Request, $Fixture.Settings, $null)
    $accepted = [bool]$apply.Invoke($null, $arguments)
    return [pscustomobject]@{ Accepted = $accepted; Updated = $arguments[3] }
}
function Reject($Fixture, $Notification, [string]$Description) {
    $before = $serializer.Serialize($Fixture.Settings)
    $result = Apply $Fixture $Notification
    Assert (!$result.Accepted -and $null -eq $result.Updated) ($Description + ' must be rejected.')
    Assert ($before -ceq $serializer.Serialize($Fixture.Settings)) ($Description + ' must leave the entire settings model unchanged.')
}
function Can-Create($Fixture) { return [bool]$targets.GetMethod('CanCreateCharacter', $static).Invoke($null, [object[]]@($Fixture.Settings, $Fixture.Account)) }

# This fixture has no Prefs folder or characters. The protocol is sufficient to
# discover a character created by the one outstanding creation request.
$fixture = Fixture
$result = Apply $fixture (Notice $fixture)
Assert $result.Accepted 'A valid creation notification must add a char to an account with no Prefs folder.'
Assert ($fixture.Account.Characters.Count -eq 1 -and $result.Updated.CharacterId -eq 980123 -and $result.Updated.Name -ceq 'Createdhero') 'The created char must have the observed ID and name.'
Assert (!$result.Updated.Selected -and !$result.Updated.NamePending) 'The discovered char must start unselected with its resolved name.'
Assert ([object]::ReferenceEquals($result.Updated, $fixture.Account.Characters[0])) 'The returned char must be the saved account model entry.'
Assert ($fixture.Account.PrefsPath -ceq '') 'Creation discovery must not require or invent a Prefs folder.'
Assert ($fixture.Request.Characters.Count -eq 1 -and $fixture.Launch.CreateCharacter -and $fixture.Launch.CharacterId -eq 0) 'Applying an event must not mutate the outstanding launch request.'
$snapshot = $serializer.Serialize($fixture.Settings)
$duplicate = Apply $fixture (Notice $fixture)
Assert ($fixture.Account.Characters.Count -eq 1 -and $snapshot -ceq $serializer.Serialize($fixture.Settings)) 'An identical creation event must be idempotent.'

# Selection and saved teams remain under the user's control.
$fixture = Fixture
$known = New-Char 980100 'Knownhero' $true
$fixture.Account.Characters.Add($known)
$preset = New-Model 'LaunchPreset'; $preset.Id = 'fixture-team'; $preset.Name = 'Existing team'
$member = New-Model 'PresetCharacter'; $member.AccountId = $fixture.Account.Id; $member.CharacterId = $known.CharacterId
$preset.Characters.Add($member); $fixture.Settings.Presets.Add($preset); $fixture.Settings.SelectedPresetId = $preset.Id
$presetBefore = $serializer.Serialize($fixture.Settings.Presets)
Assert (Can-Create $fixture) 'An account with one of two slots used must initially offer creation.'
$result = Apply $fixture (Notice $fixture)
Assert $result.Accepted 'A second char must be imported without changing the selected team.'
Assert ($known.Selected -and !$result.Updated.Selected -and $fixture.Settings.SelectedPresetId -eq $preset.Id) 'Import must preserve existing selection and keep the new char unselected.'
Assert ($presetBefore -ceq $serializer.Serialize($fixture.Settings.Presets)) 'Import must not append the char to a saved team.'
Assert (!(Can-Create $fixture)) 'The creation action must become unavailable when the imported char fills configured capacity.'

# An already listed manually named char is preserved, including its selection.
$fixture = Fixture
$existing = New-Char 980123 'My custom display name' $true
$fixture.Account.Characters.Add($existing)
$snapshot = $serializer.Serialize($fixture.Settings)
$result = Apply $fixture (Notice $fixture)
Assert ($fixture.Account.Characters.Count -eq 1 -and $fixture.Account.Characters[0].Name -ceq 'My custom display name') 'Creation discovery must not overwrite an existing manual name.'
Assert ($snapshot -ceq $serializer.Serialize($fixture.Settings)) 'Existing manual char data must remain untouched by duplicate discovery.'
$fixture = Fixture
$fixture.Account.Characters.Add((New-Char 980123 'Character 980123' $true $true))
$result = Apply $fixture (Notice $fixture)
Assert ($result.Accepted -and $result.Updated.Name -ceq 'Createdhero' -and !$result.Updated.NamePending -and $result.Updated.Selected) 'A previously imported placeholder must gain its detected name without losing selection.'
$fixture = Fixture
$fixture.Account.GamePath = $fixture.Account.GamePath.ToLowerInvariant() + '\'
$fixture.Account.Username = '  FIXTURE-LOGIN  '
$result = Apply $fixture (Notice $fixture)
Assert $result.Accepted 'Equivalent Windows path casing/trailing separators and normalized usernames must preserve the original creation target.'

foreach ($id in @(0,-1,[int]::MinValue)) {
    $fixture = Fixture; Reject $fixture (Notice $fixture $id) ('Invalid char ID ' + $id)
}
foreach ($name in @('', '   ', ('N' * 65), "New`nName", ('Bad' + [char]0 + 'Name'))) {
    $fixture = Fixture; Reject $fixture (Notice $fixture 980123 $name) 'Invalid char name'
}
foreach ($raw in @('NEXUS_CHARACTER {', 'NEXUS_CHARACTER null', 'NEXUS_CHARACTER []', 'NEXUS_CHARACTER {"Created":true,"CharacterId":2147483648,"CharacterName":"Hero"}', 'NEXUS_CHARACTER {"Created":true,"CharacterId":"bad","CharacterName":"Hero"}', ('NEXUS_CHARACTER ' + ('x' * 2050)), 'NEXUS_CHARACTER_BAD {}', 'Other output')) {
    $fixture = Fixture; Reject $fixture $raw 'Malformed or unrelated backend output'
}
foreach ($identifier in @($null, '', 'different-account-id')) {
    $fixture = Fixture; $notice = Notice $fixture; $notice.AccountId = $identifier
    Reject $fixture $notice 'Missing or mismatched event account ID'
}
$fixture = Fixture; $notice = Notice $fixture; $notice.Remove('AccountId')
Reject $fixture $notice 'Omitted event account ID'
$fixture = Fixture; $fixture.Launch.AccountId = ''
Reject $fixture (Notice $fixture) 'Creation request without an account ID'

# Events must remain bound to the account target from the launch request, even if
# the user has edited or removed that account while character creation is open.
foreach ($change in @('Username','GamePath','Server','Port','Id','Deleted')) {
    $fixture = Fixture; $notice = Notice $fixture
    switch ($change) {
        'Username' { $fixture.Account.Username = 'different-login' }
        'GamePath' { $fixture.Account.GamePath = 'C:\AO-Nexus-Synthetic\DifferentClient' }
        'Server' { $fixture.Account.Server = '192.0.2.22' }
        'Port' { $fixture.Account.Port = 7501 }
        'Id' { $fixture.Account.Id = 'replaced-account-id' }
        'Deleted' { $fixture.Settings.Accounts.Clear() }
    }
    Reject $fixture $notice ('An account whose ' + $change + ' target changed')
}

# Same IDs can legitimately exist on different servers. They cannot be assigned
# to a different account on this exact server by a creation notification.
$fixture = Fixture
$other = New-Model 'AccountConfig'; $other.Username = 'another-login'; $other.GamePath = $fixture.Account.GamePath
$other.Server = $fixture.Account.Server; $other.Port = $fixture.Account.Port
$other.Characters.Add((New-Char 980123 'Otherowner' $false)); $fixture.Settings.Accounts.Add($other)
Reject $fixture (Notice $fixture) 'A same-server char ID already assigned to another account'
$fixture = Fixture
$other = New-Model 'AccountConfig'; $other.Username = 'another-login'; $other.GamePath = $fixture.Account.GamePath
$other.Server = '192.0.2.22'; $other.Port = $fixture.Account.Port
$other.Characters.Add((New-Char 980123 'Otherworld' $true)); $fixture.Settings.Accounts.Add($other)
$result = Apply $fixture (Notice $fixture)
Assert ($result.Accepted -and $fixture.Account.Characters.Count -eq 1 -and $other.Characters.Count -eq 1 -and $other.Characters[0].Name -ceq 'Otherworld') 'Identical char IDs on different endpoints must remain independent.'

foreach ($mode in @('OrdinaryRequest','NonzeroCreationId','MultipleCreationRequests','MixedRequests','MissingCreated','FalseCreated')) {
    $fixture = Fixture; $notice = Notice $fixture
    switch ($mode) {
        'OrdinaryRequest' { $fixture.Launch.CreateCharacter = $false; $fixture.Launch.CharacterId = 980123 }
        'NonzeroCreationId' { $fixture.Launch.CharacterId = 980123 }
        'MultipleCreationRequests' { $extra = New-Model 'LaunchCharacter'; $extra.CreateCharacter = $true; $fixture.Request.Characters.Add($extra) }
        'MixedRequests' { $extra = New-Model 'LaunchCharacter'; $extra.CharacterId = 980456; $fixture.Request.Characters.Add($extra) }
        'MissingCreated' { $notice.Remove('Created') }
        'FalseCreated' { $notice.Created = $false }
    }
    Reject $fixture $notice ('Creation event/request mismatch: ' + $mode)
}

# Ordinary name resolution still updates a requested, already listed char, while
# never acquiring permission to add unknown IDs through the new event path.
$fixture = Fixture; $fixture.Launch.CreateCharacter = $false; $fixture.Launch.CharacterId = 980123
$notice = Notice $fixture; $notice.Remove('Created')
Reject $fixture $notice 'Ordinary login event for an unknown char'
$fixture.Account.Characters.Add((New-Char 980123 'Character 980123' $true $true))
$result = Apply $fixture $notice
Assert ($result.Accepted -and $fixture.Account.Characters.Count -eq 1 -and $result.Updated.Name -ceq 'Createdhero' -and !$result.Updated.NamePending -and $result.Updated.Selected) 'Ordinary pending-name resolution must remain functional and preserve selection.'
$notice.CharacterId = 980124
Reject $fixture $notice 'Ordinary event with an ID absent from the launch request'

# Exercise foreground/background ownership using unstarted BackendRunner models.
# No process is spawned. The actual form is in its synthetic/offline preview mode.
$instance = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$mainType = $assembly.GetType('AOLauncher.MainForm', $true)
function Get-Field($Object, [string]$Name) { return ,$Object.GetType().GetField($Name, $instance).GetValue($Object) }
function Set-Field($Object, [string]$Name, $Value) { $Object.GetType().GetField($Name, $instance).SetValue($Object, $Value) }
function Call-Instance($Object, [string]$Name, [object[]]$Arguments = @()) {
    $method = @($Object.GetType().GetMethods($instance) | Where-Object { $_.Name -eq $Name -and $_.GetParameters().Length -eq $Arguments.Length })[0]
    $method.Invoke($Object, $Arguments)
}
$main = $null; $first = $null; $second = $null; $unrelated = $null; $firstPreparation = $null; $secondPreparation = $null
try {
    $main = [Activator]::CreateInstance($mainType, [object[]]@($true))
    Assert ((Get-Field $main 'smoke') -and (Get-Field (Get-Field $main 'newsPanel') 'offlinePreview')) 'Lifecycle checks must use synthetic accounts and offline news.'
    $fixture = Fixture
    Set-Field $main 'settings' $fixture.Settings
    $first = New-Model 'BackendRunner'; $second = New-Model 'BackendRunner'; $unrelated = New-Model 'BackendRunner'
    $firstPreparation = [Threading.CancellationTokenSource]::new()
    $secondPreparation = [Threading.CancellationTokenSource]::new()
    $monitors = Get-Field $main 'creationMonitors'
    $monitors.Add($first) | Out-Null
    Set-Field $main 'backendRunner' $first
    Set-Field $main 'preparationCancellation' $firstPreparation
    Call-Instance $main 'SetRunning' ([object[]]@($true)) | Out-Null
    $started = @{ AccountId = $fixture.Account.Id; ProcessId = 1234; StartTimeUtcTicks = [DateTime]::UtcNow.Ticks }
    foreach ($invalid in @('NEXUS_CREATION_STARTED {', 'NEXUS_CREATION_STARTED {"AccountId":"synthetic-created-account","ProcessId":0,"StartTimeUtcTicks":1}', 'NEXUS_CREATION_STARTED {"AccountId":"wrong-account","ProcessId":1234,"StartTimeUtcTicks":1}')) {
        Call-Instance $main 'HandleOperationLine' ([object[]]@($invalid, $fixture.Request, $first, $true)) | Out-Null
        Assert ([object]::ReferenceEquals((Get-Field $main 'backendRunner'), $first) -and (Get-Field $main 'running')) 'Malformed creation-start events must not unlock or detach a foreground operation.'
    }
    $startedLine = 'NEXUS_CREATION_STARTED ' + $serializer.Serialize($started)
    Call-Instance $main 'HandleOperationLine' ([object[]]@($startedLine, $fixture.Request, $first, $true)) | Out-Null
    Assert ($null -eq (Get-Field $main 'backendRunner') -and !(Get-Field $main 'running') -and $null -eq (Get-Field $main 'preparationCancellation')) 'Opening the creation client must release the foreground operation while its monitor remains active.'
    Assert ($monitors.Contains($first) -and !(Get-Field $first 'disposed') -and !(Get-Field $first 'stopRequested')) 'Detaching the foreground must keep its creation monitor alive.'
    Assert ($null -eq $fixture.Launch.Password) 'The detached creation request must no longer retain its supplied password.'

    Set-Field $main 'backendRunner' $second
    Set-Field $main 'preparationCancellation' $secondPreparation
    Call-Instance $main 'SetRunning' ([object[]]@($true)) | Out-Null
    $status = (Get-Field $main 'statusText')
    Call-Instance $main 'HandleOperationLine' ([object[]]@($startedLine, $fixture.Request, $first, $true)) | Out-Null
    Assert ([object]::ReferenceEquals((Get-Field $main 'backendRunner'), $second) -and (Get-Field $main 'running') -and [object]::ReferenceEquals((Get-Field $main 'preparationCancellation'), $secondPreparation)) 'A delayed old creation-start event must not detach a newer foreground login.'
    $createdLine = 'NEXUS_CHARACTER ' + $serializer.Serialize((Notice $fixture))
    Call-Instance $main 'HandleOperationLine' ([object[]]@($createdLine, $fixture.Request, $first, $true)) | Out-Null
    Assert ($fixture.Account.Characters.Count -eq 1 -and !(Get-Field $main 'log').Text.Contains('synthetic-password-only')) 'A background creation result must update the list without logging credentials.'
    Assert ([object]::ReferenceEquals((Get-Field $main 'backendRunner'), $second) -and (Get-Field $main 'running') -and (Get-Field $main 'statusText') -ceq $status) 'A background creation result must preserve a newer foreground login and its status.'
    Assert (!$fixture.Account.Characters[0].Selected -and $fixture.Settings.Presets.Count -eq 0) 'The real event handler must keep the discovered char unselected and preserve teams.'

    $event = [Windows.Forms.FormClosingEventArgs]::new([Windows.Forms.CloseReason]::UserClosing, $false)
    Call-Instance $main 'OnFormClosing' ([object[]]@($main, $event)) | Out-Null
    Assert ((Get-Field $first 'stopRequested') -and (Get-Field $second 'stopRequested') -and $secondPreparation.IsCancellationRequested) 'Closing the launcher must stop its active foreground and creation monitors and cancel preparation.'
    Assert (!(Get-Field $unrelated 'stopRequested')) 'Closing must not affect an unrelated runner.'
    Assert (!$event.Cancel -and (Get-Field $main 'closing')) 'Closing must remain immediate after signalling owned observers.'
} finally {
    if ($main) { $main.Dispose() }
    if ($first) { $first.Dispose() }; if ($second) { $second.Dispose() }; if ($unrelated) { $unrelated.Dispose() }
    if ($firstPreparation) { $firstPreparation.Dispose() }; if ($secondPreparation) { $secondPreparation.Dispose() }
}

"PASS: $script:checks created-character event and foreground/background lifecycle checks. Synthetic models and offline preview only; no saved profiles, credentials, Prefs files, games, network, or backend processes were accessed."
