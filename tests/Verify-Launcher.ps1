param([string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'))
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') {
    throw 'Run using C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -NoProfile -STA -File Verify-Launcher.ps1'
}
Add-Type -AssemblyName System.Web.Extensions
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Security
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
$assemblyFile = (Resolve-Path -LiteralPath $AssemblyPath).Path
$assembly = [Reflection.Assembly]::LoadFrom($assemblyFile)
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$settingsType = $assembly.GetType('AOLauncher.LauncherSettings')
$settings = [Activator]::CreateInstance($settingsType)
$json = $serializer.Serialize($settings)
$roundtrip = $serializer.Deserialize($json, $settingsType)
if ($roundtrip.GamePath -ne '' -or $roundtrip.Server -ne '2.24.96.30' -or $roundtrip.Port -ne 7500) {
    throw 'Settings defaults or JSON roundtrip failed.'
}
if ($json -match '"Password"\s*:') { throw 'Persisted settings contain a password property.' }
'PASS: settings defaults and JSON roundtrip; no password property in persistent model.'
$staticFlags = [Reflection.BindingFlags]'NonPublic,Static'
$deserializeSettings = $assembly.GetType('AOLauncher.SettingsStore').GetMethod('Deserialize', $staticFlags)
$targetsType = $assembly.GetType('AOLauncher.AccountTargets')
$legacyJson = '{"Version":1,"GamePath":"C:\\isolated-old-game","Server":"192.0.2.10","Port":7010,"Accounts":[{"Id":"legacy-a","Label":"A","Username":"same-user","Collapsed":true,"Characters":[{"Name":"KeepName","CharacterId":701,"Selected":true}]},{"Id":"legacy-b","Label":"B","Username":"another-user","Characters":[{"Name":"KeepOther","CharacterId":702,"Selected":false}]}],"Presets":[{"Id":"legacy-preset","Name":"Keep preset","Characters":[{"AccountId":"legacy-a","CharacterId":701}]}],"SelectedPresetId":"legacy-preset"}'
$migrated = $deserializeSettings.Invoke($null, [object[]]@($legacyJson))
if ($migrated.Version -ne 2 -or $migrated.Accounts[0].Id -ne 'legacy-a' -or !$migrated.Accounts[0].Collapsed -or !$migrated.Accounts[0].Characters[0].Selected -or $migrated.Accounts[1].Characters[0].Selected -or $migrated.Accounts[0].Characters[0].Name -ne 'KeepName' -or $migrated.SelectedPresetId -ne 'legacy-preset' -or $migrated.Presets[0].Characters[0].AccountId -ne 'legacy-a') { throw 'Legacy migration changed identity, labels, folding, selection or preset references.' }
foreach ($account in $migrated.Accounts) {
    if ($account.GamePath -ne 'C:\isolated-old-game' -or $account.Server -ne '192.0.2.10' -or $account.Port -ne 7010 -or $account.LegacyCredentialTarget -ne '192.0.2.10:7010') { throw 'Legacy account did not inherit its original global target and credential scope.' }
}
$migrated.GamePath = 'C:\different-default'
$migrated.Server = '192.0.2.99'
$migrated.Port = 7999
$migratedAgain = $deserializeSettings.Invoke($null, [object[]]@($serializer.Serialize($migrated)))
if ($migratedAgain.Accounts[0].GamePath -ne 'C:\isolated-old-game' -or $migratedAgain.Accounts[0].Server -ne '192.0.2.10' -or $migratedAgain.Accounts[0].Port -ne 7010) { throw 'Changed defaults retargeted an existing account after migration.' }
if ((@($migrated.LoginServers | ForEach-Object Id) -join ',') -ne 'rebirth,live,live-2019,prk,rebirth-local') { throw 'Default login-server catalog is incomplete.' }
$expectedServers = @(
    'AO Rebirth - Live|2.24.96.30|7500|0|0',
    'Funcom AO - Rubi-Ka|37.18.192.41|7505|0|1',
    'Funcom AO - Rubi-Ka 2019|37.18.192.41|7506|0|1',
    'Project Rubi-Ka - Ithaca|5.161.243.214|7000|2|1',
    'AO Rebirth — Local development (localhost)|127.0.0.1|7500|0|0'
)
$actualServers = @($migrated.LoginServers | ForEach-Object { '{0}|{1}|{2}|{3}|{4}' -f $_.Name, $_.Server, $_.Port, $_.MaxAccounts, $_.MaxCharactersPerAccount })
if (($actualServers -join "`n") -cne ($expectedServers -join "`n")) { throw 'Default server names, endpoints or independent limits differ from the requested catalog.' }
$savedCatalogJson = '{"Version":2,"Accounts":[],"LoginServers":[{"Id":"live","Name":"My renamed test server","Server":"192.0.2.50","Port":7050,"MaxAccounts":4,"MaxCharactersPerAccount":3}]}'
$savedCatalog = $deserializeSettings.Invoke($null, [object[]]@($savedCatalogJson))
if ($savedCatalog.LoginServers.Count -ne 1 -or $savedCatalog.LoginServers[0].Id -ne 'live' -or $savedCatalog.LoginServers[0].Name -ne 'My renamed test server' -or $savedCatalog.LoginServers[0].Server -ne '192.0.2.50' -or $savedCatalog.LoginServers[0].Port -ne 7050 -or $savedCatalog.LoginServers[0].MaxAccounts -ne 4 -or $savedCatalog.LoginServers[0].MaxCharactersPerAccount -ne 3) { throw 'New defaults replaced a saved server catalog or restored removed entries.' }
$emptyCatalog = $deserializeSettings.Invoke($null, [object[]]@('{"Version":2,"Accounts":[],"LoginServers":[]}'))
if ($emptyCatalog.LoginServers.Count -ne 0) { throw 'An intentionally empty saved server catalog must stay empty.' }
'PASS: five default servers match the requested names/endpoints/limits; saved custom and empty catalogs are preserved.'
$profileType = $assembly.GetType('AOLauncher.LoginServerProfile')
$customProfile = [Activator]::CreateInstance($profileType)
$customProfile.Id = 'custom-test'
$customProfile.Name = 'My server'
$customProfile.Server = '192.0.2.20'
$customProfile.Port = 7020
$migrated.LoginServers.Add($customProfile)
$migrated.Accounts[0].ServerProfileId = $customProfile.Id
$targetsType.GetMethod('ApplyProfile', $staticFlags).Invoke($null, [object[]]@($migrated, $migrated.Accounts[0])) | Out-Null
$catalogRoundtrip = $deserializeSettings.Invoke($null, [object[]]@($serializer.Serialize($migrated)))
if ($catalogRoundtrip.LoginServers.Count -ne 6 -or $catalogRoundtrip.Accounts[0].Server -ne '192.0.2.20' -or $catalogRoundtrip.Accounts[0].Port -ne 7020 -or $catalogRoundtrip.Accounts[0].ServerProfileId -ne 'custom-test') { throw 'Custom catalog entry or linked-account selection did not persist.' }
$migrated.LoginServers.Remove($customProfile) | Out-Null
$targetsType.GetMethod('ApplyProfile', $staticFlags).Invoke($null, [object[]]@($migrated, $migrated.Accounts[0])) | Out-Null
if ($migrated.Accounts[0].ServerProfileId -or $migrated.Accounts[0].Server -ne '192.0.2.20' -or $migrated.Accounts[0].Port -ne 7020) { throw 'Removing a catalog entry must preserve the last endpoint as custom settings.' }
'PASS: v1 migration preserves IDs/names/checks/folding/presets and copies custom targets once; editable server catalog roundtrips and deleted profiles retain their endpoint.'
$catalogChange = $targetsType.GetMethod('ChangeProfiles', $staticFlags)
$catalogConfig = $deserializeSettings.Invoke($null, [object[]]@($serializer.Serialize($catalogRoundtrip)))
$catalogConfig.Accounts[1].ServerProfileId = 'rebirth'
$targetsType.GetMethod('ApplyProfile', $staticFlags).Invoke($null, [object[]]@($catalogConfig, $catalogConfig.Accounts[1])) | Out-Null
$catalogConfig.Accounts[1].Username = $catalogConfig.Accounts[0].Username
$catalogConfig.Accounts[1].Characters[0].CharacterId = $catalogConfig.Accounts[0].Characters[0].CharacterId
foreach ($collisionKind in @('username-and-id', 'id-only')) {
    if ($collisionKind -eq 'id-only') { $catalogConfig.Accounts[1].Username = 'distinct-user' }
    $unchangedCatalog = $serializer.Serialize($catalogConfig)
    $proposedProfiles = $serializer.Deserialize($unchangedCatalog, $settingsType).LoginServers
    $proposedCustom = $proposedProfiles | Where-Object Id -eq 'custom-test'
    $proposedCustom.Server = $catalogConfig.Accounts[1].Server
    $proposedCustom.Port = $catalogConfig.Accounts[1].Port
    $changeRejected = $false
    try { $catalogChange.Invoke($null, [object[]]@($catalogConfig, $proposedProfiles)) | Out-Null } catch { $changeRejected = $_.Exception.GetBaseException() -is [ArgumentException] }
    if (!$changeRejected -or $serializer.Serialize($catalogConfig) -ne $unchangedCatalog) { throw 'Colliding catalog update must reject before changing any account, catalog entry, selection or preset.' }
}
$successProfiles = $serializer.Deserialize($serializer.Serialize($catalogConfig), $settingsType).LoginServers
$successCustom = $successProfiles | Where-Object Id -eq 'custom-test'
$successCustom.Server = '192.0.2.25'; $successCustom.Port = 7025
$oldSecondEndpoint = $catalogConfig.Accounts[1].Server + ':' + $catalogConfig.Accounts[1].Port
$catalogChange.Invoke($null, [object[]]@($catalogConfig, $successProfiles)) | Out-Null
if ($catalogConfig.Accounts[0].Server -ne '192.0.2.25' -or $catalogConfig.Accounts[0].Port -ne 7025 -or ($catalogConfig.Accounts[1].Server + ':' + $catalogConfig.Accounts[1].Port) -ne $oldSecondEndpoint) { throw 'Valid catalog edit did not propagate only to linked accounts.' }
$removedProfiles = $serializer.Deserialize($serializer.Serialize($catalogConfig), $settingsType).LoginServers
$removedProfiles.Remove(($removedProfiles | Where-Object Id -eq 'custom-test')) | Out-Null
$catalogChange.Invoke($null, [object[]]@($catalogConfig, $removedProfiles)) | Out-Null
if ($catalogConfig.Accounts[0].ServerProfileId -or $catalogConfig.Accounts[0].Server -ne '192.0.2.25' -or $catalogConfig.Accounts[0].Port -ne 7025 -or !$catalogConfig.Accounts[0].Characters[0].Selected -or $catalogConfig.Presets[0].Characters[0].AccountId -ne 'legacy-a') { throw 'Removing a server profile did not preserve its account endpoint, selection and preset refs.' }
'PASS: catalog updates propagate to linked accounts, preserve removed-profile endpoint snapshots, and reject username/character collisions atomically.'

$settingsStoreType = $assembly.GetType('AOLauncher.SettingsStore')
$readSettingsDirectory = $settingsStoreType.GetMethod('ReadFromDirectory', $staticFlags)
$saveSettingsDirectory = $settingsStoreType.GetMethod('SaveToDirectory', $staticFlags)
if (!$readSettingsDirectory -or !$saveSettingsDirectory) { throw 'Isolated settings-directory seams are missing.' }
[string]$settingsTempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
[string]$settingsFixtureName = 'AOLauncher-SettingsMigration-' + [Guid]::NewGuid().ToString('N')
[string]$settingsFixtureDirectory = [IO.Path]::GetFullPath((Join-Path $settingsTempParent $settingsFixtureName))
[string]$currentSettingsFile = Join-Path $settingsFixtureDirectory 'AONexus.json'
[string]$legacySettingsFile = Join-Path $settingsFixtureDirectory 'RebirthLauncher.json'
$settingsReadLock = $null
try {
    $missingSettings = $readSettingsDirectory.Invoke($null, [object[]]@($settingsFixtureDirectory))
    if ($serializer.Serialize($missingSettings) -cne $json -or [IO.Directory]::Exists($settingsFixtureDirectory)) { throw 'Missing settings must return defaults without creating a directory or file.' }
    [IO.Directory]::CreateDirectory($settingsFixtureDirectory) | Out-Null
    $emptySettings = $readSettingsDirectory.Invoke($null, [object[]]@($settingsFixtureDirectory))
    if ($serializer.Serialize($emptySettings) -cne $json -or [IO.Directory]::GetFileSystemEntries($settingsFixtureDirectory).Length -ne 0) { throw 'An empty settings directory must return defaults without writing anything.' }

    $legacyFixture = $serializer.DeserializeObject($legacyJson)
    $legacyFixture['Language'] = 'da'
    $legacyFixture['CollapsedServers'] = @('192.0.2.10:7010')
    $legacyFixture.Accounts[1]['GamePath'] = 'C:\isolated-explicit-game'
    $legacyFixture.Accounts[1]['Server'] = '192.0.2.21'
    $legacyFixture.Accounts[1]['Port'] = 7021
    [IO.File]::WriteAllText($legacySettingsFile, $serializer.Serialize($legacyFixture), [Text.UTF8Encoding]::new($false))
    $legacyFileBytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($legacySettingsFile))
    $legacyFileTime = [IO.File]::GetLastWriteTimeUtc($legacySettingsFile)
    $fileMigrated = $readSettingsDirectory.Invoke($null, [object[]]@($settingsFixtureDirectory))
    if ($fileMigrated.Version -ne 2 -or $fileMigrated.Language -ne 'da' -or $fileMigrated.Accounts.Count -ne 2 -or $fileMigrated.Accounts[0].Id -ne 'legacy-a' -or $fileMigrated.Accounts[1].Id -ne 'legacy-b' -or $fileMigrated.Accounts[0].Username -ne 'same-user' -or $fileMigrated.Accounts[0].Label -ne 'A' -or !$fileMigrated.Accounts[0].Collapsed -or !$fileMigrated.Accounts[0].Characters[0].Selected -or $fileMigrated.Accounts[1].Characters[0].Selected -or $fileMigrated.Accounts[0].Characters[0].Name -ne 'KeepName' -or $fileMigrated.Accounts[1].Characters[0].CharacterId -ne 702 -or $fileMigrated.SelectedPresetId -ne 'legacy-preset' -or $fileMigrated.Presets[0].Id -ne 'legacy-preset' -or $fileMigrated.Presets[0].Name -ne 'Keep preset' -or $fileMigrated.Presets[0].Characters[0].AccountId -ne 'legacy-a' -or $fileMigrated.Presets[0].Characters[0].CharacterId -ne 701 -or $fileMigrated.CollapsedServers[0] -ne '192.0.2.10:7010') { throw 'Reading the legacy filename changed account identity, user text, checks, folding, language or presets.' }
    if ($fileMigrated.Accounts[0].GamePath -ne 'C:\isolated-old-game' -or $fileMigrated.Accounts[0].Server -ne '192.0.2.10' -or $fileMigrated.Accounts[0].Port -ne 7010 -or $fileMigrated.Accounts[0].LegacyCredentialTarget -ne '192.0.2.10:7010' -or $fileMigrated.Accounts[1].GamePath -ne 'C:\isolated-explicit-game' -or $fileMigrated.Accounts[1].Server -ne '192.0.2.21' -or $fileMigrated.Accounts[1].Port -ne 7021) { throw 'Legacy filename migration lost inherited or explicit custom account targets.' }
    if ([IO.File]::Exists($currentSettingsFile) -or [IO.Directory]::GetFiles($settingsFixtureDirectory).Length -ne 1 -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($legacySettingsFile)) -cne $legacyFileBytes -or [IO.File]::GetLastWriteTimeUtc($legacySettingsFile) -ne $legacyFileTime) { throw 'Reading legacy settings must not create or alter any settings file.' }
    $migratedFileSnapshot = $serializer.Serialize($fileMigrated)
    $saveSettingsDirectory.Invoke($null, [object[]]@($fileMigrated, $settingsFixtureDirectory)) | Out-Null
    $fileSaved = $readSettingsDirectory.Invoke($null, [object[]]@($settingsFixtureDirectory))
    if (![IO.File]::Exists($currentSettingsFile) -or $serializer.Serialize($fileSaved) -cne $migratedFileSnapshot -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($legacySettingsFile)) -cne $legacyFileBytes -or [IO.File]::GetLastWriteTimeUtc($legacySettingsFile) -ne $legacyFileTime) { throw 'Saving migrated settings must write AONexus.json and leave the legacy file exactly unchanged.' }

    $fileSaved.Language = 'en'
    $fileSaved.Accounts[0].Label = 'Authoritative current account'
    $fileSaved.Accounts[0].Characters[0].Selected = $false
    $saveSettingsDirectory.Invoke($null, [object[]]@($fileSaved, $settingsFixtureDirectory)) | Out-Null
    $currentSnapshot = $serializer.Serialize($fileSaved)
    $currentFileBytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($currentSettingsFile))
    $currentFileTime = [IO.File]::GetLastWriteTimeUtc($currentSettingsFile)
    $currentWins = $readSettingsDirectory.Invoke($null, [object[]]@($settingsFixtureDirectory))
    if ($serializer.Serialize($currentWins) -cne $currentSnapshot -or $currentWins.Accounts[0].Characters[0].Selected -or $currentWins.Accounts[0].Label -ne 'Authoritative current account') { throw 'AONexus.json must take precedence over a valid but stale legacy file.' }
    [IO.File]::WriteAllText($legacySettingsFile, '{ malformed legacy fixture', [Text.UTF8Encoding]::new($false))
    if ($serializer.Serialize($readSettingsDirectory.Invoke($null, [object[]]@($settingsFixtureDirectory))) -cne $currentSnapshot) { throw 'Valid current settings must load even if the unused legacy file is malformed.' }
    [IO.File]::WriteAllBytes($legacySettingsFile, [Convert]::FromBase64String($legacyFileBytes))
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($currentSettingsFile)) -cne $currentFileBytes -or [IO.File]::GetLastWriteTimeUtc($currentSettingsFile) -ne $currentFileTime) { throw 'Reading authoritative current settings must not rewrite the file.' }

    $currentWins.Accounts[0].Label = 'Must remain unsaved while locked'
    $settingsReadLock = [IO.File]::Open($currentSettingsFile, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $lockedSaveRejected = $false
    try { $saveSettingsDirectory.Invoke($null, [object[]]@($currentWins, $settingsFixtureDirectory)) | Out-Null } catch { $lockedSaveRejected = $_.Exception.GetBaseException() -is [IO.IOException] }
    $settingsReadLock.Dispose(); $settingsReadLock = $null
    if (!$lockedSaveRejected -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($currentSettingsFile)) -cne $currentFileBytes -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($legacySettingsFile)) -cne $legacyFileBytes -or [IO.Directory]::GetFiles($settingsFixtureDirectory, '*.tmp').Length -ne 0) { throw 'A failed atomic settings replacement must preserve current/legacy files and remove its temporary file.' }
    $saveSettingsDirectory.Invoke($null, [object[]]@($currentWins, $settingsFixtureDirectory)) | Out-Null
    if ($serializer.Serialize($readSettingsDirectory.Invoke($null, [object[]]@($settingsFixtureDirectory))) -cne $serializer.Serialize($currentWins) -or [IO.Directory]::GetFiles($settingsFixtureDirectory, '*.tmp').Length -ne 0) { throw 'A subsequent atomic save must persist the new current settings without leftover temporary files.' }

    $lockedCurrentBytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($currentSettingsFile))
    $settingsReadLock = [IO.File]::Open($currentSettingsFile, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    $lockedReadRejected = $false
    try { $readSettingsDirectory.Invoke($null, [object[]]@($settingsFixtureDirectory)) | Out-Null } catch { $lockedReadRejected = $_.Exception.GetBaseException() -is [IO.IOException] }
    $settingsReadLock.Dispose(); $settingsReadLock = $null
    if (!$lockedReadRejected -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($currentSettingsFile)) -cne $lockedCurrentBytes -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($legacySettingsFile)) -cne $legacyFileBytes -or [IO.Directory]::GetFileSystemEntries($settingsFixtureDirectory).Length -ne 2) { throw 'An unreadable current settings file must fail without falling back to legacy data or modifying files.' }
    [IO.File]::Delete($currentSettingsFile)
    [IO.Directory]::CreateDirectory($currentSettingsFile) | Out-Null
    try {
        $directoryCollisionRejected = $false
        try { $readSettingsDirectory.Invoke($null, [object[]]@($settingsFixtureDirectory)) | Out-Null } catch { $directoryCollisionRejected = $true }
        if (!$directoryCollisionRejected -or ![IO.Directory]::Exists($currentSettingsFile) -or [IO.Directory]::GetFileSystemEntries($currentSettingsFile).Length -ne 0 -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($legacySettingsFile)) -cne $legacyFileBytes) { throw 'A directory occupying AONexus.json must reject without falling back or writing files.' }
    } finally { if ([IO.Directory]::Exists($currentSettingsFile)) { [IO.Directory]::Delete($currentSettingsFile, $false) } }

    foreach ($badCurrentText in @('{ malformed current fixture', 'null', '{"Version":999,"Accounts":[]}')) {
        [IO.File]::WriteAllText($currentSettingsFile, $badCurrentText, [Text.UTF8Encoding]::new($false))
        $badCurrentBytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($currentSettingsFile))
        $badCurrentRejected = $false
        try { $readSettingsDirectory.Invoke($null, [object[]]@($settingsFixtureDirectory)) | Out-Null } catch { $badCurrentRejected = $true }
        if (!$badCurrentRejected -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($currentSettingsFile)) -cne $badCurrentBytes -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($legacySettingsFile)) -cne $legacyFileBytes -or [IO.Directory]::GetFileSystemEntries($settingsFixtureDirectory).Length -ne 2) { throw 'An invalid current settings file must be rejected without falling back to valid legacy data or writing any files.' }
    }
    [IO.File]::Delete($currentSettingsFile)
    [IO.File]::WriteAllText($legacySettingsFile, '{ malformed legacy fixture', [Text.UTF8Encoding]::new($false))
    $badLegacyBytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($legacySettingsFile))
    $badLegacyRejected = $false
    try { $readSettingsDirectory.Invoke($null, [object[]]@($settingsFixtureDirectory)) | Out-Null } catch { $badLegacyRejected = $true }
    if (!$badLegacyRejected -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($legacySettingsFile)) -cne $badLegacyBytes -or [IO.File]::Exists($currentSettingsFile) -or [IO.Directory]::GetFileSystemEntries($settingsFixtureDirectory).Length -ne 1) { throw 'Malformed legacy settings must be rejected without modifying it or creating a current file.' }
    'PASS: isolated filename migration preserves identity, presets, checks, language and custom targets; reads never write, current settings win, corrupt/unreadable/current-directory collisions never fall back, legacy bytes survive saves, and atomic save succeeds or fails without data loss.'
} finally {
    if ($settingsReadLock) { $settingsReadLock.Dispose() }
    $resolvedSettingsFixture = [IO.Path]::GetFullPath($settingsFixtureDirectory)
    if ([IO.Path]::GetDirectoryName($resolvedSettingsFixture) -cne $settingsTempParent -or [IO.Path]::GetFileName($resolvedSettingsFixture) -cne $settingsFixtureName -or !$settingsFixtureName.StartsWith('AOLauncher-SettingsMigration-')) { throw 'Refusing cleanup outside the verified temporary settings fixture.' }
    if ([IO.Directory]::Exists($resolvedSettingsFixture)) {
        if (([IO.File]::GetAttributes($resolvedSettingsFixture) -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or [IO.Directory]::GetDirectories($resolvedSettingsFixture).Length -ne 0) { throw 'Refusing cleanup of an unexpected settings fixture directory.' }
        foreach ($fixtureFile in [IO.Directory]::GetFiles($resolvedSettingsFixture)) { [IO.File]::Delete($fixtureFile) }
        [IO.Directory]::Delete($resolvedSettingsFixture, $false)
    }
}

$artworkType = $assembly.GetType('AOLauncher.AppArtwork')
$resourceNames = $assembly.GetManifestResourceNames()
foreach ($resourceName in @('AOLauncher.Assets.Launcher.ico', 'AOLauncher.Assets.Launcher.png')) {
    if ($resourceNames -cnotcontains $resourceName) { throw ('Renamed launcher resource is missing: ' + $resourceName) }
    $artworkStream = $assembly.GetManifestResourceStream($resourceName)
    try { if (!$artworkStream -or $artworkStream.Length -eq 0) { throw ('Renamed launcher resource is empty: ' + $resourceName) } }
    finally { if ($artworkStream) { $artworkStream.Dispose() } }
}
$iconFormOne = [Activator]::CreateInstance([Windows.Forms.Form])
$iconFormTwo = [Activator]::CreateInstance([Windows.Forms.Form])
$headerMark = $null
$iconBitmap = $null
try {
    $artworkType.GetMethod('ApplyIcon', $staticFlags).Invoke($null, [object[]]@($iconFormOne)) | Out-Null
    $artworkType.GetMethod('ApplyIcon', $staticFlags).Invoke($null, [object[]]@($iconFormTwo)) | Out-Null
    if (!$iconFormOne.Icon -or !$iconFormTwo.Icon -or [object]::ReferenceEquals($iconFormOne.Icon, $iconFormTwo.Icon)) { throw 'Each window must receive its own valid renamed embedded icon.' }
    $iconFormOne.Dispose()
    $iconBitmap = $iconFormTwo.Icon.ToBitmap()
    if ($iconBitmap.Width -le 0 -or $iconBitmap.Height -le 0) { throw 'Disposing one window invalidated another window icon.' }
    $headerMark = $artworkType.GetMethod('CreateHeaderMark', $staticFlags).Invoke($null, [object[]]@())
    if (!$headerMark.Image -or $headerMark.Image.Width -ne 512 -or $headerMark.Image.Height -ne 512 -or $headerMark.AccessibleName -ne 'AO Nexus') { throw 'Header artwork no longer resolves the renamed embedded PNG.' }
    $headerMark.Image.GetPixel(128, 128) | Out-Null
    'PASS: new AOLauncher embedded ICO/PNG resource names resolve; detached per-window icons and the 512px header image remain usable after their resource streams close.'
} finally {
    if ($iconBitmap) { $iconBitmap.Dispose() }
    if ($headerMark) { $headerMark.Dispose() }
    $iconFormOne.Dispose()
    $iconFormTwo.Dispose()
}

function Invoke-Layout($control) {
    $control.PerformLayout()
    foreach ($child in $control.Controls) { Invoke-Layout $child }
}
function Find-TreeNode($nodes, $tag) {
    foreach ($node in $nodes) {
        if ([object]::ReferenceEquals($node.Tag, $tag)) { return $node }
        $found = Find-TreeNode $node.Nodes $tag
        if ($null -ne $found) { return $found }
    }
    return $null
}
function Get-ServerNodeValue($node, [string]$name) {
    return $node.Tag.GetType().GetProperty($name, [Reflection.BindingFlags]'Public,NonPublic,Instance').GetValue($node.Tag, $null)
}
function Find-SettingsButton($control) {
    if ($control -is [Windows.Forms.Button] -and $control.Text -in @('Settings', 'Indstillinger')) { return $control }
    foreach ($child in $control.Controls) {
        $found = Find-SettingsButton $child
        if ($null -ne $found) { return $found }
    }
    return $null
}
function Test-LabelLayout($control) {
    foreach ($child in $control.Controls) {
        # Collapsed detail content and inactive tabs are intentionally not laid out for display.
        if (!$child.Visible) { continue }
        if ($child -is [Windows.Forms.Label] -and $child.Text.Length -gt 0) {
            $measured = [Windows.Forms.TextRenderer]::MeasureText($child.Text, $child.Font, [Drawing.Size]::new($child.Width, 1000), [Windows.Forms.TextFormatFlags]::WordBreak)
            if ($measured.Height -gt $child.Height) { throw ('Clipped label (' + $child.Width + 'x' + $child.Height + ', measured ' + $measured.Width + 'x' + $measured.Height + '): ' + $child.Text) }
        }
        Test-LabelLayout $child
    }
}
function Test-EditorButtons($dialog) {
    $editor = $dialog.GetType().BaseType
    foreach ($fieldName in @('SaveButton', 'CancelAction')) {
        $button = $editor.GetField($fieldName, [Reflection.BindingFlags]'NonPublic,Instance').GetValue($dialog)
        if (!$button.Parent.ClientRectangle.Contains($button.Bounds)) {
            throw "$($dialog.GetType().Name) $fieldName is clipped: $($button.Bounds) in $($button.Parent.ClientRectangle)."
        }
        if ($button.Height -lt $button.PreferredSize.Height) {
            throw "$($dialog.GetType().Name) $fieldName is shorter than its preferred height."
        }
    }
}
function Test-ButtonBounds($control) {
    foreach ($child in $control.Controls) {
        if (!$child.Visible) { continue }
        if ($child -is [Windows.Forms.Button] -and !$child.Parent.ClientRectangle.Contains($child.Bounds) -and
            $child.Parent -is [Windows.Forms.ScrollableControl] -and $child.Parent.AutoScroll) {
            $child.Parent.ScrollControlIntoView($child)
            $child.Parent.PerformLayout()
        }
        if ($child -is [Windows.Forms.Button] -and !$child.Parent.ClientRectangle.Contains($child.Bounds)) {
            throw ('Button outside parent: ' + $child.Text + ' ' + $child.Bounds + ' in ' + $child.Parent.ClientRectangle)
        }
        Test-ButtonBounds $child
    }
}

function Set-DetectedTarget($dialog, [hashtable]$values, [bool]$explicitRequest = $false) {
    $type = $dialog.GetType()
    $suggestion = $null
    if ($null -ne $values) {
        $suggestionType = $assembly.GetType('AOLauncher.PrefsTargetSuggestion')
        $suggestion = [Activator]::CreateInstance($suggestionType)
        foreach ($entry in $values.GetEnumerator()) { $suggestionType.GetProperty($entry.Key).SetValue($suggestion, $entry.Value, $null) }
    }
    $type.GetField('targetSuggestion', $flags).SetValue($dialog, $suggestion)
    $type.GetMethod('ApplyDetectedTarget', $flags).Invoke($dialog, [object[]]@($explicitRequest)) | Out-Null
}
function Select-ServerProfile($dialog, [int]$index) {
    $picker = $dialog.GetType().GetField('serverProfile', $flags).GetValue($dialog)
    $picker.SelectedIndex = $index
    $picker.GetType().GetMethod('OnSelectionChangeCommitted', $flags).Invoke($picker, [object[]]@([EventArgs]::Empty)) | Out-Null
}
function Test-DetectionHints($dialog, [string]$language) {
    $type = $dialog.GetType()
    $tabs = $type.GetField('accountTabs', $flags).GetValue($dialog)
    $tabs.SelectedIndex = 1
    Select-ServerProfile $dialog 5
    $status = $type.GetField('targetStatus', $flags).GetValue($dialog)
    $cases = @(
        @{ Values=$null; English='Choose Prefs to detect'; Danish='Vælg Prefs for at finde' },
        @{ Values=@{ FromSavedAccount=$true }; English='Server matched to the saved setup'; Danish='Serveren matcher den gemte opsætning' },
        @{ Values=@{ DimensionName='Project Rubi-Ka — Ithaca' }; English='Last server selected in AO:'; Danish='Senest valgte server i AO:' },
        @{ Values=@{}; English='The server could not be detected.'; Danish='Serveren kunne ikke findes.' }
    )
    foreach ($case in $cases) {
        Set-DetectedTarget $dialog $case.Values
        $expected = if ($language -eq 'da') { $case.Danish } else { $case.English }
        if (!$status.Text.StartsWith($expected)) { throw ('Detection hint is not localized: ' + $status.Text) }
        Invoke-Layout $dialog
        Test-LabelLayout $tabs.SelectedTab
        Test-ButtonBounds $dialog
    }
}

$candidateType = $assembly.GetType('AOLauncher.PrefsCandidate')
$pickerType = $assembly.GetType('AOLauncher.PrefsPickerDialog')
$pickerCandidates = [Array]::CreateInstance($candidateType, 5)
$candidateData = @(
    @{ InstallationName='Matched client'; AccountName='WantedAccount'; PrefsPath='C:\SyntheticPrefs\A\Prefs\WantedAccount'; GamePath='C:\SyntheticGames\Matched'; CharacterCount=3; GameExists=$true; MatchesGamePath=$true },
    @{ InstallationName='Other client'; AccountName='AnotherAccount'; PrefsPath='C:\SyntheticPrefs\B\Prefs\AnotherAccount'; GamePath='C:\SyntheticGames\Other'; CharacterCount=2; GameExists=$true },
    @{ InstallationName='Removed client'; AccountName='DifferentAccount'; PrefsPath='C:\SyntheticPrefs\C\Prefs\DifferentAccount'; GamePath='C:\SyntheticGames\Removed'; CharacterCount=4; GameExists=$false },
    @{ InstallationName='Matching folder name'; AccountName='PossibleAccount'; PrefsPath='C:\SyntheticPrefs\D\Prefs\PossibleAccount'; CharacterCount=1; MatchesGameFolderName=$true },
    @{ InstallationName='Unverified client'; AccountName='UnknownAccount'; PrefsPath='C:\SyntheticPrefs\E\Prefs\UnknownAccount'; CharacterCount=5 }
)
for ($index = 0; $index -lt $candidateData.Count; $index++) {
    $candidate = [Activator]::CreateInstance($candidateType)
    foreach ($entry in $candidateData[$index].GetEnumerator()) { $candidateType.GetProperty($entry.Key).SetValue($candidate, $entry.Value, $null) }
    $pickerCandidates.SetValue($candidate, $index)
}
function Get-PickerCandidateSnapshot {
    $values = New-Object 'System.Collections.Generic.List[string]'
    foreach ($item in $pickerCandidates) {
        foreach ($property in ($candidateType.GetProperties() | Sort-Object Name)) {
            $values.Add($property.Name + ':' + $serializer.Serialize($property.GetValue($item, $null)))
        }
    }
    return [string]::Join('|', $values)
}
$pickerCandidateSnapshot = Get-PickerCandidateSnapshot

$formType = $assembly.GetType('AOLauncher.MainForm')
$form = [Activator]::CreateInstance($formType, [object[]]@($true))
try {
    $tree = $formType.GetField('tree', $flags).GetValue($form)
    $selected = $formType.GetField('selectionLabel', $flags).GetValue($form)
    $config = $formType.GetField('settings', $flags).GetValue($form)
    # Seed the original two-account behavioral fixture explicitly. Nexus's richer
    # three-server preview data is presentation content, not this test's input.
    while ($config.Accounts.Count -gt 2) { $config.Accounts.RemoveAt($config.Accounts.Count - 1) }
    $config.Accounts[0].Characters.RemoveAt(2)
    $config.Accounts[0].Characters[0].Selected = $true
    $config.Accounts[0].Characters[1].Selected = $false
    $config.Accounts[1].Characters[0].Selected = $true
    foreach ($fixtureAccount in $config.Accounts) {
        $fixtureAccount.ServerProfileId = 'rebirth'; $fixtureAccount.Collapsed = $false
        $targetsType.GetMethod('ApplyProfile', $staticFlags).Invoke($null, [object[]]@($config, $fixtureAccount)) | Out-Null
    }
    $config.Presets.Clear(); $config.SelectedPresetId = $null; $config.CollapsedServers.Clear()
    $formType.GetMethod('RefreshPresetPicker', $flags).Invoke($form, @()) | Out-Null
    $formType.GetMethod('RefreshTree', $flags).Invoke($form, [object[]]@($null)) | Out-Null
    if ($tree.Nodes.Count -ne 1 -or $tree.Nodes[0].Nodes.Count -ne 2 -or $selected.Text -notmatch '^2 characters selected') { throw 'Smoke sample data, server grouping or initial selection incorrect.' }
    $firstAccountNode = Find-TreeNode $tree.Nodes $config.Accounts[0]
    $firstAccountNode.Checked = $true
    if ($config.Accounts[0].Characters.Where({$_.Selected}).Count -ne 2 -or $selected.Text -notmatch '^3 characters selected') {
        throw 'Checking parent did not select all children and update counter.'
    }
    (Find-TreeNode $tree.Nodes $config.Accounts[0].Characters[0]).Checked = $false
    if ($firstAccountNode.Checked -or $firstAccountNode.Parent.Checked -or $selected.Text -notmatch '^2 characters selected') { throw 'Child toggle did not update account/server parents and counter.' }
    if ($formType.GetField('startButton', $flags).GetValue($form).Enabled) { throw 'Smoke mode must not permit backend launch.' }
    $jsonAccounts = $serializer.Serialize($config)

    $null = $tree.Handle # Expansion events are delivered by the native tree control.
    $selectionBeforeCollapse = $serializer.Serialize(@($config.Accounts | ForEach-Object { $_.Characters | ForEach-Object { $_.Selected } }))
    $collapseMethod = $formType.GetMethod('SetAccountsCollapsed', $flags)
    $collapseMethod.Invoke($form, [object[]]@($true)) | Out-Null
    if (@($tree.Nodes | Where-Object IsExpanded).Count -ne 0 -or @($config.Accounts | Where-Object { !$_.Collapsed }).Count -ne 0 -or $config.CollapsedServers.Count -ne 1) {
        throw 'Collapse all did not hide server/account children and remember both expansion states.'
    }
    $persistedCollapse = $serializer.Deserialize($serializer.Serialize($config), $settingsType)
    if (@($persistedCollapse.Accounts | Where-Object { !$_.Collapsed }).Count -ne 0 -or $persistedCollapse.CollapsedServers.Count -ne 1) { throw 'Collapsed server/account state did not survive JSON roundtrip.' }
    $formType.GetMethod('RefreshTree', $flags).Invoke($form, [object[]]@($null)) | Out-Null
    if (@($tree.Nodes | Where-Object IsExpanded).Count -ne 0) { throw 'Refreshing the tree reopened collapsed accounts.' }
    $tree.Nodes[0].Expand()
    if ($config.CollapsedServers.Count -ne 0 -or @($config.Accounts | Where-Object { !$_.Collapsed }).Count -ne 0) { throw 'Expanding a server must remember that group state without expanding its account children.' }
    $firstAccountNode = Find-TreeNode $tree.Nodes $config.Accounts[0]
    $firstAccountNode.Expand()
    if ($config.Accounts[0].Collapsed -or !$config.Accounts[1].Collapsed) { throw 'Opening one account changed another account or failed to remember its state.' }
    $firstAccountNode.Collapse()
    if (!$config.Accounts[0].Collapsed) { throw 'Closing one account did not remember its state.' }
    $collapseMethod.Invoke($form, [object[]]@($false)) | Out-Null
    if (@($tree.Nodes | Where-Object { !$_.IsExpanded }).Count -ne 0 -or @($config.Accounts | Where-Object Collapsed).Count -ne 0 -or $config.CollapsedServers.Count -ne 0) { throw 'Expand all did not open and remember every server and account.' }
    $selectionAfterCollapse = $serializer.Serialize(@($config.Accounts | ForEach-Object { $_.Characters | ForEach-Object { $_.Selected } }))
    if ($selectionAfterCollapse -ne $selectionBeforeCollapse) { throw 'Expanding or collapsing changed character checkboxes.' }
    if (!$tree.ShowPlusMinus -or !$tree.ShowRootLines) { throw 'Account expansion controls must be visible.' }
    'PASS: individual/all account folding persists, survives refresh and leaves character checkboxes unchanged.'
    $roundtripAccounts = $serializer.Deserialize($jsonAccounts, $settingsType)
    if ($roundtripAccounts.Accounts.Count -ne 2 -or $roundtripAccounts.Accounts[0].Characters.Count -ne 2 -or $jsonAccounts -match '"Password"\s*:') {
        throw 'Account/character JSON roundtrip or password exclusion failed.'
    }
    'PASS: two-account JSON roundtrip, parent/child checkbox behavior, selection counts, and disabled backend in smoke mode.'
    $pendingCharacter = $config.Accounts[0].Characters[0]
    $pendingCharacter.NamePending = $true
    $pendingCharacter.Selected = $true
    $pendingJson = $serializer.Serialize($config)
    $pendingRoundtrip = $serializer.Deserialize($pendingJson, $settingsType)
    if (!$pendingRoundtrip.Accounts[0].Characters[0].NamePending) { throw 'NamePending did not persist through JSON roundtrip.' }
    $characterDialogType = $assembly.GetType('AOLauncher.CharacterDialog')
    $pendingDialog = [Activator]::CreateInstance($characterDialogType, [object[]]@('Eksempelkonto', $pendingCharacter))
    try {
        if (!$pendingDialog.NamePending -or $pendingDialog.CharacterName -ne ('Character ' + $pendingCharacter.CharacterId)) {
            throw 'A blank optional name must produce the ID placeholder and NamePending=true.'
        }
        if ($characterDialogType.GetField('idInput', $flags).GetValue($pendingDialog).Text -ne $pendingCharacter.CharacterId.ToString()) {
            throw 'Editing a pending name must preserve the imported Character ID.'
        }
        $characterDialogType.GetField('nameInput', $flags).GetValue($pendingDialog).Text = 'Mit eget navn'
        if ($pendingDialog.NamePending -or $pendingDialog.CharacterName -ne 'Mit eget navn') { throw 'An entered label must remain available before login.' }
    } finally { $pendingDialog.Dispose() }
    'PASS: optional blank names use ID placeholders, user labels remain intact, and NamePending persists in JSON.'
    $eventMethod = $formType.GetMethod('TryApplyCharacterEvent', [Reflection.BindingFlags]'NonPublic,Static')
    $request = [Activator]::CreateInstance($assembly.GetType('AOLauncher.LaunchRequest'))
    $requested = [Activator]::CreateInstance($assembly.GetType('AOLauncher.LaunchCharacter'))
    $requested.Account = $config.Accounts[0].Username
    $requested.AccountId = $config.Accounts[0].Id
    $requested.Server = $config.Accounts[0].Server
    $requested.Port = $config.Accounts[0].Port
    $requested.CharacterId = $pendingCharacter.CharacterId
    $request.Characters.Add($requested)
    $pendingCharacter.Name = 'Min midlertidige label'
    foreach ($line in @(
        ('AOQL_CHARACTER {"CharacterId":' + $config.Accounts[0].Characters[1].CharacterId + ',"CharacterName":"Forkert"}'),
        'AOQL_CHARACTER {not-json}',
        'AOQL_CHARACTER null',
        ('AOQL_CHARACTER {"CharacterId":' + $pendingCharacter.CharacterId + ',"CharacterName":""}'),
        ('AOQL_CHARACTER {"CharacterId":' + $pendingCharacter.CharacterId + ',"CharacterName":"Bad\nName"}')
    )) {
        $eventArgs = [object[]]@($line, $request, $config, $null)
        if ($eventMethod.Invoke($null, $eventArgs)) { throw 'An invalid or unrequested character event was accepted.' }
        if ($pendingCharacter.Name -ne 'Min midlertidige label' -or !$pendingCharacter.NamePending) { throw 'Rejected event changed the stored label.' }
    }
    $validLine = 'AOQL_CHARACTER ' + $serializer.Serialize(@{ AccountId = $requested.AccountId; CharacterId = $pendingCharacter.CharacterId; CharacterName = 'FoundName' })
    $requested.Account = 'wrong-account'
    if ($eventMethod.Invoke($null, [object[]]@($validLine, $request, $config, $null))) { throw 'An event for a mismatched account was accepted.' }
    $requested.Account = $config.Accounts[0].Username
    $eventArgs = [object[]]@($validLine, $request, $config, $null)
    if (!$eventMethod.Invoke($null, $eventArgs) -or $pendingCharacter.Name -ne 'FoundName' -or $pendingCharacter.NamePending) {
        throw 'A valid event did not update the actual name and clear NamePending.'
    }
    $handleLine = $formType.GetMethod('HandleBackendLine', $flags)
    $handleLine.Invoke($form, [object[]]@('AOQL_CHARACTER {secret-marker-not-json}', $request)) | Out-Null
    $handleLine.Invoke($form, [object[]]@($validLine, $request)) | Out-Null
    $logText = $formType.GetField('log', $flags).GetValue($form).Text
    if ($logText -match 'AOQL_CHARACTER|secret-marker-not-json|CharacterId') { throw 'Raw protocol JSON reached the visible log.' }
    if ((Find-TreeNode $tree.Nodes $pendingCharacter).Text -ne 'FoundName') { throw 'A valid name event did not refresh the tree.' }
    'PASS: malformed/unrequested/mismatched-account events preserve labels; a valid event updates name and tree without raw JSON in the log.'
    $stopButton = $formType.GetField('stopButton', $flags).GetValue($form)
    if ($stopButton.Enabled) { throw 'Stop button must be disabled while idle.' }
    $formType.GetMethod('SetRunning', $flags).Invoke($form, [object[]]@($true)) | Out-Null
    if (!$stopButton.Enabled -or $tree.Enabled) { throw 'Running mode must enable Stop and disable editing.' }
    $formType.GetMethod('StopLogin', $flags).Invoke($form, [object[]]@()) | Out-Null
    if ($stopButton.Enabled -or !$formType.GetField('stopRequested', $flags).GetValue($form)) { throw 'Stop must set its pending flag and disable repeated stop requests.' }
    $formType.GetMethod('SetRunning', $flags).Invoke($form, [object[]]@($false)) | Out-Null
    if (!$tree.Enabled -or $stopButton.Enabled) { throw 'Finishing must restore editing and disable Stop.' }
    'PASS: running/idle controls and Stop flag transition correctly without creating a process.'
    $mergeSettings = $serializer.Deserialize($jsonAccounts, $settingsType)
    $mergeAccount = $mergeSettings.Accounts[0]
    $mergeAccount.PrefsPath = 'C:\isolated-verification\Prefs\ExampleAccount'
    $originalCharacter = $mergeAccount.Characters[0]
    $originalName = $originalCharacter.Name
    $originalSelected = $originalCharacter.Selected
    $mergeMethod = $formType.GetMethod('TryMergeImportedCharacters', [Reflection.BindingFlags]'NonPublic,Static')
    $mergeArguments = [object[]]@($mergeSettings, $mergeAccount, [int[]]@($originalCharacter.CharacterId, 910001, 910001), $null)
    if (!$mergeMethod.Invoke($null, $mergeArguments)) { throw 'Valid Prefs import failed.' }
    $imported = $mergeAccount.Characters | Where-Object { $_.CharacterId -eq 910001 }
    if ($mergeAccount.Characters.Count -ne 3 -or $imported.Selected -or !$imported.NamePending -or $imported.Name -ne 'Character 910001') {
        throw 'Imported IDs must be deduplicated, unchecked placeholders.'
    }
    if ($originalCharacter.Name -ne $originalName -or $originalCharacter.Selected -ne $originalSelected) { throw 'Import changed an existing name or selection.' }
    $conflictingId = $mergeSettings.Accounts[1].Characters[0].CharacterId
    $mergeArguments = [object[]]@($mergeSettings, $mergeAccount, [int[]]@(910002, $conflictingId), $null)
    if ($mergeMethod.Invoke($null, $mergeArguments) -or $mergeAccount.Characters.Count -ne 3 -or [string]::IsNullOrEmpty($mergeArguments[3])) {
        throw 'Cross-account ID collision must reject the entire import before adding any characters.'
    }
    $mergeRoundtrip = $serializer.Deserialize($serializer.Serialize($mergeSettings), $settingsType)
    if ($mergeRoundtrip.Accounts[0].PrefsPath -ne $mergeAccount.PrefsPath) { throw 'PrefsPath did not persist in JSON.' }
    'PASS: Prefs import preserves existing names/checks, deduplicates new unchecked placeholders, rejects cross-account collisions atomically, and persists PrefsPath.'
    $presetType = $assembly.GetType('AOLauncher.PresetService')
    $staticFlags = [Reflection.BindingFlags]'NonPublic,Static'
    $preset = $presetType.GetMethod('Create', $staticFlags).Invoke($null, [object[]]@($config, 'Save new…'))
    $formType.GetMethod('RefreshPresetPicker', $flags).Invoke($form, [object[]]@()) | Out-Null
    $picker = $formType.GetField('presetPicker', $flags).GetValue($form)
    $savedReferenceCount = $preset.Characters.Count
    $presetCharacterNode = Find-TreeNode $tree.Nodes $config.Accounts[0].Characters[0]
    $presetCharacterNode.Checked = !$presetCharacterNode.Checked
    $modifiedSelection = @($config.Accounts | ForEach-Object { $_.Characters | ForEach-Object { $_.Selected } }) -join ','
    if (!$formType.GetField('updatePresetButton', $flags).GetValue($form).Enabled -or $preset.Characters.Count -ne $savedReferenceCount) { throw 'Checkbox edits must enable Update without changing the saved preset.' }
    $formType.GetMethod('RefreshPresetPicker', $flags).Invoke($form, [object[]]@()) | Out-Null
    if ((@($config.Accounts | ForEach-Object { $_.Characters | ForEach-Object { $_.Selected } }) -join ',') -ne $modifiedSelection) { throw 'Refreshing the dropdown must not apply the saved preset over current checkboxes.' }
    $config.Accounts[0].Label = 'Settings'
    $config.Accounts[0].Characters[0].Name = 'Build your team'
    $formType.GetMethod('RefreshTree', $flags).Invoke($form, [object[]]@($config.Accounts[0].Characters[0])) | Out-Null
    $savedLog = $formType.GetField('log', $flags).GetValue($form).Text
    foreach ($language in @('da', 'en')) {
        $config.Language = $language
        $formType.GetMethod('ApplyLanguage', $flags).Invoke($form, [object[]]@()) | Out-Null
        $expectedSettings = if ($language -eq 'da') { 'Indstillinger' } else { 'Settings' }
        if ((Find-SettingsButton $form).Text -ne $expectedSettings) { throw 'Language switch did not translate the settings button.' }
        if ((Find-TreeNode $tree.Nodes $config.Accounts[0]).Text -notlike 'Settings *' -or (Find-TreeNode $tree.Nodes $config.Accounts[0].Characters[0]).Text -ne 'Build your team' -or $formType.GetField('detailTitle', $flags).GetValue($form).Text -ne 'Build your team' -or $picker.SelectedItem.ToString() -ne 'Save new…') { throw 'Language switch translated saved user names.' }
        if ($config.SelectedPresetId -ne $preset.Id -or (@($config.Accounts | ForEach-Object { $_.Characters | ForEach-Object { $_.Selected } }) -join ',') -ne $modifiedSelection) { throw 'Language switch changed preset or checkbox selection.' }
        if ($formType.GetField('log', $flags).GetValue($form).Text -ne $savedLog) { throw 'Language switch changed the existing log.' }
        Invoke-Layout $form
        Test-LabelLayout $form
        Test-ButtonBounds $form
    }
    $picker.GetType().GetMethod('OnSelectionChangeCommitted', $flags).Invoke($picker, [object[]]@([EventArgs]::Empty)) | Out-Null
    if ($formType.GetField('updatePresetButton', $flags).GetValue($form).Enabled -or $formType.GetField('running', $flags).GetValue($form)) { throw 'Choosing the same preset must restore its selection without logging in.' }
    $presetCharacterNode = Find-TreeNode $tree.Nodes $config.Accounts[0].Characters[0]
    $presetCharacterNode.Checked = !$presetCharacterNode.Checked
    $formType.GetMethod('UpdateCurrentPreset', $flags).Invoke($form, [object[]]@($null, [EventArgs]::Empty)) | Out-Null
    if ($formType.GetField('updatePresetButton', $flags).GetValue($form).Enabled) { throw 'Update must save current checkboxes and become disabled.' }
    $missingReference = $preset.Characters[0]
    $oldAccount = $config.Accounts | Where-Object { $_.Id -eq $missingReference.AccountId }
    $movedCharacter = $oldAccount.Characters | Where-Object { $_.CharacterId -eq $missingReference.CharacterId }
    $oldAccount.Characters.Remove($movedCharacter) | Out-Null
    $otherAccount = $config.Accounts | Where-Object { $_.Id -ne $oldAccount.Id } | Select-Object -First 1
    $otherAccount.Characters.Add($movedCharacter)
    $formType.GetMethod('ApplySelectedPreset', $flags).Invoke($form, [object[]]@($null, [EventArgs]::Empty)) | Out-Null
    if ($movedCharacter.Selected -or !$formType.GetField('log', $flags).GetValue($form).Text.Contains('saved characters no longer exist')) { throw 'Missing preset refs must be logged and must not select a matching ID under a different account.' }
    'PASS: preset edits show Modified without overwriting saved choices; same-preset selection restores checkboxes without login; Update captures selection; removed references cannot select another account.'
    'PASS: English/Danish immediate switches preserve current checkboxes, selected preset, log and user names matching translation keys.'
    foreach ($size in @([Drawing.Size]::new(1046,869), $form.MinimumSize)) {
        $form.Size = $size
        Invoke-Layout $form
        $button = Find-SettingsButton $form
        if ($null -eq $button -or !$button.Parent.ClientRectangle.Contains($button.Bounds)) { throw 'Settings button is outside its header.' }
        Test-LabelLayout $form
        "PASS: settings button bounds $($button.Bounds) within header $($button.Parent.ClientRectangle)."
    }
    foreach ($language in @('en', 'da')) {
      $config.Language = $language
      $formType.GetMethod('ApplyLanguage', $flags).Invoke($form, [object[]]@()) | Out-Null
      foreach ($name in @('AccountDialog', 'CharacterDialog', 'PasswordDialog', 'SettingsDialog', 'PresetNameDialog', 'LoginServersDialog', 'PrefsPickerDialog', 'SelectedTargetsDialog', 'LaunchConfirmationDialog')) {
        $type = $assembly.GetType('AOLauncher.' + $name)
        switch ($name) {
            'AccountDialog' { $arguments = [object[]]@($config.Accounts[0]) }
            'CharacterDialog' { $arguments = [object[]]@('Eksempelkonto', $null) }
            'PasswordDialog' { $arguments = [object[]]@($config.Accounts[0]) }
            'SettingsDialog' { $arguments = [object[]]@($config) }
            'PresetNameDialog' { $arguments = [object[]]@('Preset', '', [string[]]@('Existing')) }
            'LoginServersDialog' { $arguments = New-Object object[] 1; $arguments[0] = $config.LoginServers }
            'PrefsPickerDialog' { $arguments = New-Object object[] 2; $arguments[0] = $pickerCandidates; $arguments[1] = 'WantedAccount' }
            'SelectedTargetsDialog' { $arguments = [object[]]@((('Synthetic checked account — 2 characters' + "`r`nServer: Synthetic server (192.0.2.50:7050)`r`nGame folder: C:\SyntheticGames\" + ('LongFolderName' * 12) + "`r`n`r`n") * 30)) }
            'LaunchConfirmationDialog' { $arguments = [object[]]@($formType.GetMethod('BuildLaunchConfirmation', $staticFlags).Invoke($null, [object[]]@($config))) }
        }
        $dialog = [Activator]::CreateInstance($type, $arguments)
        try {
            if ($name -eq 'LaunchConfirmationDialog') {
                $modelBeforeCancel = $serializer.Serialize($config)
                $fields = $type.BaseType.GetField('Fields', $flags).GetValue($dialog)
                $summaryLabels = @($fields.Controls | Where-Object { $_ -is [Windows.Forms.Label] })
                $confirm = $type.BaseType.GetField('SaveButton', $flags).GetValue($dialog)
                $cancel = $type.BaseType.GetField('CancelAction', $flags).GetValue($dialog)
                $expectedConfirm = if ($language -eq 'da') { 'Log ind' } else { 'Log in' }
                if ($summaryLabels.Count -ne 1 -or $summaryLabels[0].Text -ne $arguments[0] -or $confirm.Text -ne $expectedConfirm -or $confirm.DialogResult -ne [Windows.Forms.DialogResult]::OK -or $cancel.DialogResult -ne [Windows.Forms.DialogResult]::Cancel -or ![object]::ReferenceEquals($dialog.AcceptButton, $cancel) -or ![object]::ReferenceEquals($dialog.ActiveControl, $cancel) -or ![object]::ReferenceEquals($dialog.CancelButton, $cancel)) { throw 'Login confirmation must show its full summary, localize Log in, and default Enter, initial focus and Escape to Cancel.' }
                $cancel.GetType().GetMethod('OnClick', $flags).Invoke($cancel, [object[]]@([EventArgs]::Empty)) | Out-Null
                if ($dialog.DialogResult -ne [Windows.Forms.DialogResult]::Cancel -or $serializer.Serialize($config) -ne $modelBeforeCancel -or $formType.GetField('running', $flags).GetValue($form)) { throw 'Cancelling login confirmation must preserve settings, selections and idle state.' }
            }
            if ($name -eq 'SelectedTargetsDialog') {
                $fields = $type.BaseType.GetField('Fields', $flags).GetValue($dialog)
                $pendingControls = [Collections.Generic.Queue[Windows.Forms.Control]]::new()
                $pendingControls.Enqueue($fields)
                $textArea = @(while ($pendingControls.Count -gt 0) {
                    $candidateControl = $pendingControls.Dequeue()
                    if ($candidateControl -is [Windows.Forms.TextBox]) { $candidateControl }
                    foreach ($childControl in $candidateControl.Controls) { $pendingControls.Enqueue($childControl) }
                })
                $close = $type.BaseType.GetField('SaveButton', $flags).GetValue($dialog)
                $expectedClose = if ($language -eq 'da') { 'Luk' } else { 'Close' }
                if ($textArea.Count -ne 1 -or $textArea[0].Text -ne $arguments[0] -or !$textArea[0].ReadOnly -or !$textArea[0].Multiline -or $textArea[0].WordWrap -or $textArea[0].ScrollBars -ne [Windows.Forms.ScrollBars]::Both -or $close.Text -ne $expectedClose -or $close.DialogResult -ne [Windows.Forms.DialogResult]::OK -or ![object]::ReferenceEquals($dialog.CancelButton, $close)) { throw 'Selected-target details must retain long text, support both scroll directions, remain read-only, and close with a localized button or Escape.' }
            }
            if ($name -eq 'PresetNameDialog') {
                $nameField = $type.GetField('nameInput', $flags).GetValue($dialog)
                $saveAction = $type.BaseType.GetField('SaveButton', $flags).GetValue($dialog)
                foreach ($invalidName in @('', ' existing ')) {
                    $nameField.Text = $invalidName
                    $saveAction.GetType().GetMethod('OnClick', $flags).Invoke($saveAction, [object[]]@([EventArgs]::Empty)) | Out-Null
                    if ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::OK) { throw 'Blank or duplicate preset name was accepted.' }
                }
                $nameField.Text = '  My team  '
                $saveAction.GetType().GetMethod('OnClick', $flags).Invoke($saveAction, [object[]]@([EventArgs]::Empty)) | Out-Null
                if ($dialog.DialogResult -ne [Windows.Forms.DialogResult]::OK -or $dialog.PresetName -ne 'My team') { throw 'Valid preset name was not trimmed and accepted.' }
            }
            if ($name -eq 'SettingsDialog') {
                $originalServerName = $config.LoginServers[0].Name
                $dialog.LoginServers[0].Name = 'Unsaved server edit'
                if ($config.LoginServers[0].Name -ne $originalServerName) { throw 'Settings must clone the server catalog so Cancel cannot change live settings.' }
                $languageField = $type.GetField('languageInput', $flags).GetValue($dialog)
                if ($dialog.Language -ne $language) { throw 'Settings did not initialize the saved language choice.' }
                for ($choice = 0; $choice -lt 3; $choice++) {
                    $languageField.SelectedIndex = $choice
                    if ($dialog.Language -ne @('auto', 'da', 'en')[$choice]) { throw 'Language choice does not return its stable preference code.' }
                }
            }
            if ($name -eq 'LoginServersDialog') {
                $grid = $type.GetField('grid', $flags).GetValue($dialog)
                $saveAction = $type.BaseType.GetField('SaveButton', $flags).GetValue($dialog)
                $originalServer = $config.LoginServers[0].Server
                $grid.Rows[0].Cells[1].Value = '192.0.2.50'
                $grid.Rows[0].Cells[2].Value = '65536'
                $saveAction.GetType().GetMethod('OnClick', $flags).Invoke($saveAction, [object[]]@([EventArgs]::Empty)) | Out-Null
                if ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::OK -or $config.LoginServers[0].Server -ne $originalServer) { throw 'Invalid catalog edit was accepted or changed original data.' }
                $grid.Rows[0].Cells[2].Value = '7050'
                $saveAction.GetType().GetMethod('OnClick', $flags).Invoke($saveAction, [object[]]@([EventArgs]::Empty)) | Out-Null
                if ($dialog.DialogResult -ne [Windows.Forms.DialogResult]::OK -or $dialog.LoginServers[0].Server -ne '192.0.2.50' -or $dialog.LoginServers[0].Port -ne 7050 -or $dialog.LoginServers[0].Id -ne $config.LoginServers[0].Id -or $config.LoginServers[0].Server -ne $originalServer) { throw 'Catalog editor must validate and return edited copies with stable IDs.' }
            }
            if ($name -eq 'AccountDialog') {
                if ($dialog.GamePath -ne $config.Accounts[0].GamePath) { throw 'Editing an existing account must preserve its saved game folder.' }
                $noCharacters = $assembly.GetType('AOLauncher.PrefsImporter').GetMethod('NoCharacters', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, [object[]]@())
                $assembly.GetType('AOLauncher.EditorDialog').GetField('ErrorLabel', $flags).GetValue($dialog).Text = $noCharacters.Message
                $tabs = $type.GetField('accountTabs', $flags).GetValue($dialog)
                $null = $dialog.Handle
                $null = $tabs.Handle # Native tab pages otherwise retain their uninitialized 200x100 bounds.
                foreach ($page in $tabs.TabPages) { $tabs.SelectedTab = $page; Invoke-Layout $dialog; Test-LabelLayout $page }
                Test-ButtonBounds $dialog
                Test-DetectionHints $dialog $language
            }
            if ($name -eq 'PrefsPickerDialog') {
                $folders = $type.GetField('folders', $flags).GetValue($dialog)
                $details = $type.GetField('folderDetails', $flags).GetValue($dialog)
                $saveAction = $type.BaseType.GetField('SaveButton', $flags).GetValue($dialog)
                $null = $dialog.Handle
                $null = $folders.Handle
                $type.GetMethod('OnShown', $flags).Invoke($dialog, [object[]]@([EventArgs]::Empty)) | Out-Null
                if ($folders.SelectedRows.Count -ne 0 -or $saveAction.Enabled -or $dialog.SelectedCandidate -or !$folders.ReadOnly) { throw 'Prefs picker must start read-only with no selection and disabled acceptance.' }
                $saveAction.GetType().GetMethod('OnClick', $flags).Invoke($saveAction, [object[]]@([EventArgs]::Empty)) | Out-Null
                if ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::OK -or $dialog.SelectedCandidate) { throw 'Prefs picker accepted without an explicit row choice.' }
                $expectedStatuses = if ($language -eq 'da') { @('Matcher den valgte spilmappe', 'Installeret', 'Spilinstallationen mangler', 'Samme mappenavn — ikke bekræftet', 'Installation ikke bekræftet') } else { @('Matches selected game folder', 'Installed', 'Game installation missing', 'Same folder name — unverified', 'Installation not verified') }
                for ($index = 0; $index -lt $expectedStatuses.Count; $index++) {
                    if ($folders.Rows[$index].Cells[3].Value -ne $expectedStatuses[$index] -or $folders.Rows[$index].Cells[0].Value -ne $pickerCandidates[$index].InstallationName) { throw ('Prefs picker row ' + $index + ' in ' + $language + ': expected ' + $expectedStatuses[$index] + ', got ' + $folders.Rows[$index].Cells[3].Value + '; installation=' + $folders.Rows[$index].Cells[0].Value) }
                }
                $folders.CurrentCell = $folders.Rows[2].Cells[0]
                $folders.Rows[2].Selected = $true
                if (!$saveAction.Enabled -or !$details.Text.Contains($pickerCandidates[2].PrefsPath) -or !$details.Text.Contains($pickerCandidates[2].GamePath) -or !$details.Text.Contains('WantedAccount') -or !$details.Text.Contains('DifferentAccount')) { throw 'Selected Prefs row must show full paths and the username mismatch without applying it.' }
                if ($dialog.SelectedCandidate) { throw 'Selecting a row must not accept or import it automatically.' }
                $saveAction.GetType().GetMethod('OnClick', $flags).Invoke($saveAction, [object[]]@([EventArgs]::Empty)) | Out-Null
                if ($dialog.DialogResult -ne [Windows.Forms.DialogResult]::OK -or ![object]::ReferenceEquals($dialog.SelectedCandidate, $pickerCandidates[2])) { throw 'Use folder did not return exactly the explicitly selected candidate.' }
                if ((Get-PickerCandidateSnapshot) -ne $pickerCandidateSnapshot) { throw 'Prefs picker changed its input candidates.' }
            }
            Invoke-Layout $dialog
            Test-LabelLayout $dialog
            Test-EditorButtons $dialog
            "PASS: $language $name labels fit their allocated height."
            $dialog.Font = [Drawing.Font]::new($dialog.Font.FontFamily, $dialog.Font.Size * 1.5, $dialog.Font.Style)
            $dialog.Scale([Drawing.SizeF]::new(1.5, 1.5))
            Invoke-Layout $dialog
            Test-EditorButtons $dialog
            if ($name -eq 'AccountDialog') { Test-ButtonBounds $dialog; Test-DetectionHints $dialog $language }
            "PASS: $language $name Save/Cancel buttons fit at normal and simulated 150% DPI scale."
        } finally { $dialog.Dispose() }
      }
      $cancelArguments = New-Object object[] 2; $cancelArguments[0] = $pickerCandidates; $cancelArguments[1] = 'WantedAccount'
      $cancelPicker = [Activator]::CreateInstance($pickerType, $cancelArguments)
      try {
          $cancelFolders = $pickerType.GetField('folders', $flags).GetValue($cancelPicker)
          $cancelFolders.Rows[1].Selected = $true
          $cancelButton = $pickerType.BaseType.GetField('CancelAction', $flags).GetValue($cancelPicker)
          $cancelButton.GetType().GetMethod('OnClick', $flags).Invoke($cancelButton, [object[]]@([EventArgs]::Empty)) | Out-Null
          if ($cancelPicker.DialogResult -ne [Windows.Forms.DialogResult]::Cancel -or $cancelPicker.SelectedCandidate -or (Get-PickerCandidateSnapshot) -ne $pickerCandidateSnapshot) { throw 'Cancel must return no chosen Prefs candidate and leave input data unchanged.' }
      } finally { $cancelPicker.Dispose() }
      $emptyArguments = New-Object object[] 2; $emptyArguments[0] = [Array]::CreateInstance($candidateType, 0); $emptyArguments[1] = ''
      $emptyPicker = [Activator]::CreateInstance($pickerType, $emptyArguments)
      try {
          $emptyDetails = $pickerType.GetField('folderDetails', $flags).GetValue($emptyPicker).Text
          $expectedEmpty = if ($language -eq 'da') { 'Ingen kontomapper' } else { 'No account folders' }
          if ($pickerType.BaseType.GetField('SaveButton', $flags).GetValue($emptyPicker).Enabled -or !$emptyDetails.StartsWith($expectedEmpty)) { throw 'Empty Prefs results must explain manual browsing in the chosen language and disable acceptance.' }
      } finally { $emptyPicker.Dispose() }
      "PASS: $language Prefs picker requires explicit choice, preserves candidate data on Cancel, localizes status/empty guidance and shows full paths with username mismatch hints."
    }
    $config.Language = 'en'
    $formType.GetMethod('ApplyLanguage', $flags).Invoke($form, [object[]]@()) | Out-Null
} finally { $form.Dispose() }

$groupForm = [Activator]::CreateInstance($formType, [object[]]@($true))
try {
    $groupConfig = $formType.GetField('settings', $flags).GetValue($groupForm)
    $groupTree = $formType.GetField('tree', $flags).GetValue($groupForm)
    $groupRefresh = $formType.GetMethod('RefreshTree', $flags)
    $groupCollapse = $formType.GetMethod('SetAccountsCollapsed', $flags)
    $groupConfig.Accounts.Clear()
    $groupConfig.LoginServers.Clear()
    foreach ($row in @(
        @{ Id='test-zulu'; Name='Zulu server'; Server='192.0.2.9'; Port=7500 },
        @{ Id='test-alpha'; Name='Alpha server'; Server='192.0.2.5'; Port=7500 },
        @{ Id='test-middle'; Name='Middle server'; Server='192.0.2.5'; Port=7501 }
    )) {
        $profile = [Activator]::CreateInstance($profileType)
        foreach ($entry in $row.GetEnumerator()) { $profileType.GetProperty($entry.Key).SetValue($profile, $entry.Value, $null) }
        $groupConfig.LoginServers.Add($profile)
    }
    $groupAccountType = $assembly.GetType('AOLauncher.AccountConfig')
    $groupCharacterType = $assembly.GetType('AOLauncher.CharacterConfig')
    $groupIndex = 0
    foreach ($row in @(
        @{ Label='Zulu group account'; Server='192.0.2.9'; Port=7500; ServerProfileId='test-zulu' },
        @{ Label='zulu account'; Server='192.0.2.5'; Port=7500; ServerProfileId='test-alpha' },
        @{ Label='Middle account'; Server='192.0.2.5'; Port=7501; ServerProfileId='test-middle' },
        @{ Label='Alpha account'; Server='192.0.2.5'; Port=7500; ServerProfileId=$null },
        @{ Label='Fallback account'; Server='192.0.2.20'; Port=7000; ServerProfileId=$null }
    )) {
        $account = [Activator]::CreateInstance($groupAccountType)
        foreach ($entry in $row.GetEnumerator()) { $groupAccountType.GetProperty($entry.Key).SetValue($account, $entry.Value, $null) }
        $account.Username = 'group-test-' + $groupIndex
        $account.GamePath = 'C:\SyntheticGames\Grouped'
        $characterIndex = 0
        foreach ($name in @('Zebra first', 'Alpha second', 'Middle third')) {
            $character = [Activator]::CreateInstance($groupCharacterType)
            $character.Name = $name; $character.CharacterId = 920000 + ($groupIndex * 10) + $characterIndex; $character.Selected = $false
            $account.Characters.Add($character)
            $characterIndex++
        }
        $groupConfig.Accounts.Add($account)
        $groupIndex++
    }
    $savedAccountOrder = @($groupConfig.Accounts | ForEach-Object Id) -join ','
    $linkedAlpha = $groupConfig.Accounts[1]
    $middleAccount = $groupConfig.Accounts[2]
    $customAlpha = $groupConfig.Accounts[3]
    $fallbackAccount = $groupConfig.Accounts[4]
    $groupRefresh.Invoke($groupForm, [object[]]@($null)) | Out-Null
    $null = $groupTree.Handle
    if ($groupTree.Nodes.Count -ne 4) { throw 'Server groups must merge accounts with equal endpoints and separate ports.' }
    $serverNames = [string[]]@($groupTree.Nodes | ForEach-Object { Get-ServerNodeValue $_ 'Name' })
    $sortedServerNames = [string[]]$serverNames.Clone()
    [Array]::Sort($sortedServerNames, [StringComparer]::CurrentCultureIgnoreCase)
    if (($serverNames -join '|') -ne ($sortedServerNames -join '|') -or $serverNames[0] -ne 'Alpha server' -or $serverNames[-1] -ne 'Zulu server') { throw ('Server groups are not alphabetically ordered by friendly name: ' + ($serverNames -join '|')) }
    $alphaGroup = @($groupTree.Nodes | Where-Object { (Get-ServerNodeValue $_ 'Endpoint') -eq '192.0.2.5:7500' })
    $middleGroup = @($groupTree.Nodes | Where-Object { (Get-ServerNodeValue $_ 'Endpoint') -eq '192.0.2.5:7501' })
    if ($alphaGroup.Count -ne 1 -or $middleGroup.Count -ne 1 -or $alphaGroup[0].Nodes.Count -ne 2 -or (Get-ServerNodeValue $alphaGroup[0] 'Name') -ne 'Alpha server' -or ![object]::ReferenceEquals($alphaGroup[0].Nodes[0].Tag, $customAlpha) -or ![object]::ReferenceEquals($alphaGroup[0].Nodes[1].Tag, $linkedAlpha)) { throw 'Custom/profile accounts sharing an endpoint must share its friendly group and sort alphabetically within it.' }
    $alphaGroup = $alphaGroup[0]; $middleGroup = $middleGroup[0]
    foreach ($account in $groupConfig.Accounts) {
        $node = Find-TreeNode $groupTree.Nodes $account
        if (($node.Nodes | Where-Object { $_.Tag.GetType().FullName -eq 'AOLauncher.CharacterConfig' } | ForEach-Object Text) -join '|' -ne 'Zebra first|Alpha second|Middle third') { throw 'Grouping must preserve saved character order instead of alphabetizing characters.' }
    }
    if ((@($groupConfig.Accounts | ForEach-Object Id) -join ',') -ne $savedAccountOrder) { throw 'Sorting the view must not reorder the saved accounts.' }
    'PASS: grouped-tree hierarchy sorts servers/accounts and preserves saved character/model order.'
    $alphaGroup.Checked = $true
    if (@($linkedAlpha.Characters | Where-Object { !$_.Selected }).Count -or @($customAlpha.Characters | Where-Object { !$_.Selected }).Count -or @($groupConfig.Accounts | Where-Object { $_ -ne $linkedAlpha -and $_ -ne $customAlpha } | ForEach-Object Characters | Where-Object Selected).Count) { throw 'Checking one server must select only its descendant characters.' }
    $changedCharacterNode = Find-TreeNode $groupTree.Nodes $customAlpha.Characters[0]
    $changedCharacterNode.Checked = $false
    if ($changedCharacterNode.Parent.Checked -or $alphaGroup.Checked -or !(Find-TreeNode $groupTree.Nodes $linkedAlpha).Checked) { throw 'A child toggle must synchronize its account and server without changing sibling accounts.' }
    $changedCharacterNode.Checked = $true
    if (!$changedCharacterNode.Parent.Checked -or !$alphaGroup.Checked) { throw 'Checking the final unchecked child must check both ancestor levels.' }
    $middleGroup.Checked = $true
    $alphaGroup.Checked = $false
    if (@($linkedAlpha.Characters | Where-Object Selected).Count -or @($customAlpha.Characters | Where-Object Selected).Count -or @($middleAccount.Characters | Where-Object { !$_.Selected }).Count) { throw 'Clearing one server must leave another server selection unchanged.' }
    (Find-TreeNode $groupTree.Nodes $customAlpha).Checked = $true
    if ($alphaGroup.Checked -or !$middleGroup.Checked) { throw 'A partially selected server must stay unchecked while another fully selected server stays checked.' }
    $groupTree.SelectedNode = $alphaGroup
    foreach ($field in @('editButton', 'removeButton', 'addCharacterButton')) {
        if ($formType.GetField($field, $flags).GetValue($groupForm).Enabled) { throw 'Server headings must not enable account/character editing or removal.' }
    }
    $groupTree.SelectedNode = Find-TreeNode $groupTree.Nodes $customAlpha
    if (!$formType.GetField('editButton', $flags).GetValue($groupForm).Enabled -or !$formType.GetField('removeButton', $flags).GetValue($groupForm).Enabled) { throw 'Account selection must retain Edit and Remove actions.' }
    'PASS: server/character checkbox synchronization and group action restrictions.'
    $selectedBeforeCollapse = @($groupConfig.Accounts | ForEach-Object Characters | ForEach-Object Selected) -join ','
    $groupCollapse.Invoke($groupForm, [object[]]@($true)) | Out-Null
    if ($groupConfig.CollapsedServers.Count -ne 4 -or @($groupTree.Nodes | Where-Object IsExpanded).Count -or @($groupConfig.Accounts | Where-Object { !$_.Collapsed }).Count) { throw 'Collapse all must persist every endpoint group and account expansion state.' }
    $groupRoundtrip = $deserializeSettings.Invoke($null, [object[]]@($serializer.Serialize($groupConfig)))
    if (($groupRoundtrip.CollapsedServers | Sort-Object) -join ',' -ne (($groupConfig.CollapsedServers | Sort-Object) -join ',')) { throw 'Endpoint-keyed server collapse states must survive settings serialization.' }
    $groupRefresh.Invoke($groupForm, [object[]]@($null)) | Out-Null
    if (@($groupTree.Nodes | Where-Object IsExpanded).Count) { throw 'Refreshing collapsed grouped data must not reopen groups.' }
    $alphaGroup = $groupTree.Nodes | Where-Object { (Get-ServerNodeValue $_ 'Endpoint') -eq '192.0.2.5:7500' }
    $alphaGroup.Expand()
    if ($groupConfig.CollapsedServers.Contains('192.0.2.5:7500') -or $groupConfig.CollapsedServers.Count -ne 3 -or !(Find-TreeNode $groupTree.Nodes $customAlpha).Tag.Collapsed) { throw 'Expanding one group must preserve every other group and account collapse state.' }
    $groupCollapse.Invoke($groupForm, [object[]]@($false)) | Out-Null
    if ($groupConfig.CollapsedServers.Count -ne 0 -or @($groupTree.Nodes | Where-Object { !$_.IsExpanded }).Count -or @($groupConfig.Accounts | Where-Object Collapsed).Count -or (@($groupConfig.Accounts | ForEach-Object Characters | ForEach-Object Selected) -join ',') -ne $selectedBeforeCollapse) { throw 'Expand all must reveal every group/account without changing checkboxes.' }
    (Find-TreeNode $groupTree.Nodes $customAlpha).Collapse($true)
    $alphaGroup.Collapse($true)
    $groupRefresh.Invoke($groupForm, [object[]]@($null)) | Out-Null
    $alphaGroup = $groupTree.Nodes | Where-Object { (Get-ServerNodeValue $_ 'Endpoint') -eq '192.0.2.5:7500' }
    if ($alphaGroup.IsExpanded -or $linkedAlpha.Collapsed -or !$customAlpha.Collapsed) { throw 'Rebuilding a collapsed server must preserve differing child account states without reopening the group.' }
    $groupTree.SelectedNode = $alphaGroup
    foreach ($language in @('da', 'en')) {
        $groupConfig.Language = $language
        $formType.GetMethod('ApplyLanguage', $flags).Invoke($groupForm, [object[]]@()) | Out-Null
        $alphaGroup = $groupTree.Nodes | Where-Object { (Get-ServerNodeValue $_ 'Endpoint') -eq '192.0.2.5:7500' }
        if ($alphaGroup.IsExpanded -or $linkedAlpha.Collapsed -or !$customAlpha.Collapsed) { throw 'Language refresh reopened a collapsed server or changed its saved child account states.' }
    }
    $alphaGroup.Expand()
    if ($linkedAlpha.Collapsed -or !$customAlpha.Collapsed) { throw 'Manually expanding a server must retain each child account expansion preference.' }
    $alphaGroup.Collapse($true)
    $groupPreset = $presetType.GetMethod('Create', $staticFlags).Invoke($null, [object[]]@($groupConfig, 'Cross-group selection'))
    $formType.GetMethod('RefreshPresetPicker', $flags).Invoke($groupForm, [object[]]@()) | Out-Null
    foreach ($node in $groupTree.Nodes) { $node.Checked = $false }
    $formType.GetMethod('ApplySelectedPreset', $flags).Invoke($groupForm, [object[]]@($null, [EventArgs]::Empty)) | Out-Null
    if ((@($groupConfig.Accounts | ForEach-Object Characters | ForEach-Object Selected) -join ',') -ne $selectedBeforeCollapse -or $groupPreset.Characters.Count -ne 6) { throw 'Applying a preset must restore references across server groups without changing selection scope.' }
    $middleGroup = $groupTree.Nodes | Where-Object { (Get-ServerNodeValue $_ 'Endpoint') -eq '192.0.2.5:7501' }
    if (!$middleGroup.Checked -or (Find-TreeNode $groupTree.Nodes $linkedAlpha).Checked) { throw 'Applying a preset must synchronize server/account checkbox state.' }
    $alphaGroup = $groupTree.Nodes | Where-Object { (Get-ServerNodeValue $_ 'Endpoint') -eq '192.0.2.5:7500' }
    if ($alphaGroup.IsExpanded -or $linkedAlpha.Collapsed -or !$customAlpha.Collapsed) { throw 'Preset refresh must preserve a collapsed server with mixed account expansion states.' }
    $groupCollapse.Invoke($groupForm, [object[]]@($true)) | Out-Null
    $customAlpha.Server = '192.0.2.9'; $customAlpha.Port = 7500; $customAlpha.ServerProfileId = 'test-zulu'
    $groupRefresh.Invoke($groupForm, [object[]]@($customAlpha)) | Out-Null
    $movedAccountNode = Find-TreeNode $groupTree.Nodes $customAlpha
    if (![object]::ReferenceEquals($groupTree.SelectedNode.Tag, $customAlpha) -or (Get-ServerNodeValue $movedAccountNode.Parent 'Endpoint') -ne '192.0.2.9:7500' -or !$movedAccountNode.Parent.IsExpanded -or @($groupTree.Nodes | Where-Object IsExpanded).Count -ne 1) { throw 'Editing an account server must move it into the correct group, reveal its row, and open only the destination group.' }
    $movedCharacter = $customAlpha.Characters[0]
    $customAlpha.Characters.Remove($movedCharacter) | Out-Null
    $middleAccount.Characters.Add($movedCharacter)
    $groupRefresh.Invoke($groupForm, [object[]]@($movedCharacter)) | Out-Null
    $movedCharacterNode = Find-TreeNode $groupTree.Nodes $movedCharacter
    if (![object]::ReferenceEquals($groupTree.SelectedNode.Tag, $movedCharacter) -or ![object]::ReferenceEquals($movedCharacterNode.Parent.Tag, $middleAccount) -or !$movedCharacterNode.Parent.IsExpanded -or !$movedCharacterNode.Parent.Parent.IsExpanded -or $middleAccount.Collapsed) { throw 'Explicitly revealing a moved character must open both ancestor levels and retain focus on that character.' }
    $middleProfile = $groupConfig.LoginServers | Where-Object Id -eq 'test-middle'
    $middleProfile.Name = 'Abacus renamed server'
    $groupRefresh.Invoke($groupForm, [object[]]@($movedCharacter)) | Out-Null
    if ((Get-ServerNodeValue $groupTree.Nodes[0] 'Name') -ne 'Abacus renamed server' -or ![object]::ReferenceEquals($groupTree.SelectedNode.Tag, $movedCharacter)) { throw ('Renaming a server profile must update group ordering without losing character focus. First group=' + (Get-ServerNodeValue $groupTree.Nodes[0] 'Name') + '; selected=' + $groupTree.SelectedNode.Text + '; expected character=' + $movedCharacter.Name) }
    $groupConfig.Language = 'da'
    $formType.GetMethod('ApplyLanguage', $flags).Invoke($groupForm, [object[]]@()) | Out-Null
    if (![object]::ReferenceEquals($groupTree.SelectedNode.Tag, $movedCharacter) -or !(Find-TreeNode $groupTree.Nodes $movedCharacter).Parent.Parent.IsExpanded) { throw 'Language refresh must preserve focused character and expanded ancestors.' }
    $fallbackNode = Find-TreeNode $groupTree.Nodes $fallbackAccount
    if ($fallbackNode.Parent.Text -notlike '*Brugerdefineret server*') { throw 'Unmatched server groups must use the localized Custom server fallback.' }
    $groupTree.SelectedNode = $fallbackNode.Parent
    $groupConfig.Language = 'en'
    $formType.GetMethod('ApplyLanguage', $flags).Invoke($groupForm, [object[]]@()) | Out-Null
    if ((Get-ServerNodeValue $groupTree.SelectedNode 'Endpoint') -ne '192.0.2.20:7000' -or $groupTree.SelectedNode.Parent -ne $null -or $groupTree.SelectedNode.Text -notlike '*Custom server*') { throw 'Language refresh must preserve a selected server by endpoint despite rebuilding group objects.' }
    'PASS: server/account sorting preserves character/model order; endpoint grouping, descendant checks, parent synchronization, persisted folding, cross-server presets, server-action restrictions, and focus/reveal after moves or language changes all work.'
} finally { $groupForm.Dispose() }

$footerForm = [Activator]::CreateInstance($formType, [object[]]@($true))
try {
    $footerConfig = $formType.GetField('settings', $flags).GetValue($footerForm)
    $footerTree = $formType.GetField('tree', $flags).GetValue($footerForm)
    $summary = $formType.GetField('pathLabel', $flags).GetValue($footerForm)
    $refreshFooter = $formType.GetMethod('RefreshTree', $flags)
    $detailsMethod = $formType.GetMethod('BuildSelectedTargetDetails', $flags)
    $one = $footerConfig.Accounts[0]
    $two = $footerConfig.Accounts[1]
    $one.Label = 'Footer checked account one'; $one.GamePath = 'C:\SyntheticGames\Shared'; $one.Server = '2.24.96.30'; $one.Port = 7500; $one.ServerProfileId = 'rebirth'
    $two.Label = 'Footer checked account two'; $two.GamePath = 'C:\SyntheticGames\Separate'; $two.Server = '2.24.96.30'; $two.Port = 7500; $two.ServerProfileId = 'rebirth'
    $uncheckedAccount = [Activator]::CreateInstance($assembly.GetType('AOLauncher.AccountConfig'))
    $uncheckedAccount.Label = 'FOCUS_ONLY_ACCOUNT'; $uncheckedAccount.Username = 'synthetic-unchecked'
    $uncheckedAccount.GamePath = 'C:\SyntheticGames\FOCUS_ONLY_PATH'; $uncheckedAccount.Server = '192.0.2.221'; $uncheckedAccount.Port = 7221
    $uncheckedCharacter = [Activator]::CreateInstance($assembly.GetType('AOLauncher.CharacterConfig'))
    $uncheckedCharacter.Name = 'Unchecked'; $uncheckedCharacter.CharacterId = 910301
    $uncheckedAccount.Characters.Add($uncheckedCharacter)
    $footerConfig.Accounts.Add($uncheckedAccount)
    foreach ($account in $footerConfig.Accounts) { foreach ($character in $account.Characters) { $character.Selected = $false } }
    $refreshFooter.Invoke($footerForm, [object[]]@($uncheckedAccount)) | Out-Null
    if ($summary -isnot [Windows.Forms.LinkLabel] -or $summary.Enabled -or $summary.Text -ne 'Select characters to see servers and game folders.') { throw 'Empty selection must show a disabled explanatory footer link.' }
    if ($detailsMethod.Invoke($footerForm, [object[]]@()) -ne '') { throw 'No selected accounts must produce no target details.' }
    $one.Characters[0].Selected = $true
    $refreshFooter.Invoke($footerForm, [object[]]@($uncheckedAccount)) | Out-Null
    if (!$summary.Enabled -or $summary.Text -ne '1 login server · 1 game folder · Details…') { throw ('Single-account footer is incorrect: ' + $summary.Text) }
    $details = $detailsMethod.Invoke($footerForm, [object[]]@())
    if (!$details.Contains($one.Label + ' — 1 character') -or !$details.Contains($one.GamePath) -or !$details.Contains('2.24.96.30:7500') -or !$details.Contains('AO Rebirth') -or $details.Contains($two.Label) -or $details.Contains('FOCUS_ONLY')) { throw 'Selected-target details must contain the selected character count, full selected-account target data and exclude unchecked accounts, even when focused.' }
    $singleSummary = $summary.Text
    $footerTree.SelectedNode = Find-TreeNode $footerTree.Nodes $two
    if ($summary.Text -ne $singleSummary) { throw 'Focusing another unchecked account changed the checked-target footer.' }
    $two.Characters[0].Selected = $true
    $refreshFooter.Invoke($footerForm, [object[]]@($uncheckedAccount)) | Out-Null
    if ($summary.Text -ne '1 login server · 2 game folders · Details…') { throw 'Two game folders sharing an endpoint must count one login server.' }
    $two.GamePath = 'c:\syntheticgames\SHARED\'
    $refreshFooter.Invoke($footerForm, [object[]]@($uncheckedAccount)) | Out-Null
    if ($summary.Text -ne '1 login server · 1 game folder · Details…') { throw 'Windows path case and a trailing slash must not create a duplicate game folder.' }
    $two.Server = '37.18.192.41'; $two.Port = 7506; $two.ServerProfileId = 'live-2019'
    $two.GamePath = 'C:\SyntheticGames\Separate'
    $refreshFooter.Invoke($footerForm, [object[]]@($uncheckedAccount)) | Out-Null
    if ($summary.Text -ne '2 login servers · 2 game folders · Details…') { throw 'Distinct checked targets must summarize all selected servers and game folders.' }
    $details = $detailsMethod.Invoke($footerForm, [object[]]@())
    if (!$details.Contains($one.Label) -or !$details.Contains($two.Label) -or !$details.Contains('37.18.192.41:7506') -or !$details.Contains($two.GamePath) -or $details.Contains('FOCUS_ONLY')) { throw 'Multiple-account details omitted a selected target or included an unchecked account.' }
    $two.GamePath = ''
    $refreshFooter.Invoke($footerForm, [object[]]@($uncheckedAccount)) | Out-Null
    if ($summary.Text -notmatch '^2 login servers · 1 game folder' -or $summary.Text -notmatch '1 missing') { throw ('Blank checked game folders need a separate missing count: ' + $summary.Text) }
    $details = $detailsMethod.Invoke($footerForm, [object[]]@())
    if (!$details.Contains('Not set')) { throw 'Details must identify a missing game folder explicitly.' }
    $two.GamePath = 'C:\SyntheticGames\Separate'
    $refreshFooter.Invoke($footerForm, [object[]]@($uncheckedAccount)) | Out-Null
    $languageCredentials = New-Object 'System.Collections.Generic.Dictionary[string,string]'
    foreach ($account in $footerConfig.Accounts) { $languageCredentials.Add($account.Id, 'synthetic-language-test') }
    $footerLog = $formType.GetField('log', $flags).GetValue($footerForm)
    $previousLog = $footerLog.Text
    foreach ($language in @('en', 'da', 'en')) {
        $footerConfig.Language = $language
        $formType.GetMethod('ApplyLanguage', $flags).Invoke($footerForm, [object[]]@()) | Out-Null
        if ($footerLog.Text -ne $previousLog) { throw 'Switching language must preserve every existing log line.' }
        $expectedSummary = if ($language -eq 'da') { '2 loginservere · 2 spilmapper · Detaljer…' } else { '2 login servers · 2 game folders · Details…' }
        if ($summary.Text -ne $expectedSummary) { throw ('Checked-target footer did not switch language: ' + $summary.Text) }
        $localizedDetails = $detailsMethod.Invoke($footerForm, [object[]]@())
        $expectedFolderCaption = if ($language -eq 'da') { 'Spilmappe: ' } else { 'Game folder: ' }
        if (!$localizedDetails.Contains($expectedFolderCaption + $one.GamePath) -or !$localizedDetails.Contains($one.Label) -or $localizedDetails.Contains('FOCUS_ONLY')) { throw 'Target detail captions must translate while selected account labels and full game paths stay unchanged.' }
        $languageRequest = $formType.GetMethod('CreateLaunchRequest', $staticFlags).Invoke($null, [object[]]@($footerConfig, $languageCredentials.psobject.BaseObject))
        if ($languageRequest.Language -ne $language -or $languageRequest.Characters.Count -ne 2) { throw 'New launch requests must carry the currently resolved language and checked selection.' }
        $formType.GetMethod('SetRunning', $flags).Invoke($footerForm, [object[]]@($true)) | Out-Null
        $formType.GetMethod('StopLogin', $flags).Invoke($footerForm, [object[]]@()) | Out-Null
        $formType.GetMethod('SetRunning', $flags).Invoke($footerForm, [object[]]@($false)) | Out-Null
        $expectedStop = if ($language -eq 'da') { 'Stopper nye login. Spilvinduerne forbliver åbne.' } else { 'Stopping new logins. Game windows remain open.' }
        if (!$footerLog.Text.StartsWith($previousLog) -or !$footerLog.Text.Substring($previousLog.Length).Contains($expectedStop)) { throw 'New Stop login messages must use the current language while preserving old log lines.' }
        $previousLog = $footerLog.Text
        $nameEvent = 'AOQL_CHARACTER ' + $serializer.Serialize(@{ AccountId=$one.Id; CharacterId=$one.Characters[0].CharacterId; CharacterName=('Detected' + $language) })
        $formType.GetMethod('HandleBackendLine', $flags).Invoke($footerForm, [object[]]@($nameEvent, $languageRequest)) | Out-Null
        $expectedNameMessage = if ($language -eq 'da') { 'loggede ind som Detectedda' } else { 'logged in as Detecteden' }
        if (!$footerLog.Text.StartsWith($previousLog) -or !$footerLog.Text.Substring($previousLog.Length).Contains($expectedNameMessage)) { throw 'New character-ready log messages must follow the changed language without rewriting history.' }
        $previousLog = $footerLog.Text
        Invoke-Layout $footerForm
        Test-LabelLayout $footerForm
    }
    'PASS: footer summarizes only checked accounts, ignores focused unchecked accounts, deduplicates endpoints/Windows paths, reports missing folders and localizes details; new log messages/requests follow language changes while history stays intact.'
} finally { $footerForm.Dispose() }

$confirmationConfig = [Activator]::CreateInstance($settingsType)
$confirmationAccountType = $assembly.GetType('AOLauncher.AccountConfig')
$confirmationCharacterType = $assembly.GetType('AOLauncher.CharacterConfig')
$confirmationMethod = $formType.GetMethod('BuildLaunchConfirmation', $staticFlags)
$setLanguage = $assembly.GetType('AOLauncher.Localization.Texts').GetMethod('SetLanguage', [Reflection.BindingFlags]'NonPublic,Static')
$confirmationIndex = 0
foreach ($row in @(
    @{ Username='same-user'; Server='192.0.2.61'; Port=7561; ServerProfileId='profile-one'; Checks=@($true, $false) },
    @{ Username='other-user'; Server='192.0.2.61'; Port=7561; ServerProfileId=$null; Checks=@($true) },
    @{ Username='same-user'; Server='192.0.2.61'; Port=7562; ServerProfileId='profile-two'; Checks=@($true, $true) },
    @{ Username='unchecked-user'; Server='192.0.2.62'; Port=7563; ServerProfileId=$null; Checks=@($false, $false) }
)) {
    $account = [Activator]::CreateInstance($confirmationAccountType)
    $account.Label = 'Confirmation account ' + $confirmationIndex
    $account.Username = $row.Username; $account.Server = $row.Server; $account.Port = $row.Port; $account.ServerProfileId = $row.ServerProfileId
    $characterIndex = 0
    foreach ($checked in $row.Checks) {
        $character = [Activator]::CreateInstance($confirmationCharacterType)
        $character.Name = 'Confirmation character ' + $characterIndex
        $character.CharacterId = 940000 + ($confirmationIndex * 10) + $characterIndex; $character.Selected = $checked
        $account.Characters.Add($character)
        $characterIndex++
    }
    $confirmationConfig.Accounts.Add($account)
    $confirmationIndex++
}
foreach ($case in @(
    @{ Name='partial accounts, repeated usernames, distinct ports'; Selection='initial'; English="Log in 3 accounts on 2 login servers?`r`n`r`n4 characters selected."; Danish="Log ind med 3 konti på 2 loginservere?`r`n`r`n4 chars valgt." },
    @{ Name='custom and profile accounts on the same endpoint'; Selection='same-endpoint'; English="Log in 2 accounts on 1 login server?`r`n`r`n2 characters selected."; Danish="Log ind med 2 konti på 1 loginserver?`r`n`r`n2 chars valgt." },
    @{ Name='singular selection'; Selection='single'; English="Log in 1 account on 1 login server?`r`n`r`n1 character selected."; Danish="Log ind med 1 konto på 1 loginserver?`r`n`r`n1 char valgt." },
    @{ Name='empty selection'; Selection='zero'; English="Log in 0 accounts on 0 login servers?`r`n`r`n0 characters selected."; Danish="Log ind med 0 konti på 0 loginservere?`r`n`r`n0 chars valgt." }
)) {
    if ($case.Selection -eq 'same-endpoint') { foreach ($character in $confirmationConfig.Accounts[2].Characters) { $character.Selected = $false } }
    if ($case.Selection -eq 'single') { $confirmationConfig.Accounts[1].Characters[0].Selected = $false }
    if ($case.Selection -eq 'zero') { $confirmationConfig.Accounts[0].Characters[0].Selected = $false }
    $beforeSummary = $serializer.Serialize($confirmationConfig)
    foreach ($language in @('en', 'da')) {
        $setLanguage.Invoke($null, [object[]]@($language)) | Out-Null
        $confirmationText = $confirmationMethod.Invoke($null, [object[]]@($confirmationConfig))
        $expectedText = if ($language -eq 'da') { $case.Danish } else { $case.English }
        if ($confirmationText -ne $expectedText -or $serializer.Serialize($confirmationConfig) -ne $beforeSummary) { throw ('Incorrect or mutating ' + $language + ' login confirmation for ' + $case.Name + ': ' + $confirmationText) }
    }
}
$setLanguage.Invoke($null, [object[]]@('en')) | Out-Null
'PASS: bilingual login confirmation counts selected accounts/characters, merges equal endpoints, separates ports, preserves repeated usernames across servers, handles singular/zero counts, and Cancel preserves data with no login.'

foreach ($language in @('en', 'da')) {
    $scaledForm = [Activator]::CreateInstance($formType, [object[]]@($true))
    try {
        $scaledConfig = $formType.GetField('settings', $flags).GetValue($scaledForm)
        $scaledConfig.Language = $language
        $formType.GetMethod('ApplyLanguage', $flags).Invoke($scaledForm, [object[]]@()) | Out-Null
        $scaledForm.Size = $scaledForm.MinimumSize
        $scaledForm.Font = [Drawing.Font]::new($scaledForm.Font.FontFamily, $scaledForm.Font.Size * 1.5, $scaledForm.Font.Style)
        $scaledForm.Scale([Drawing.SizeF]::new(1.5, 1.5))
        Invoke-Layout $scaledForm
        Test-LabelLayout $scaledForm
        Test-ButtonBounds $scaledForm
        "PASS: $language main-window labels and all button bounds fit at minimum size and simulated 150% DPI scale."
    } finally { $scaledForm.Dispose() }
}
$assembly.GetType('AOLauncher.Localization.Texts').GetMethod('SetLanguage', [Reflection.BindingFlags]'NonPublic,Static').Invoke($null, [object[]]@('en')) | Out-Null

function Wait-DialogWork($condition) {
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    while (!(& $condition)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Timed out waiting for isolated dialog scan.' }
        [Windows.Forms.Application]::DoEvents()
        [Threading.Thread]::Sleep(10)
    }
}
function Complete-AccountSave($dialog, $button) {
    # scanning describes only the nested Prefs scan. Its finally block clears the
    # flag before the async Save handler resumes to show review/errors or accept.
    # Clear the previous message so this wait observes this click's real outcome.
    $editorType.GetField('ErrorLabel', $flags).GetValue($dialog).Text = ''
    $button.GetType().GetMethod('OnClick', $flags).Invoke($button, [object[]]@([EventArgs]::Empty)) | Out-Null
    Wait-DialogWork {
        !$accountDialogType.GetField('scanning', $flags).GetValue($dialog) -and
            ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::OK -or
                ($editorType.GetField('ErrorLabel', $flags).GetValue($dialog).Text.Length -gt 0 -and
                    $accountDialogType.GetField('accountTabs', $flags).GetValue($dialog).SelectedIndex -eq 1))
    }
}
[string]$tempPrefsRoot = Join-Path ([IO.Path]::GetTempPath()) ('AOLauncher-PrefsUI-' + [Guid]::NewGuid().ToString('N'))
[string]$tempAccountPath = Join-Path $tempPrefsRoot 'ExampleAccount'
[string]$fakeGamePath = Join-Path $tempPrefsRoot 'GameClientA'
[string]$otherFakeGamePath = Join-Path $tempPrefsRoot 'GameClientB'
[string]$detectedPrefsPath = Join-Path $fakeGamePath 'Prefs'
[string]$detectedAccountPath = Join-Path $detectedPrefsPath 'DetectedAccount'
$accountDialogType = $assembly.GetType('AOLauncher.AccountDialog')
$editorType = $assembly.GetType('AOLauncher.EditorDialog')
$accountDialog = $null
$directSaveDialog = $null
$autoScanDialog = $null
$initialSaveDialog = $null
try {
    [IO.Directory]::CreateDirectory((Join-Path $tempAccountPath 'Char910101')) | Out-Null
    foreach ($gameDirectory in @($fakeGamePath, $otherFakeGamePath)) {
        [IO.Directory]::CreateDirectory($gameDirectory) | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $gameDirectory 'AnarchyOnline.exe'), [byte[]]@())
    }
    $targetConfig = $deserializeSettings.Invoke($null, [object[]]@($jsonAccounts))
    $firstTarget = $targetConfig.Accounts[0]
    $secondTarget = $targetConfig.Accounts[1]
    $firstTarget.GamePath = $fakeGamePath
    $secondTarget.GamePath = $otherFakeGamePath
    $firstTarget.Server = '192.0.2.31'; $firstTarget.Port = 7031; $firstTarget.ServerProfileId = $null
    $secondTarget.Server = '192.0.2.32'; $secondTarget.Port = 7032; $secondTarget.ServerProfileId = $null
    $secondTarget.Username = $firstTarget.Username
    foreach ($account in $targetConfig.Accounts) { foreach ($character in $account.Characters) { $character.Selected = $false } }
    $firstTarget.Characters[0].Selected = $true
    $secondTarget.Characters[0].Selected = $true
    $secondTarget.Characters[0].CharacterId = $firstTarget.Characters[0].CharacterId
    $validateSelected = $formType.GetMethod('ValidateSelectedAccounts', $staticFlags)
    $validateSelected.Invoke($null, [object[]]@($targetConfig)) | Out-Null
    $duplicatePreset = $presetType.GetMethod('Create', $staticFlags).Invoke($null, [object[]]@($targetConfig, 'Across servers'))
    foreach ($account in $targetConfig.Accounts) { foreach ($character in $account.Characters) { $character.Selected = $false } }
    $presetResult = $presetType.GetMethod('Apply', $staticFlags).Invoke($null, [object[]]@($targetConfig, $duplicatePreset))
    if ($presetResult.SelectedCount -ne 2 -or !$firstTarget.Characters[0].Selected -or !$secondTarget.Characters[0].Selected) { throw 'Preset lost a duplicate CharacterId belonging to another account/server.' }
    $sampleCredentials = New-Object 'System.Collections.Generic.Dictionary[string,string]'
    $sampleCredentials.Add($firstTarget.Id, 'fake-password-A')
    $sampleCredentials.Add($secondTarget.Id, 'fake-password-B')
    $targetRequest = $formType.GetMethod('CreateLaunchRequest', $staticFlags).Invoke($null, [object[]]@($targetConfig, $sampleCredentials.psobject.BaseObject))
    if ($targetRequest.Characters.Count -ne 2 -or $targetRequest.Characters[0].AccountId -eq $targetRequest.Characters[1].AccountId -or $targetRequest.Characters[0].GamePath -ne $fakeGamePath -or $targetRequest.Characters[1].GamePath -ne $otherFakeGamePath -or $targetRequest.Characters[0].Server -ne '192.0.2.31' -or $targetRequest.Characters[1].Port -ne 7032 -or $targetRequest.Characters[1].Password -ne 'fake-password-B') { throw 'Launch request did not carry each account ID, credentials and independent game target.' }
    $firstName = $firstTarget.Characters[0].Name
    $modernEvent = 'AOQL_CHARACTER ' + $serializer.Serialize(@{ AccountId = $secondTarget.Id; CharacterId = $secondTarget.Characters[0].CharacterId; CharacterName = 'SecondServerName' })
    if (!$eventMethod.Invoke($null, [object[]]@($modernEvent, $targetRequest, $targetConfig, $null)) -or $secondTarget.Characters[0].Name -ne 'SecondServerName' -or $firstTarget.Characters[0].Name -ne $firstName) { throw 'Modern AccountId event updated the wrong duplicate character.' }
    $unscopedEvent = 'AOQL_CHARACTER ' + $serializer.Serialize(@{ CharacterId = $secondTarget.Characters[0].CharacterId; CharacterName = 'MustNotChange' })
    if ($eventMethod.Invoke($null, [object[]]@($unscopedEvent, $targetRequest, $targetConfig, $null))) { throw 'Modern request accepted an event lacking AccountId.' }
    $legacyRequest = $serializer.Deserialize($serializer.Serialize($targetRequest), $assembly.GetType('AOLauncher.LaunchRequest'))
    foreach ($entry in $legacyRequest.Characters) { $entry.AccountId = $null }
    if ($eventMethod.Invoke($null, [object[]]@($unscopedEvent, $legacyRequest, $targetConfig, $null))) { throw 'Ambiguous legacy name event was accepted.' }
    $secondTarget.Server = '192.0.2.33'
    if ($eventMethod.Invoke($null, [object[]]@($modernEvent, $targetRequest, $targetConfig, $null))) { throw 'Event from the previous account endpoint was accepted.' }
    $secondTarget.Server = $firstTarget.Server; $secondTarget.Port = $firstTarget.Port
    $collisionRejected = $false
    try { $validateSelected.Invoke($null, [object[]]@($targetConfig)) | Out-Null } catch { $collisionRejected = $_.Exception.GetBaseException() -is [ArgumentException] }
    if (!$collisionRejected) { throw 'Same-server duplicate CharacterId was accepted by login preflight.' }
    $importAttempt = [object[]]@($targetConfig, $secondTarget, [int[]]@(999001), $null)
    if ($mergeMethod.Invoke($null, $importAttempt) -or @($secondTarget.Characters | Where-Object CharacterId -eq 999001).Count -ne 0) { throw 'Changing to a conflicting server must reject imports atomically, including existing character IDs.' }
    $secondTarget.Server = '192.0.2.32'; $secondTarget.Port = 7032
    if (!$mergeMethod.Invoke($null, [object[]]@($targetConfig, $secondTarget, [int[]]@($firstTarget.Characters[1].CharacterId), $null))) { throw 'Same CharacterId on a different endpoint must be importable.' }
    $firstTarget.Server = '37.18.192.41'; $firstTarget.Port = 7505
    $secondTarget.Server = '37.18.192.41'; $secondTarget.Port = 7506
    $liveRejected = $false
    try { $validateSelected.Invoke($null, [object[]]@($targetConfig)) | Out-Null } catch { $liveRejected = $_.Exception.GetBaseException() -is [ArgumentException] }
    if (!$liveRejected) { throw 'Official Live guard must reject two selected characters for one username across dimensions.' }
    $secondTarget.Username = 'another-live-account'
    $validateSelected.Invoke($null, [object[]]@($targetConfig)) | Out-Null
    $firstTarget.Server = '5.161.243.214'; $firstTarget.Port = 7000
    $secondTarget.Server = '5.161.243.214'; $secondTarget.Port = 7000
    $prkDisplayName = 'Renamed shared test realm'
    ($targetConfig.LoginServers | Where-Object Id -eq 'prk').Name = $prkDisplayName
    $secondTarget.Characters[0].CharacterId = 999002
    $secondTarget.Username = ' ' + $firstTarget.Username.ToUpperInvariant() + ' '
    $prkRejected = $false
    try { $validateSelected.Invoke($null, [object[]]@($targetConfig)) | Out-Null } catch {
        $failure = $_.Exception.GetBaseException()
        $prkRejected = $failure -is [ArgumentException] -and $failure.Message.Contains($prkDisplayName) -and $failure.Message.Contains('5.161.243.214:7000') -and $failure.Message.Contains('allows at most 1 selected characters. You selected 2.')
    }
    if (!$prkRejected) { throw 'PRK must reject a duplicate username across saved accounts/folders, ignoring case and whitespace.' }
    $secondTarget.Username = 'another-prk-account'
    $validateSelected.Invoke($null, [object[]]@($targetConfig)) | Out-Null
    $firstTarget.Characters[1].Selected = $true
    $prkRejected = $false
    try { $validateSelected.Invoke($null, [object[]]@($targetConfig)) | Out-Null } catch {
        $failure = $_.Exception.GetBaseException()
        $prkRejected = $failure -is [ArgumentException] -and $failure.Message.Contains($prkDisplayName) -and $failure.Message.Contains('5.161.243.214:7000') -and $failure.Message.Contains('allows at most 1 selected characters. You selected 2.')
    }
    if (!$prkRejected) { throw 'PRK must reject two selected characters within one saved account.' }
    $firstTarget.Characters[1].Selected = $false
    $secondTarget.Username = $firstTarget.Username
    $firstTarget.Server = '37.18.192.41'; $firstTarget.Port = 7505
    $validateSelected.Invoke($null, [object[]]@($targetConfig)) | Out-Null
    $firstTarget.Server = '2.24.96.30'; $firstTarget.Port = 7500
    $secondTarget.Server = '2.24.96.30'; $secondTarget.Port = 7500
    $firstTarget.Characters[1].Selected = $true
    $validateSelected.Invoke($null, [object[]]@($targetConfig)) | Out-Null
    'PASS: PRK allows distinct accounts, rejects multiple selected characters per username, keeps Live/PRK identities separate and preserves Rebirth multi-character selection.'
    $firstTarget.GamePath = Join-Path $tempPrefsRoot 'MissingGame'
    $missingRejected = $false
    try { $validateSelected.Invoke($null, [object[]]@($targetConfig)) | Out-Null } catch { $missingRejected = $_.Exception.GetBaseException() -is [ArgumentException] }
    if (!$missingRejected -or $targetsType.GetMethod('GameExists', $staticFlags).Invoke($null, [object[]]@($firstTarget.GamePath))) { throw 'Missing game folders must fail preflight.' }
    $firstTarget.GamePath = $fakeGamePath
    'PASS: per-account requests/presets support same usernames and IDs across servers; events are AccountId/endpoint-bound; collisions, missing games and official Live multi-character selections are rejected.'

    $dialogDefaults = [Activator]::CreateInstance($settingsType)
    $dialogDefaults.GamePath = $fakeGamePath
    $dialogDefaults.Server = '192.0.2.40'; $dialogDefaults.Port = 7040
    $profileDialog = [Activator]::CreateInstance($accountDialogType, [object[]]@($null, $dialogDefaults))
    try {
        if ($profileDialog.GamePath -ne '' -or $profileDialog.Server -ne '' -or $profileDialog.ServerProfileId -or $accountDialogType.GetField('portInput', $flags).GetValue($profileDialog).Text -ne '') { throw 'A new account must start without an implicit game folder, server, port or preset.' }
        $accountDialogType.GetField('pathInput', $flags).GetValue($profileDialog).Text = $fakeGamePath
        $accountDialogType.GetField('usernameInput', $flags).GetValue($profileDialog).Text = 'SyntheticAccount'
        $saveAction = $editorType.GetField('SaveButton', $flags).GetValue($profileDialog)
        $saveAction.GetType().GetMethod('OnClick', $flags).Invoke($saveAction, [object[]]@([EventArgs]::Empty)) | Out-Null
        if ($profileDialog.DialogResult -eq [Windows.Forms.DialogResult]::OK -or !$editorType.GetField('ErrorLabel', $flags).GetValue($profileDialog).Text.StartsWith('Choose a server preset') -or $accountDialogType.GetField('accountTabs', $flags).GetValue($profileDialog).SelectedIndex -ne 1) { throw 'An unresolved new account must reject Save and show the server choice before accepting.' }
        $profilePicker = $accountDialogType.GetField('serverProfile', $flags).GetValue($profileDialog)
        if ($profilePicker.Items[0] -ne 'Choose server preset' -or $profilePicker.Items[1] -ne 'Custom server') { throw 'New-account preset choices must distinguish unresolved from explicitly custom.' }
        Select-ServerProfile $profileDialog 5
        if ($profileDialog.ServerProfileId -ne 'prk' -or $profileDialog.Server -ne '5.161.243.214' -or $accountDialogType.GetField('portInput', $flags).GetValue($profileDialog).Text -ne '7000' -or $profileDialog.GamePath -ne $fakeGamePath) { throw 'Server preset did not copy its endpoint or changed the account game folder.' }
        $accountDialogType.GetField('serverInput', $flags).GetValue($profileDialog).Text = '192.0.2.41'
        if ($profileDialog.ServerProfileId) { throw 'Manually editing an endpoint must detach its catalog profile.' }
    } finally { $profileDialog.Dispose() }
    'PASS: new accounts have no implicit game/server target, unresolved Save is rejected, explicit presets change endpoint only, and manual endpoint editing becomes custom.'

    $rebirthSuggestion = @{ GamePath=$fakeGamePath; ServerProfileId='rebirth'; Server='2.24.96.30'; Port=7500; DimensionName='AORebirth' }
    $liveSuggestion = @{ GamePath=$otherFakeGamePath; ServerProfileId='live'; Server='37.18.192.41'; Port=7505; DimensionName='Rubi-Ka' }
    $detectedDialog = [Activator]::CreateInstance($accountDialogType, [object[]]@($null, $dialogDefaults))
    try {
        Set-DetectedTarget $detectedDialog $rebirthSuggestion
        if ($detectedDialog.GamePath -ne $fakeGamePath -or $detectedDialog.ServerProfileId -ne 'rebirth' -or $detectedDialog.Server -ne '2.24.96.30' -or !$accountDialogType.GetField('serverConfirmed', $flags).GetValue($detectedDialog)) { throw 'Untouched new-account fields did not adopt an exact detected preset and game folder.' }
        Set-DetectedTarget $detectedDialog $liveSuggestion
        if ($detectedDialog.GamePath -ne $otherFakeGamePath -or $detectedDialog.ServerProfileId -ne 'live') { throw 'A repeated detection did not replace the previous automatic suggestion.' }
        $accountDialogType.GetField('prefsInput', $flags).GetValue($detectedDialog).Text = 'C:\SyntheticPrefs\Changed\Prefs\Account'
        if ($detectedDialog.GamePath -ne '' -or $detectedDialog.Server -ne '' -or $accountDialogType.GetField('serverConfirmed', $flags).GetValue($detectedDialog)) { throw 'Changing Prefs input must immediately clear the previous automatic target and confirmation.' }
        Set-DetectedTarget $detectedDialog $liveSuggestion
        $accountDialogType.GetField('prefsInput', $flags).GetValue($detectedDialog).Text = ''
        if ($detectedDialog.GamePath -ne '' -or $detectedDialog.Server -ne '' -or $accountDialogType.GetField('serverConfirmed', $flags).GetValue($detectedDialog)) { throw 'Clearing Prefs input must immediately clear the previous automatic target and confirmation.' }
        Set-DetectedTarget $detectedDialog $liveSuggestion
        Set-DetectedTarget $detectedDialog @{ DimensionName='Unmatched synthetic dimension' }
        if ($detectedDialog.GamePath -ne '' -or $detectedDialog.Server -ne '' -or $detectedDialog.ServerProfileId -or $accountDialogType.GetField('portInput', $flags).GetValue($detectedDialog).Text -ne '' -or $accountDialogType.GetField('serverConfirmed', $flags).GetValue($detectedDialog) -or $accountDialogType.GetField('useDetectedTarget', $flags).GetValue($detectedDialog).Enabled) { throw 'An unresolved replacement must clear previous automatic targets and disable adoption.' }
        Set-DetectedTarget $detectedDialog @{ ServerProfileId='rebirth'; Server='192.0.2.200'; Port=7500; DimensionName='Mismatched endpoint' }
        if ($detectedDialog.Server -ne '' -or $detectedDialog.ServerProfileId -or $accountDialogType.GetField('serverConfirmed', $flags).GetValue($detectedDialog)) { throw 'Profile ID alone must not authorize a mismatched suggested endpoint.' }
        $accountDialogType.GetField('pathInput', $flags).GetValue($detectedDialog).Text = $otherFakeGamePath
        Set-DetectedTarget $detectedDialog $rebirthSuggestion
        if ($detectedDialog.GamePath -ne $otherFakeGamePath -or $detectedDialog.ServerProfileId -ne 'rebirth') { throw 'A manual game folder must survive automatic server detection independently.' }
        $accountDialogType.GetField('serverInput', $flags).GetValue($detectedDialog).Text = '192.0.2.42'
        $accountDialogType.GetField('portInput', $flags).GetValue($detectedDialog).Text = '7042'
        Set-DetectedTarget $detectedDialog $liveSuggestion
        if ($detectedDialog.GamePath -ne $otherFakeGamePath -or $detectedDialog.Server -ne '192.0.2.42' -or $accountDialogType.GetField('portInput', $flags).GetValue($detectedDialog).Text -ne '7042' -or $detectedDialog.ServerProfileId) { throw 'Detection overwrote a manual custom endpoint.' }
        $accountDialogType.GetField('prefsInput', $flags).GetValue($detectedDialog).Text = 'C:\SyntheticPrefs\Other\Prefs\Account'
        $accountDialogType.GetField('prefsInput', $flags).GetValue($detectedDialog).Text = ''
        if ($detectedDialog.GamePath -ne $otherFakeGamePath -or $detectedDialog.Server -ne '192.0.2.42' -or !$accountDialogType.GetField('serverConfirmed', $flags).GetValue($detectedDialog)) { throw 'Changing or clearing Prefs must preserve a manually selected game/server target.' }
        $accountDialogType.GetField('pathInput', $flags).GetValue($detectedDialog).Text = ''
        Set-DetectedTarget $detectedDialog $rebirthSuggestion
        if ($detectedDialog.GamePath -ne '') { throw 'A manually cleared game folder must remain blank during detection.' }
        $useDetected = $accountDialogType.GetField('useDetectedTarget', $flags).GetValue($detectedDialog)
        $useDetected.GetType().GetMethod('OnClick', $flags).Invoke($useDetected, [object[]]@([EventArgs]::Empty)) | Out-Null
        if ($detectedDialog.GamePath -ne $fakeGamePath -or $detectedDialog.ServerProfileId -ne 'rebirth') { throw 'Explicit Use detected settings did not replace the manual target with the current valid suggestion.' }
        Set-DetectedTarget $detectedDialog $liveSuggestion
        if ($detectedDialog.GamePath -ne $fakeGamePath -or $detectedDialog.ServerProfileId -ne 'rebirth') { throw 'An explicit adoption must be protected from subsequent automatic changes.' }
        Set-DetectedTarget $detectedDialog @{ ServerProfileId='live'; Server='37.18.192.41'; Port=7505; DimensionName='Rubi-Ka' } $true
        if ($detectedDialog.GamePath -ne $fakeGamePath -or $detectedDialog.ServerProfileId -ne 'live') { throw 'Explicit adoption without a game path must preserve the manual game folder.' }
        Set-DetectedTarget $detectedDialog @{ GamePath=$otherFakeGamePath } $true
        if ($detectedDialog.GamePath -ne $otherFakeGamePath -or $detectedDialog.ServerProfileId -ne 'live') { throw 'Explicit adoption without a server must preserve the manual server.' }
    } finally { $detectedDialog.Dispose() }
    $manualServerDialog = [Activator]::CreateInstance($accountDialogType, [object[]]@($null, $dialogDefaults))
    try {
        Select-ServerProfile $manualServerDialog 5
        Set-DetectedTarget $manualServerDialog $rebirthSuggestion
        if ($manualServerDialog.GamePath -ne $fakeGamePath -or $manualServerDialog.ServerProfileId -ne 'prk') { throw 'A manually chosen preset must survive while an untouched game folder is detected.' }
    } finally { $manualServerDialog.Dispose() }
    $existingSnapshot = $serializer.Serialize($firstTarget)
    $existingDialog = [Activator]::CreateInstance($accountDialogType, [object[]]@($firstTarget, $dialogDefaults))
    try {
        Set-DetectedTarget $existingDialog $rebirthSuggestion
        if ($existingDialog.GamePath -ne $firstTarget.GamePath -or $existingDialog.Server -ne $firstTarget.Server -or $accountDialogType.GetField('portInput', $flags).GetValue($existingDialog).Text -ne $firstTarget.Port.ToString()) { throw 'Automatic detection must preserve an existing account target.' }
        Set-DetectedTarget $existingDialog $rebirthSuggestion $true
        if ($existingDialog.ServerProfileId -ne 'rebirth' -or $existingDialog.Server -ne '2.24.96.30' -or $serializer.Serialize($firstTarget) -ne $existingSnapshot) { throw 'Explicit detected settings must update the edit dialog without mutating its source account.' }
    } finally { $existingDialog.Dispose() }
    'PASS: detected targets fill untouched fields, replace/clear prior automatic values, require exact profile endpoints, preserve independent manual/existing targets, and support explicit partial adoption without modifying source accounts.'

    $accountDialog = [Activator]::CreateInstance($accountDialogType, [object[]]@($null))
    Select-ServerProfile $accountDialog 2
    $accountDialogType.GetField('pathInput', $flags).GetValue($accountDialog).Text = $fakeGamePath
    $accountDialogType.GetField('prefsInput', $flags).GetValue($accountDialog).Text = $tempAccountPath
    $scanTask = $accountDialogType.GetMethod('ScanPrefsAsync', $flags).Invoke($accountDialog, [object[]]@($false))
    Wait-DialogWork { $scanTask.IsCompleted }
    if (!$scanTask.Result -or $accountDialog.ImportedCharacterIds.Count -ne 1 -or $accountDialog.Username -ne 'ExampleAccount' -or $accountDialog.AccountLabel -ne 'ExampleAccount') {
        throw 'Prefs preview did not discover IDs and fill blank account fields.'
    }
    [IO.Directory]::CreateDirectory((Join-Path $tempAccountPath 'Char910102')) | Out-Null
    $accountDialogType.GetField('usernameInput', $flags).GetValue($accountDialog).Text = 'MyLoginOverride'
    $accountDialogType.GetField('labelInput', $flags).GetValue($accountDialog).Text = 'My custom label'
    $saveButton = $editorType.GetField('SaveButton', $flags).GetValue($accountDialog)
    $saveButton.GetType().GetMethod('OnClick', $flags).Invoke($saveButton, [object[]]@([EventArgs]::Empty)) | Out-Null
    Wait-DialogWork { $accountDialog.DialogResult -eq [Windows.Forms.DialogResult]::OK }
    if ($accountDialog.ImportedCharacterIds.Count -ne 2 -or $accountDialog.Username -ne 'MyLoginOverride' -or $accountDialog.AccountLabel -ne 'My custom label') {
        throw 'Save must rescan the folder and preserve explicitly entered account fields.'
    }
    if ($accountDialog.PrefsPath -ne [IO.Path]::GetFullPath($tempAccountPath)) { throw 'PrefsPath must store the canonical account folder.' }
    $directSaveDialog = [Activator]::CreateInstance($accountDialogType, [object[]]@($null))
    Select-ServerProfile $directSaveDialog 2
    $accountDialogType.GetField('pathInput', $flags).GetValue($directSaveDialog).Text = $fakeGamePath
    $accountDialogType.GetField('prefsInput', $flags).GetValue($directSaveDialog).Text = $tempAccountPath
    $directSaveButton = $editorType.GetField('SaveButton', $flags).GetValue($directSaveDialog)
    $directSaveButton.GetType().GetMethod('OnClick', $flags).Invoke($directSaveButton, [object[]]@([EventArgs]::Empty)) | Out-Null
    Wait-DialogWork { $directSaveDialog.DialogResult -eq [Windows.Forms.DialogResult]::OK }
    if ($directSaveDialog.Username -ne 'ExampleAccount' -or $directSaveDialog.ImportedCharacterIds.Count -ne 2) {
        throw 'Pasting a folder and saving must work with initially blank username and label.'
    }
    'PASS: async Prefs preview finds IDs; Save rescans current data, preserves manual server/path and account overrides, and fills initially blank usernames from the pasted Prefs folder.'

    [IO.Directory]::CreateDirectory((Join-Path $detectedAccountPath 'Char910201')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $fakeGamePath 'dimensions.txt'), "STARTINFO`r`ndisplayname = AORebirth`r`nconnect = 2.24.96.30`r`nports = 7500`r`nENDINFO`r`nSTARTINFO`r`ndisplayname = Rubi-Ka`r`nconnect = cm.d1.funcom.com`r`nports = 7505`r`nENDINFO`r`n")
    [IO.File]::WriteAllText((Join-Path $detectedPrefsPath 'Prefs.xml'), '<Root><String name="SelectedDimension" value="&quot;AORebirth&quot;" /></Root>')
    $autoScanDialog = [Activator]::CreateInstance($accountDialogType, [object[]]@($null, $dialogDefaults))
    $accountDialogType.GetField('prefsInput', $flags).GetValue($autoScanDialog).Text = $detectedAccountPath
    $scanTask = $accountDialogType.GetMethod('ScanPrefsAsync', $flags).Invoke($autoScanDialog, [object[]]@($false))
    Wait-DialogWork { $scanTask.IsCompleted }
    if (!$scanTask.Result -or $autoScanDialog.GamePath -ne $fakeGamePath -or $autoScanDialog.ServerProfileId -ne 'rebirth' -or $autoScanDialog.Username -ne 'DetectedAccount') { throw 'The real async import/resolver path did not populate untouched game/server/account fields from synthetic Prefs.' }
    [IO.File]::WriteAllText((Join-Path $detectedPrefsPath 'Prefs.xml'), '<Root><String name="SelectedDimension" value="&quot;Rubi-Ka&quot;" /></Root>')
    $autoSave = $editorType.GetField('SaveButton', $flags).GetValue($autoScanDialog)
    Complete-AccountSave $autoScanDialog $autoSave
    $reviewError = $editorType.GetField('ErrorLabel', $flags).GetValue($autoScanDialog).Text
    $reviewTab = $accountDialogType.GetField('accountTabs', $flags).GetValue($autoScanDialog).SelectedIndex
    if ($autoScanDialog.DialogResult -eq [Windows.Forms.DialogResult]::OK -or $autoScanDialog.ServerProfileId -ne 'live' -or !$reviewError.StartsWith('Detected settings changed.') -or $reviewTab -ne 1) {
        throw ('A valid server change discovered during Save must stay open and require review of the new target. Actual: result={0}; profile={1}; tab={2}; scanning={3}; error={4}' -f $autoScanDialog.DialogResult, $autoScanDialog.ServerProfileId, $reviewTab, $accountDialogType.GetField('scanning', $flags).GetValue($autoScanDialog), $reviewError)
    }
    Complete-AccountSave $autoScanDialog $autoSave
    if ($autoScanDialog.DialogResult -ne [Windows.Forms.DialogResult]::OK -or $autoScanDialog.ServerProfileId -ne 'live' -or $autoScanDialog.Port -ne 7505) { throw 'Saving again with an unchanged reviewed target must accept the account.' }
    $autoScanDialog.DialogResult = [Windows.Forms.DialogResult]::None
    [IO.File]::WriteAllText((Join-Path $detectedPrefsPath 'Prefs.xml'), '<Root><String name="SelectedDimension" value="&quot;Unknown&quot;" /></Root>')
    Complete-AccountSave $autoScanDialog $autoSave
    if ($autoScanDialog.DialogResult -eq [Windows.Forms.DialogResult]::OK -or !$editorType.GetField('ErrorLabel', $flags).GetValue($autoScanDialog).Text.StartsWith('Detected settings changed.')) { throw 'Cleared detection during Save must first surface the target change.' }
    Complete-AccountSave $autoScanDialog $autoSave
    if ($autoScanDialog.DialogResult -eq [Windows.Forms.DialogResult]::OK -or $autoScanDialog.Server -ne '' -or $autoScanDialog.ServerProfileId -or $autoScanDialog.GamePath -ne $fakeGamePath -or !$editorType.GetField('ErrorLabel', $flags).GetValue($autoScanDialog).Text.StartsWith('Choose a server preset')) { throw 'Save must rescan and reject an unresolved replacement rather than retain or accept the previous automatic server.' }
    Select-ServerProfile $autoScanDialog 5
    $accountDialogType.GetField('pathInput', $flags).GetValue($autoScanDialog).Text = $otherFakeGamePath
    [IO.File]::WriteAllText((Join-Path $detectedPrefsPath 'Prefs.xml'), '<Root><String name="SelectedDimension" value="&quot;AORebirth&quot;" /></Root>')
    Complete-AccountSave $autoScanDialog $autoSave
    if ($autoScanDialog.DialogResult -ne [Windows.Forms.DialogResult]::OK -or $autoScanDialog.GamePath -ne $otherFakeGamePath -or $autoScanDialog.ServerProfileId -ne 'prk') { throw 'Changed Prefs metadata must not overwrite or unnecessarily block unchanged manual settings during Save.' }
    $initialSaveDialog = [Activator]::CreateInstance($accountDialogType, [object[]]@($null, $dialogDefaults))
    $accountDialogType.GetField('prefsInput', $flags).GetValue($initialSaveDialog).Text = $detectedAccountPath
    $initialSave = $editorType.GetField('SaveButton', $flags).GetValue($initialSaveDialog)
    Complete-AccountSave $initialSaveDialog $initialSave
    if ($initialSaveDialog.DialogResult -eq [Windows.Forms.DialogResult]::OK -or $initialSaveDialog.GamePath -ne $fakeGamePath -or $initialSaveDialog.ServerProfileId -ne 'rebirth' -or $initialSaveDialog.Username -ne 'DetectedAccount' -or !$editorType.GetField('ErrorLabel', $flags).GetValue($initialSaveDialog).Text.StartsWith('Detected settings changed.') -or $accountDialogType.GetField('accountTabs', $flags).GetValue($initialSaveDialog).SelectedIndex -ne 1) { throw 'Pasting only Prefs and saving must show automatically filled targets for review before accepting.' }
    Complete-AccountSave $initialSaveDialog $initialSave
    if ($initialSaveDialog.DialogResult -ne [Windows.Forms.DialogResult]::OK -or $initialSaveDialog.ImportedCharacterIds.Count -ne 1) { throw 'A second unchanged Save must accept the initially pasted Prefs account after target review.' }
    'PASS: real async Prefs scans detect targets; Save-time changes and first-save autofill require review, unchanged second saves succeed, unresolved targets reject, and manual choices remain accepted.'
} finally {
    if ($accountDialog) { $accountDialog.Dispose() }
    if ($directSaveDialog) { $directSaveDialog.Dispose() }
    if ($autoScanDialog) { $autoScanDialog.Dispose() }
    if ($initialSaveDialog) { $initialSaveDialog.Dispose() }
    if ([IO.Directory]::Exists($detectedAccountPath)) {
        [IO.Directory]::Delete((Join-Path $detectedAccountPath 'Char910201'), $false)
        [IO.Directory]::Delete($detectedAccountPath, $false)
    }
    if ([IO.Directory]::Exists($detectedPrefsPath)) {
        [IO.File]::Delete((Join-Path $detectedPrefsPath 'Prefs.xml'))
        [IO.Directory]::Delete($detectedPrefsPath, $false)
    }
    [IO.File]::Delete((Join-Path $fakeGamePath 'dimensions.txt'))
    foreach ($gameDirectory in @($fakeGamePath, $otherFakeGamePath)) {
        if ([IO.Directory]::Exists($gameDirectory)) {
            [IO.File]::Delete((Join-Path $gameDirectory 'AnarchyOnline.exe'))
            [IO.Directory]::Delete($gameDirectory, $false)
        }
    }
    if ([IO.Directory]::Exists($tempAccountPath)) {
        foreach ($directory in [IO.Directory]::GetDirectories($tempAccountPath)) { [IO.Directory]::Delete($directory, $false) }
        [IO.Directory]::Delete($tempAccountPath, $false)
    }
    if ([IO.Directory]::Exists($tempPrefsRoot)) { [IO.Directory]::Delete($tempPrefsRoot, $false) }
}

$credentialType = $assembly.GetType('AOLauncher.CredentialStore')
$credentialFlags = [Reflection.BindingFlags]'NonPublic,Static'
$saveCredential = $credentialType.GetMethod('SaveToDirectory', $credentialFlags)
$readCredential = $credentialType.GetMethod('ReadFromDirectory', $credentialFlags)
[string]$tempCredentialDirectory = Join-Path ([IO.Path]::GetTempPath()) ('AOLauncher-Verify-' + [Guid]::NewGuid().ToString('N'))
$testAccount = [Activator]::CreateInstance($assembly.GetType('AOLauncher.AccountConfig'))
$testAccount.Username = 'isolated-verification-account'
$testAccount.Server = '192.0.2.90'
$testAccount.Port = 7090
[string]$samplePassword = 'Example-only-verification-password-' + [Guid]::NewGuid().ToString('N')
try {
    if ($null -ne $readCredential.Invoke($null, [object[]]@($testAccount, $tempCredentialDirectory))) { throw 'Missing saved password should return null.' }
    $saveCredential.Invoke($null, [object[]]@($testAccount, $samplePassword, $tempCredentialDirectory)) | Out-Null
    if ($readCredential.Invoke($null, [object[]]@($testAccount, $tempCredentialDirectory)) -ne $samplePassword) { throw 'Encrypted password roundtrip failed.' }
    $encryptedFiles = @(Get-ChildItem -LiteralPath $tempCredentialDirectory -File)
    if ($encryptedFiles.Count -ne 1) { throw 'Expected one encrypted credential file.' }
    $encryptedBytes = [IO.File]::ReadAllBytes($encryptedFiles[0].FullName)
    if ([Text.Encoding]::UTF8.GetString($encryptedBytes).Contains($samplePassword)) { throw 'Password found in plaintext in the encrypted file.' }
    $testAccount.Server = '192.0.2.91'
    if ($null -ne $readCredential.Invoke($null, [object[]]@($testAccount, $tempCredentialDirectory))) { throw 'A saved password must not be returned for another server.' }
    $testAccount.Server = '192.0.2.90'; $testAccount.Port = 7091
    if ($null -ne $readCredential.Invoke($null, [object[]]@($testAccount, $tempCredentialDirectory))) { throw 'A saved password must not be returned for another port.' }
    $testAccount.Port = 7090; $testAccount.GamePath = 'C:\another-game-copy'
    if ($readCredential.Invoke($null, [object[]]@($testAccount, $tempCredentialDirectory)) -ne $samplePassword) { throw 'Moving a game folder on the same endpoint must not lose its saved password.' }
    $originalAccountId = $testAccount.Id
    $testAccount.Id = [Guid]::NewGuid().ToString('D')
    $otherCredentialPath = $credentialType.GetMethod('AccountPath', $credentialFlags).Invoke($null, [object[]]@($testAccount, $tempCredentialDirectory))
    [IO.File]::WriteAllBytes($otherCredentialPath, $encryptedBytes)
    if ($null -ne $readCredential.Invoke($null, [object[]]@($testAccount, $tempCredentialDirectory))) { throw 'Copied encrypted record must not work under another account ID.' }
    $testAccount.Id = $originalAccountId
    $testAccount.Username = 'different-account'
    if ($null -ne $readCredential.Invoke($null, [object[]]@($testAccount, $tempCredentialDirectory))) { throw 'A saved password must not be returned for another username.' }
    $testAccount.Username = 'isolated-verification-account'
    [byte[]]$legacyPlaintext = [Text.Encoding]::UTF8.GetBytes($serializer.Serialize(@{ Version = 1; Username = $testAccount.Username; Password = $samplePassword }))
    [byte[]]$credentialEntropy = [Text.Encoding]::UTF8.GetBytes('AORebirthLauncher.Credentials.v1')
    [byte[]]$legacyCiphertext = [Security.Cryptography.ProtectedData]::Protect($legacyPlaintext, $credentialEntropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    [Array]::Clear($legacyPlaintext, 0, $legacyPlaintext.Length)
    [IO.File]::WriteAllBytes($encryptedFiles[0].FullName, $legacyCiphertext)
    if ($null -ne $readCredential.Invoke($null, [object[]]@($testAccount, $tempCredentialDirectory))) { throw 'Legacy credential must not migrate without its original endpoint marker.' }
    $testAccount.LegacyCredentialTarget = '192.0.2.90:7090'
    $testAccount.Server = '192.0.2.91'
    if ($null -ne $readCredential.Invoke($null, [object[]]@($testAccount, $tempCredentialDirectory))) { throw 'Legacy credential must not migrate to a different endpoint.' }
    $testAccount.Server = '192.0.2.90'
    if ($readCredential.Invoke($null, [object[]]@($testAccount, $tempCredentialDirectory)) -ne $samplePassword) { throw 'Original legacy target did not migrate its encrypted credential.' }
    [byte[]]$upgradedPlaintext = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($encryptedFiles[0].FullName), $credentialEntropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $upgradedRecord = $serializer.DeserializeObject([Text.Encoding]::UTF8.GetString($upgradedPlaintext))
    [Array]::Clear($upgradedPlaintext, 0, $upgradedPlaintext.Length)
    if ($upgradedRecord.Version -ne 2 -or $upgradedRecord.Target -ne '192.0.2.90:7090' -or $upgradedRecord.AccountId -ne $testAccount.Id) { throw 'Legacy credential was not rewritten with v2 target and account binding.' }
    $upgradedRecord = $null
    [IO.File]::WriteAllBytes($encryptedFiles[0].FullName, [byte[]]@(1,2,3,4))
    if ($null -ne $readCredential.Invoke($null, [object[]]@($testAccount, $tempCredentialDirectory))) { throw 'Corrupted encrypted password should be ignored.' }
    'PASS: Windows DPAPI encrypted roundtrip, no plaintext password, username/account/endpoint binding, same-server folder moves, scoped legacy-v1 upgrade and corrupted-file handling in an isolated temporary directory.'
} finally {
    if (Test-Path -LiteralPath $tempCredentialDirectory) {
        Get-ChildItem -LiteralPath $tempCredentialDirectory -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
        Remove-Item -LiteralPath $tempCredentialDirectory
    }
    $samplePassword = $null
}
$gameFilterType = $assembly.GetType('AOLauncher.GameLogFilter')
$filterMethod = $gameFilterType.GetMethod('Filter', $flags)
$gameFilter = [Activator]::CreateInstance($gameFilterType, $true)
$routineLine = '[2026-10-24 09:25:45.402] [RDB] [info] get_bytes_for_entry located RDB entry 1010004:9028 with size 3489'
$notice = $filterMethod.Invoke($gameFilter, [object[]]@($routineLine))
if (!$notice -or $notice -eq $routineLine) { throw 'Routine diagnostics must produce one explanatory notice.' }
foreach ($category in @('RDB', 'DatabaseController')) {
    foreach ($level in @('info', 'debug', 'trace')) {
        if ($null -ne $filterMethod.Invoke($gameFilter, [object[]]@("[2026-10-24 09:25:45.402] [$category] [$level] routine"))) { throw 'Routine database flood was not suppressed after the notice.' }
    }
}
foreach ($preservedLine in @(
    '[2026-10-24 09:25:45.402] [RDB] [error] Failed to step statement SELECT version, data FROM rdb_1010003 WHERE id = 0, error: no more rows available',
    '[2026-10-24 09:25:45.402] [RDB] [warning] warning',
    '[2026-10-24 09:25:45.402] [DatabaseController] [critical] failure',
    '[2026-10-24 09:25:45.402] [Unknown] [info] preserve',
    '[invalid timestamp] [RDB] [info] preserve',
    'Backend error mentions [RDB] [info] but must remain visible',
    'AOQL_CHARACTER {"AccountId":"sample","CharacterId":35748,"CharacterName":"Sample"}',
    'This account''s queue stopped: ID 35749 was not detected in game.'
)) {
    if ($filterMethod.Invoke($gameFilter, [object[]]@($preservedLine)) -cne $preservedLine) { throw "Diagnostic filter changed a required status/error: $preservedLine" }
}
$anotherFilter = [Activator]::CreateInstance($gameFilterType, $true)
if (!$filterMethod.Invoke($anotherFilter, [object[]]@($routineLine))) { throw 'A new login run must get its own diagnostic notice.' }
'PASS: routine PRK database messages are suppressed once per run; warnings, errors, unknown formats and login protocol events remain unchanged.'
'All checks use isolated sample data and a temporary encrypted credential directory. No real settings or credentials touched; no windows shown or backend/game launched.'
"Assembly: $assemblyFile"
"SHA256: $((Get-FileHash -LiteralPath $assemblyFile -Algorithm SHA256).Hash)"
