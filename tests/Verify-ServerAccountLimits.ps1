param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'),
    [string]$PreviewDirectory
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') {
    throw 'Run with 32-bit Windows PowerShell -NoProfile -STA.'
}
Add-Type -AssemblyName System.Web.Extensions
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ServerLimitPreviewNative {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
}
'@
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$static = [Reflection.BindingFlags]'NonPublic,Static'
$instance = [Reflection.BindingFlags]'NonPublic,Instance'
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$settingsType = $assembly.GetType('AOLauncher.LauncherSettings')
$profileType = $assembly.GetType('AOLauncher.LoginServerProfile')
$targets = $assembly.GetType('AOLauncher.AccountTargets')
$deserialize = $assembly.GetType('AOLauncher.SettingsStore').GetMethod('Deserialize', $static)
$limitMethod = $targets.GetMethod('AccountLimit', $static)
$characterLimitMethod = $targets.GetMethod('CharacterLimit', $static)
$selectedMethod = $targets.GetMethod('ValidateSelectedAccountLimits', $static)
$languageMethod = $assembly.GetType('AOLauncher.Localization.Texts').GetMethod('SetLanguage', $static)
$assembly.GetType('AOLauncher.GuiStrings').GetMethod('Register', $static).Invoke($null, @()) | Out-Null
$script:checks = 0
$script:charId = 970000

function Assert([bool]$Condition, [string]$Description) {
    if (!$Condition) { throw $Description }
    $script:checks++
}
function New-Settings { [Activator]::CreateInstance($settingsType) }
function Clone-Settings($Value) { $deserialize.Invoke($null, [object[]]@($serializer.Serialize($Value))) }
function Limit($Value, [string]$Endpoint) { $limitMethod.Invoke($null, [object[]]@($Value, $Endpoint)) }
function CharacterLimit($Value, [string]$Endpoint) { $characterLimitMethod.Invoke($null, [object[]]@($Value, $Endpoint)) }
function Expect-Rejection([scriptblock]$Action, [string]$Description) {
    $failure = $null
    try { & $Action | Out-Null } catch { $failure = $_.Exception.GetBaseException() }
    Assert ($failure -is [ArgumentException]) $Description
    return $failure.Message
}
function Add-Account($Value, [string]$Username, [string]$Server = '5.161.243.214', [int]$Port = 7000) {
    $account = [Activator]::CreateInstance($assembly.GetType('AOLauncher.AccountConfig'))
    $account.Username = $Username; $account.Label = $Username.Trim()
    $account.Server = $Server; $account.Port = $Port; $account.GamePath = 'C:\SyntheticGames\AccountLimitTest'
    $character = [Activator]::CreateInstance($assembly.GetType('AOLauncher.CharacterConfig'))
    $script:charId++; $character.CharacterId = $script:charId; $character.Name = 'Synthetic character ' + $script:charId
    $account.Characters.Add($character); $Value.Accounts.Add($account)
    return $account
}
function Add-Character($Account) {
    $character = [Activator]::CreateInstance($assembly.GetType('AOLauncher.CharacterConfig'))
    $script:charId++; $character.CharacterId = $script:charId; $character.Name = 'Synthetic character ' + $script:charId
    $Account.Characters.Add($character)
    return $character
}
function Click-Editor($Dialog, [string]$Name = 'SaveButton') {
    $button = $Dialog.GetType().BaseType.GetField($Name, $instance).GetValue($Dialog)
    $button.GetType().GetMethod('OnClick', $instance).Invoke($button, [object[]]@([EventArgs]::Empty)) | Out-Null
}
function New-ServerDialog($Profiles) {
    $arguments = New-Object object[] 1; $arguments[0] = $Profiles
    return [Activator]::CreateInstance($assembly.GetType('AOLauncher.LoginServersDialog'), $arguments)
}
function Layout($Control) {
    $Control.PerformLayout()
    foreach ($child in $Control.Controls) { Layout $child }
    [Windows.Forms.Application]::DoEvents()
}
function Check-Layout($Control) {
    foreach ($child in $Control.Controls) {
        if ($child -is [Windows.Forms.Label] -and $child.Text.Length -gt 0) {
            $size = [Windows.Forms.TextRenderer]::MeasureText($child.Text, $child.Font, [Drawing.Size]::new($child.Width, 1000), [Windows.Forms.TextFormatFlags]::WordBreak)
            Assert ($size.Height -le $child.Height) ('Clipped label: ' + $child.Text)
        }
        if ($child -is [Windows.Forms.Button]) {
            Assert ($child.Parent.ClientRectangle.Contains($child.Bounds)) ('Button outside parent: ' + $child.Text + '; bounds=' + $child.Bounds + '; parent=' + $child.Parent.ClientRectangle)
            Assert ($child.Height -ge $child.PreferredSize.Height) ('Button too short: ' + $child.Text)
        }
        Check-Layout $child
    }
}
function Save-Preview($Control, [string]$Name) {
    if (!$PreviewDirectory) { return }
    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PreviewDirectory)) | Out-Null
    $Control.ShowInTaskbar = $false
    $Control.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $Control.Location = [Drawing.Point]::new(-32000, -32000)
    $Control.Show()
    [Windows.Forms.Application]::DoEvents()
    Layout $Control
    $bitmap = [Drawing.Bitmap]::new($Control.Width, $Control.Height)
    try {
        # Print only this owned synthetic fixture; native printing preserves the
        # clipping of slim-scroll viewports, unlike nested DrawToBitmap calls.
        $graphics = [Drawing.Graphics]::FromImage($bitmap); $dc = $graphics.GetHdc()
        try { Assert ([ServerLimitPreviewNative]::PrintWindow($Control.Handle,$dc,0)) 'The synthetic limits fixture must render successfully.' }
        finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        $path = Join-Path ([IO.Path]::GetFullPath($PreviewDirectory)) $Name
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
        'PREVIEW: ' + $path
    } finally { $bitmap.Dispose(); $Control.Hide() }
}

$languageMethod.Invoke($null, [object[]]@('en')) | Out-Null
$legacy = '{"Version":1,"Accounts":[],"LoginServers":[{"Id":"live","Name":"Live","Server":"37.18.192.41","Port":7505},{"Id":"live-2019","Name":"Live 2019","Server":"37.18.192.41","Port":7506},{"Id":"prk","Name":"PRK","Server":"5.161.243.214","Port":7000},{"Id":"rebirth","Name":"Rebirth","Server":"2.24.96.30","Port":7500},{"Id":"custom","Name":"Custom","Server":"192.0.2.12","Port":7012}]}'
$migrated = $deserialize.Invoke($null, [object[]]@($legacy))
Assert ((@($migrated.LoginServers | ForEach-Object MaxAccounts) -join ',') -eq '0,0,2,0,0') 'Missing legacy account limits must migrate by endpoint.'
Assert ((@($migrated.LoginServers | ForEach-Object MaxCharactersPerAccount) -join ',') -eq '1,1,1,0,0') 'Missing legacy character limits must migrate by endpoint.'
$migrated.LoginServers[0].MaxAccounts = 3; $migrated.LoginServers[2].MaxAccounts = 0
$migrated.LoginServers[0].MaxCharactersPerAccount = 3; $migrated.LoginServers[2].MaxCharactersPerAccount = 0
$roundtrip = Clone-Settings $migrated
Assert ((@($roundtrip.LoginServers | ForEach-Object MaxAccounts) -join ',') -eq '3,0,0,0,0') 'Explicit account overrides and unlimited must survive roundtrip.'
Assert ((@($roundtrip.LoginServers | ForEach-Object MaxCharactersPerAccount) -join ',') -eq '3,1,0,0,0') 'Explicit character overrides and unlimited must survive roundtrip.'
$defaults = New-Settings
Assert ((Limit $defaults '37.18.192.41:7505') -eq 0) 'Live account default must be unlimited.'
Assert ((Limit $defaults '37.18.192.41:7506') -eq 0) 'Live 2019 account default must be unlimited.'
Assert ((CharacterLimit $defaults '37.18.192.41:7505') -eq 1) 'Live character default must be one per account.'
Assert ((CharacterLimit $defaults '5.161.243.214:7000') -eq 1) 'PRK character default must be one per account.'
Assert ((CharacterLimit $defaults '2.24.96.30:7500') -eq 0) 'Rebirth character default must be unlimited.'
Assert ((Limit $defaults '5.161.243.214:7000') -eq 2) 'PRK default must be two accounts.'
Assert ((Limit $defaults '2.24.96.30:7500') -eq 0) 'Rebirth default must be unlimited.'
$defaults.LoginServers.Clear()
Assert ((Limit $defaults '5.161.243.214:7000') -eq 2) 'An endpoint without a matching profile must retain its default.'
Assert ((Limit $defaults '192.0.2.20:7000') -eq 0) 'Unknown endpoint default must be unlimited.'
foreach ($limit in @(0, 3, 2)) {
    $profile = [Activator]::CreateInstance($profileType)
    $profile.Name = 'Duplicate endpoint ' + $limit; $profile.Server = '5.161.243.214'; $profile.Port = 7000; $profile.MaxAccounts = $limit
    $profile.MaxCharactersPerAccount = $limit
    $defaults.LoginServers.Add($profile)
}
Assert ((Limit $defaults ' 5.161.243.214:07000 ') -eq 2) 'Duplicate profiles must enforce the strictest positive value after endpoint normalization.'
Assert ((CharacterLimit $defaults '5.161.243.214:7000') -eq 2) 'Duplicate profiles must independently enforce the strictest positive character cap.'
foreach ($invalid in @(-1, 101)) {
    $defaults.LoginServers[0].MaxAccounts = $invalid
    Expect-Rejection { Clone-Settings $defaults } ('Invalid stored account limit accepted: ' + $invalid) | Out-Null
    $defaults.LoginServers[0].MaxAccounts = 0
    $defaults.LoginServers[0].MaxCharactersPerAccount = $invalid
    Expect-Rejection { Clone-Settings $defaults } ('Invalid stored character limit accepted: ' + $invalid) | Out-Null
    $defaults.LoginServers[0].MaxCharactersPerAccount = 0
}

$selected = New-Settings
$one = Add-Account $selected 'one'; $two = Add-Account $selected 'two'
$selectedMethod.Invoke($null, [object[]]@($selected)) | Out-Null
Assert $true 'Two PRK accounts accepted.'
$alias = Add-Account $selected ' ONE '
$alias.Characters[0].CharacterId = $one.Characters[0].CharacterId
$selectedMethod.Invoke($null, [object[]]@($selected)) | Out-Null
Assert $true 'Trimmed case-insensitive duplicate username must count once.'
$three = Add-Account $selected 'three'
$before = $serializer.Serialize($selected)
$message = Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($selected)) } 'Three distinct PRK accounts must fail.'
Assert ($message.Contains('Project Rubi-Ka') -and $message.Contains('5.161.243.214:7000') -and $message.Contains('2') -and $message.Contains('3')) 'Selected-limit error must identify the server, endpoint, count and limit.'
Assert ($serializer.Serialize($selected) -ceq $before) 'Validation must preserve saved selection.'
$three.Characters[0].Selected = $false
$selectedMethod.Invoke($null, [object[]]@($selected)) | Out-Null
Assert $true 'Unselected accounts must not count.'
$live = New-Settings
$null = Add-Account $live 'live-one' '37.18.192.41' 7505
$selectedMethod.Invoke($null, [object[]]@($live)) | Out-Null
Assert $true 'One Live account accepted.'
$null = Add-Account $live 'live-two' '37.18.192.41' 7505
$selectedMethod.Invoke($null, [object[]]@($live)) | Out-Null
Assert $true 'Multiple Live accounts must be accepted by default.'
$null = Add-Character $live.Accounts[0]
Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($live)) } 'Two chars on one Live account must fail.' | Out-Null
$live.LoginServers[1].MaxCharactersPerAccount = 3
$selectedMethod.Invoke($null, [object[]]@($live)) | Out-Null
Assert $true 'An explicit Live character override must be honored.'
$live.LoginServers[1].MaxAccounts = 1
Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($live)) } 'An explicit Live account cap must be independent of the char cap.' | Out-Null
$crossLive = New-Settings
$null = Add-Account $crossLive 'same-live-user' '37.18.192.41' 7505
$null = Add-Account $crossLive ' SAME-LIVE-USER ' '37.18.192.41' 7506
Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($crossLive)) } 'One char per account must span selected Live dimensions.' | Out-Null
$crossLive.LoginServers[1].MaxCharactersPerAccount = 0
Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($crossLive)) } 'Selected Live dimension limits must use the strictest positive value.' | Out-Null
$crossLive.LoginServers[2].MaxCharactersPerAccount = 0
$selectedMethod.Invoke($null, [object[]]@($crossLive)) | Out-Null
Assert $true 'Both selected Live endpoints explicitly unlimited must override the default.'
$prkChars = New-Settings
$prkCharAccount = Add-Account $prkChars 'prk-chars'
$null = Add-Character $prkCharAccount
Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($prkChars)) } 'PRK must default to one char per account.' | Out-Null
$prkChars.LoginServers[3].MaxCharactersPerAccount = 0
$selectedMethod.Invoke($null, [object[]]@($prkChars)) | Out-Null
Assert $true 'PRK explicit unlimited chars must be independent of its two-account cap.'
$customCaps = Clone-Settings $defaults
$customCaps.LoginServers.Clear()
$customProfile = [Activator]::CreateInstance($profileType)
$customProfile.Name = 'Custom limits'; $customProfile.Server = '192.0.2.30'; $customProfile.Port = 7030
$customProfile.MaxAccounts = 2; $customProfile.MaxCharactersPerAccount = 2
$customCaps.LoginServers.Add($customProfile)
$customAccount = Add-Account $customCaps 'custom-user' '192.0.2.30' 7030
$null = Add-Character $customAccount
$selectedMethod.Invoke($null, [object[]]@($customCaps)) | Out-Null
Assert $true 'A custom server must accept the configured character count.'
$null = Add-Character $customAccount
Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($customCaps)) } 'A custom server must enforce its character cap.' | Out-Null
$unlimited = New-Settings
1..12 | ForEach-Object { $null = Add-Account $unlimited ('rebirth-' + $_) '2.24.96.30' 7500 }
$null = Add-Account $unlimited 'prk-one'; $null = Add-Account $unlimited 'prk-two'
$null = Add-Account $unlimited 'live-one' '37.18.192.41' 7505
$selectedMethod.Invoke($null, [object[]]@($unlimited)) | Out-Null
Assert $true 'Unlimited and independent server selections must be accepted together.'
'PASS: legacy migration, explicit overrides, duplicate endpoints, username normalization, selected limits and independent servers.'

foreach ($language in @('en', 'da')) {
    $languageMethod.Invoke($null, [object[]]@($language)) | Out-Null
    $oneOfTwo = if ($language -eq 'da') { 'tillader højst 1 valgte chars. Du har valgt 2.' } else { 'allows at most 1 selected characters. You selected 2.' }
    $twoOfThree = if ($language -eq 'da') { 'tillader højst 2 valgte chars. Du har valgt 3.' } else { 'allows at most 2 selected characters. You selected 3.' }
    $customName = if ($language -eq 'da') { 'Brugerdefineret server' } else { 'Custom server' }
    $namedLive = New-Settings
    $namedLive.LoginServers[1].Name = 'Renamed primary dimension'
    $namedLive.LoginServers[2].Name = 'Renamed second dimension'
    $namedAccount = Add-Account $namedLive 'same-live-user' '37.18.192.41' 7505
    $namedAccount.Label = 'Selected account label'; $namedAccount.ServerProfileId = 'live'
    $null = Add-Character $namedAccount
    $unrelatedLive = Add-Account $namedLive 'different-live-user' '37.18.192.41' 7506
    $unrelatedLive.ServerProfileId = 'live-2019'
    $message = Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($namedLive)) } 'Renamed Live profile must still enforce its character cap.'
    Assert ($message.Contains('Selected account label') -and $message.Contains('Renamed primary dimension') -and $message.Contains('37.18.192.41:7505') -and $message.Contains($oneOfTwo)) ('Live error must include account label, configured name, endpoint and translated counts: ' + $language)
    Assert (!$message.Contains('Renamed second dimension') -and !$message.Contains('37.18.192.41:7506') -and !$message.Contains('official Live')) 'An unrelated selected account must not add its dimension to the character-limit error.'
    $namedAccount.Characters[1].Selected = $false
    $unrelatedLive.Username = ' SAME-LIVE-USER '
    $message = Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($namedLive)) } 'The same account across renamed Live dimensions must retain its shared cap.'
    Assert ($message.Contains('Renamed primary dimension') -and $message.Contains('37.18.192.41:7505') -and $message.Contains('Renamed second dimension') -and $message.Contains('37.18.192.41:7506') -and $message.Contains($oneOfTwo)) 'Cross-dimension error must name both selected dimensions and endpoints dynamically.'

    $namedPrk = New-Settings
    $namedPrk.LoginServers[3].Name = 'Renamed Ithaca realm'
    $namedPrk.LoginServers[3].MaxCharactersPerAccount = 2
    $namedPrkAccount = Add-Account $namedPrk 'renamed-prk-user'
    $namedPrkAccount.ServerProfileId = 'prk'
    $null = Add-Character $namedPrkAccount; $null = Add-Character $namedPrkAccount
    $message = Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($namedPrk)) } 'Renamed PRK profile must enforce its configured cap.'
    Assert ($message.Contains('Renamed Ithaca realm') -and $message.Contains('5.161.243.214:7000') -and $message.Contains($twoOfThree) -and !$message.Contains('Project Rubi-Ka')) 'PRK error must use the current profile name and configured count.'
    $namedPrk.LoginServers[3].MaxCharactersPerAccount = 1
    $namedPrkAccount.Characters[2].Selected = $false
    $message = Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($namedPrk)) } 'The one-character PRK default must use the same dynamic error path.'
    Assert ($message.Contains('Renamed Ithaca realm') -and $message.Contains('5.161.243.214:7000') -and $message.Contains($oneOfTwo) -and !$message.Contains('Project Rubi-Ka')) 'A one-character PRK cap must not use a hardcoded server name.'
    $namedPrkAccount.Characters[1].Selected = $false
    $null = Add-Account $namedPrk 'second-renamed-prk-user'; $null = Add-Account $namedPrk 'third-renamed-prk-user'
    $message = Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($namedPrk)) } 'Renamed PRK profile must retain its independent account cap.'
    $accountCount = if ($language -eq 'da') { 'tillader højst 2 valgte konti. Du har valgt 3.' } else { 'allows at most 2 selected accounts. You selected 3.' }
    Assert ($message.Contains('Renamed Ithaca realm') -and $message.Contains('5.161.243.214:7000') -and $message.Contains($accountCount) -and !$message.Contains('Project Rubi-Ka')) 'Account-limit errors must also use the renamed server and translated counts.'

    $namedCustom = New-Settings
    $namedCustom.LoginServers.Clear()
    foreach ($name in @('Unused endpoint alias', 'My chosen custom realm')) {
        $profile = [Activator]::CreateInstance($profileType)
        $profile.Name = $name; $profile.Server = '192.0.2.70'; $profile.Port = 7070
        $profile.MaxAccounts = 0; $profile.MaxCharactersPerAccount = 1
        $namedCustom.LoginServers.Add($profile)
    }
    $namedCustomAccount = Add-Account $namedCustom 'custom-named-user' '192.0.2.70' 7070
    $namedCustomAccount.ServerProfileId = $namedCustom.LoginServers[1].Id
    $null = Add-Character $namedCustomAccount
    $message = Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($namedCustom)) } 'Linked custom profile must enforce its character cap.'
    Assert ($message.Contains('My chosen custom realm') -and !$message.Contains('Unused endpoint alias') -and $message.Contains('192.0.2.70:7070') -and $message.Contains($oneOfTwo)) 'The selected linked profile name must take priority over another profile on the same endpoint.'
    $namedCustom.LoginServers.RemoveAt(0)
    $namedCustomAccount.ServerProfileId = 'missing-profile'
    $message = Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($namedCustom)) } 'A missing profile link must retain endpoint limits.'
    Assert ($message.Contains('My chosen custom realm') -and $message.Contains('192.0.2.70:7070')) 'A missing link must use a configured name matching the current endpoint.'
    $wrongProfile = [Activator]::CreateInstance($profileType)
    $wrongProfile.Id = 'missing-profile'; $wrongProfile.Name = 'Wrong endpoint name'; $wrongProfile.Server = '192.0.2.71'; $wrongProfile.Port = 7071
    $wrongProfile.MaxAccounts = 0; $wrongProfile.MaxCharactersPerAccount = 0
    $namedCustom.LoginServers.Add($wrongProfile)
    $message = Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($namedCustom)) } 'A stale profile link must not change the account endpoint.'
    Assert ($message.Contains('My chosen custom realm') -and !$message.Contains('Wrong endpoint name')) 'A linked profile with a different endpoint must not supply the displayed server name.'
    $namedCustom.LoginServers[0].Name = '  '
    $message = Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($namedCustom)) } 'A blank profile name must not disable the configured cap.'
    Assert ($message.Contains($customName) -and $message.Contains('192.0.2.70:7070') -and $message.Contains($oneOfTwo)) 'A blank server name must fall back to the localized custom-server label and endpoint.'
    $namedCustom.LoginServers[0].MaxCharactersPerAccount = 0
    $selectedMethod.Invoke($null, [object[]]@($namedCustom)) | Out-Null
    Assert $true 'An explicit zero character cap must remain unlimited after dynamic naming.'

    $missingCatalog = New-Settings
    $missingCatalog.LoginServers.Clear()
    $missingAccount = Add-Account $missingCatalog 'unlinked-live-user' '37.18.192.41' 7505
    $null = Add-Character $missingAccount
    $message = Expect-Rejection { $selectedMethod.Invoke($null, [object[]]@($missingCatalog)) } 'A missing catalog must retain the default endpoint cap.'
    Assert ($message.Contains($customName) -and $message.Contains('37.18.192.41:7505') -and $message.Contains($oneOfTwo)) 'An unconfigured endpoint must have a safe localized display name.'
    foreach ($character in $missingAccount.Characters) { $character.Selected = $false }
    $selectedMethod.Invoke($null, [object[]]@($missingCatalog)) | Out-Null
    Assert $true 'An empty selection must not produce a character-limit error.'
}
$languageMethod.Invoke($null, [object[]]@('en')) | Out-Null
'PASS: bilingual character-limit errors use renamed profile names and endpoints, scope shared Live dimensions to the offending account, prefer valid linked names, handle stale/blank/missing names, and preserve zero/empty-selection behavior.'

$wireSettings = New-Settings
$wirePrk = Add-Account $wireSettings 'wire-prk'
$wireLive = Add-Account $wireSettings 'wire-live' '37.18.192.41' 7505
$wireRebirth = Add-Account $wireSettings 'wire-rebirth' '2.24.96.30' 7500
$wireCustom = Add-Account $wireSettings 'wire-custom' '192.0.2.80' 7080
$wirePrk.ServerProfileId = 'prk'; $wireSettings.LoginServers[3].Name = 'Wire renamed Ithaca'
$wireLive.ServerProfileId = 'live'; $wireSettings.LoginServers[1].Name = 'Wire renamed Live'
$wireSettings.LoginServers[3].MaxAccounts = 3
$wireSettings.LoginServers[3].MaxCharactersPerAccount = 2
$wireCredentials = New-Object 'System.Collections.Generic.Dictionary[string,string]'
foreach ($account in $wireSettings.Accounts) { $wireCredentials.Add($account.Id, 'synthetic-only') }
$wireRequest = $assembly.GetType('AOLauncher.MainForm').GetMethod('CreateLaunchRequest', $static).Invoke($null, [object[]]@($wireSettings, $wireCredentials.PSObject.BaseObject))
$wireJson = $serializer.DeserializeObject($serializer.Serialize($wireRequest))
Assert ($wireJson.Characters.Count -eq 4) 'The backend wire request must include selected characters.'
Assert ($wireJson.Characters[0].MaxAccounts -eq 3 -and $wireJson.Characters[0].MaxCharactersPerAccount -eq 2) 'The backend wire request must contain both configured PRK caps.'
Assert ($wireJson.Characters[1].MaxAccounts -eq 0 -and $wireJson.Characters[1].MaxCharactersPerAccount -eq 1) 'The backend wire request must distinguish unlimited accounts from one char per Live account.'
Assert ($wireJson.Characters[2].MaxAccounts -eq 0 -and $wireJson.Characters[2].MaxCharactersPerAccount -eq 0) 'The backend wire request must preserve both explicit unlimited Rebirth caps.'
Assert ($wireJson.Characters[0].ServerName -ceq 'Wire renamed Ithaca' -and $wireJson.Characters[1].ServerName -ceq 'Wire renamed Live') 'The backend wire request must carry the configured server name independently from its endpoint.'
Assert ($wireJson.Characters[2].ServerName -ceq $wireSettings.LoginServers[0].Name -and $wireJson.Characters[3].ServerName -ceq 'Custom server') 'Unlinked request entries must use a matching endpoint name or localized custom-server fallback.'
foreach ($character in $wireRequest.Characters) { $character.Password = $null }
$languageMethod.Invoke($null, [object[]]@('da')) | Out-Null
$wireRequest = $assembly.GetType('AOLauncher.MainForm').GetMethod('CreateLaunchRequest', $static).Invoke($null, [object[]]@($wireSettings, $wireCredentials.PSObject.BaseObject))
Assert ($wireRequest.Characters[0].ServerName -ceq 'Wire renamed Ithaca' -and $wireRequest.Characters[3].ServerName -ceq 'Brugerdefineret server') 'Language changes must preserve user server names while localizing the request fallback.'
foreach ($character in $wireRequest.Characters) { $character.Password = $null }
$languageMethod.Invoke($null, [object[]]@('en')) | Out-Null
$wireCredentials.Clear(); $wireJson = $null
'PASS: serialized GUI-to-backend request carries account and character caps independently, including zero overrides, and dynamic server names with bilingual fallbacks.'

$settingsDialogType = $assembly.GetType('AOLauncher.SettingsDialog')
$cancelSettings = New-Settings
$cancelSettings.LoginServers[3].MaxAccounts = 3
$cancelSettings.LoginServers[3].MaxCharactersPerAccount = 4
$snapshot = $serializer.Serialize($cancelSettings)
$dialog = [Activator]::CreateInstance($settingsDialogType, [object[]]@($cancelSettings))
try {
    Assert ($dialog.LoginServers[3].MaxAccounts -eq 3) 'Settings clone must preserve custom limits.'
    Assert ($dialog.LoginServers[3].MaxCharactersPerAccount -eq 4) 'Settings clone must preserve custom character limits.'
    $dialog.LoginServers[3].MaxAccounts = 7
    $dialog.LoginServers[3].MaxCharactersPerAccount = 8
    Click-Editor $dialog 'CancelAction'
    Assert ($serializer.Serialize($cancelSettings) -ceq $snapshot) 'Cancel must not mutate the original limits.'
} finally { $dialog.Dispose() }
$dialog = [Activator]::CreateInstance($settingsDialogType, [object[]]@($cancelSettings))
try {
    $dialog.LoginServers[3].MaxAccounts = 0
    $dialog.LoginServers[3].MaxCharactersPerAccount = 0
    Click-Editor $dialog
    Assert ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::OK) 'Settings Save must accept edited limits.'
    $targets.GetMethod('ChangeProfiles', $static).Invoke($null, [object[]]@($cancelSettings, $dialog.LoginServers)) | Out-Null
    Assert ((Clone-Settings $cancelSettings).LoginServers[3].MaxAccounts -eq 0) 'Saved unlimited override must roundtrip.'
    Assert ((Clone-Settings $cancelSettings).LoginServers[3].MaxCharactersPerAccount -eq 0) 'Saved unlimited character override must roundtrip.'
} finally { $dialog.Dispose() }

foreach ($language in @('en', 'da')) {
    $languageMethod.Invoke($null, [object[]]@($language)) | Out-Null
    foreach ($column in @('MaxAccounts', 'MaxCharactersPerAccount')) {
      foreach ($inputValue in @('0', '2', '1.5', 'not-a-number', '-1', '101')) {
        $config = New-Settings; $original = $serializer.Serialize($config)
        $dialog = New-ServerDialog $config.LoginServers
        try {
            $grid = $dialog.GetType().GetField('grid', $instance).GetValue($dialog)
            Assert ($grid.Columns.Count -eq 6 -and $grid.Columns.Contains('MaxAccounts') -and $grid.Columns.Contains('MaxCharactersPerAccount') -and $grid.Columns.Contains('CharacterSlots')) 'Server editor must expose both simultaneous limits and total character slots.'
            $grid.Rows[3].Cells[$column].Value = $inputValue
            Click-Editor $dialog
            if ($inputValue -in @('0', '2')) {
                Assert ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::OK -and $dialog.LoginServers[3].$column -eq [int]$inputValue) ('Valid grid limit rejected: ' + $inputValue)
            } else {
                $errorText = $dialog.GetType().BaseType.GetField('ErrorLabel', $instance).GetValue($dialog).Text
                Assert ($dialog.DialogResult -ne [Windows.Forms.DialogResult]::OK -and $errorText.Contains('100') -and $errorText.Contains('0')) ('Invalid grid limit accepted or no guidance: ' + $inputValue)
            }
            Assert ($serializer.Serialize($config) -ceq $original) 'Server editor must return copies without changing its input.'
        } finally { $dialog.Dispose() }
      }
    }
    $dialog = New-ServerDialog (New-Settings).LoginServers
    try {
        $dialog.ShowInTaskbar=$false; $dialog.StartPosition='Manual'; $dialog.Location=[Drawing.Point]::new(-32000,-32000); $dialog.Show()
        Layout $dialog; Check-Layout $dialog
        $grid = $dialog.GetType().GetField('grid', $instance).GetValue($dialog)
        Assert ($grid.Parent.ClientRectangle.Contains($grid.Bounds) -and $grid.Height -ge 150) 'Server grid must remain inside the editor.'
        if ($language -eq 'da') { Save-Preview $dialog 'login-servers-da.png' }
        $dialog.Font = [Drawing.Font]::new($dialog.Font.FontFamily, $dialog.Font.Size * 1.5, $dialog.Font.Style)
        $dialog.Scale([Drawing.SizeF]::new(1.5, 1.5))
        $dialog.Show()
        Layout $dialog; Check-Layout $dialog
        Assert ($grid.Parent.ClientRectangle.Contains($grid.Bounds)) 'Server grid must fit at simulated 150% scale.'
    } finally { $dialog.Dispose() }
}
'PASS: Settings clone/Cancel/Save and real server grid accept whole-number limits, reject invalid input, and fit English/Danish at 100% and simulated 150% scale.'

$formType = $assembly.GetType('AOLauncher.MainForm')
$form = [Activator]::CreateInstance($formType, [object[]]@($true))
try {
    $config = $formType.GetField('settings', $instance).GetValue($form)
    $config.Accounts.Clear()
    $null = Add-Account $config 'First PRK account'; $null = Add-Account $config 'Second PRK account'
    $third = Add-Account $config 'Third PRK account'; $third.Characters[0].Selected = $false
    $null = Add-Account $config 'Rebirth account' '2.24.96.30' 7500
    $tree = $formType.GetField('tree', $instance).GetValue($form)
    $null = $form.Handle; $null = $tree.Handle
    foreach ($language in @('en', 'da')) {
        $config.Language = $language
        $formType.GetMethod('ApplyLanguage', $instance).Invoke($form, @()) | Out-Null
        $prk = @($tree.Nodes | Where-Object { $_.Text.Contains('Project Rubi-Ka') })[0]
        $rebirth = @($tree.Nodes | Where-Object { $_.Text.Contains('AO Rebirth') })[0]
        $accountWord = if ($language -eq 'da') { 'konti' } else { 'accounts' }
        $unlimitedWord = if ($language -eq 'da') { 'Ubegr' } else { 'Unlimited' }
        Assert ($prk.Text.Contains('2/2 ' + $accountWord)) 'Server overview must show selected accounts and the limit.'
        $selectedWord = if ($language -eq 'da') { 'konti valgt' } else { 'accounts selected' }
        Assert ($prk.Text.Contains($selectedWord)) 'Server account ratios must explicitly describe selected accounts rather than character capacity.'
        $charsWord = if ($language -eq 'da') { 'Maks. chars online/konto: 1' } else { 'Max. chars online/account: 1' }
        $tooltipBindings = $assembly.GetType('AOLauncher.NexusToolTips', $true).GetField('bindings', $static).GetValue($null)
        $bindingArguments = [object[]]@($tree, $null)
        $hasBinding = [bool]$tooltipBindings.GetType().GetMethod('TryGetValue').Invoke($tooltipBindings, $bindingArguments)
        Assert (!$tree.ShowNodeToolTips -and $hasBinding -and $null -ne $bindingArguments[1]) 'Compact server overview must use the attached themed tooltip binding instead of native tooltips.'
        Assert ($prk.ToolTipText.Contains($charsWord)) 'Compact server overview must expose the separate character limit per account in its tooltip.'
        Assert ($rebirth.Text.Contains('/∞ ' + $accountWord) -and $rebirth.ToolTipText.Contains($unlimitedWord)) 'Compact server overview must show infinity and explain unlimited in its tooltip.'
        Assert ($prk.ForeColor.ToArgb() -ne $assembly.GetType('AOLauncher.Theme').GetField('Warning').GetValue($null).ToArgb()) 'A selection at the limit must not appear over-limit.'
        $third.Characters[0].Selected = $true
        $formType.GetMethod('RefreshTree', $instance).Invoke($form, [object[]]@($null)) | Out-Null
        $prk = @($tree.Nodes | Where-Object { $_.Text.Contains('Project Rubi-Ka') })[0]
        $tree.SelectedNode = $prk
        $formType.GetMethod('UpdateSelection', $instance).Invoke($form, @()) | Out-Null
        $details = $formType.GetField('detailText', $instance).GetValue($form).Text
        $warning = if ($language -eq 'da') { 'Valget overskrider serverens' } else { 'Selection exceeds the server limits' }
        Assert ($prk.Text.Contains('3/2 ' + $accountWord) -and $prk.ForeColor.ToArgb() -eq $assembly.GetType('AOLauncher.Theme').GetField('Warning').GetValue($null).ToArgb()) 'An excessive selection must be shown in red with its actual count.'
        Assert ($details.Contains($warning) -and $details.Contains('2')) 'Selected server details must explain the account limit and excessive selection.'
        Layout $form
        if ($language -eq 'en') { Save-Preview $form 'server-account-limits-en.png' }
        $third.Characters[0].Selected = $false
        $extraChar = Add-Character $config.Accounts[0]
        $formType.GetMethod('RefreshTree', $instance).Invoke($form, [object[]]@($null)) | Out-Null
        $prk = @($tree.Nodes | Where-Object { $_.Text.Contains('Project Rubi-Ka') })[0]
        Assert ($prk.Text.Contains('2/2 ' + $accountWord) -and $prk.ForeColor.ToArgb() -eq $assembly.GetType('AOLauncher.Theme').GetField('Warning').GetValue($null).ToArgb()) 'Exceeding only the character cap must also mark the server red.'
        $config.Accounts[0].Characters.Remove($extraChar) | Out-Null
    }
    Assert (!$formType.GetField('running', $instance).GetValue($form)) 'Smoke tests must not start a login.'
} finally { $form.Dispose() }
'PASS: bilingual server overview shows account counts/limits, unlimited servers and red over-limit guidance using isolated smoke data.'

# Reproduce the reported visual comparison using synthetic settings: unlimited
# simultaneous Live accounts, one char online/account, six creation slots/account.
$slotMethod = $targets.GetMethod('ServerCharacterSlotLimit', $static)
$accountSlotMethod = $targets.GetMethod('CharacterSlotLimit', $static)
$translation = $assembly.GetType('AOLauncher.Localization.Texts').GetMethod('T', $static)
function Translate([string]$Text) { $translation.Invoke($null, [object[]]@($Text)) }
function Main-Field($Form, [string]$Name) { return ,$formType.GetField($Name, $instance).GetValue($Form) }
function Main-Call($Form, [string]$Name, [object[]]$Arguments = @()) { $formType.GetMethod($Name, $instance).Invoke($Form, $Arguments) }
function Tree-Nodes($Collection) { foreach ($node in $Collection) { $node; Tree-Nodes $node.Nodes } }
foreach ($language in @('en','da')) {
  foreach ($scale in @(1,1.5)) {
    $form = [Activator]::CreateInstance($formType, [object[]]@($true))
    $dialog = $null
    try {
        $config = Main-Field $form 'settings'
        $config.Accounts.Clear(); $config.Presets.Clear(); $config.SelectedPresetId = $null; $config.LoginServers.Clear()
        $profile = [Activator]::CreateInstance($profileType)
        $profile.Id = 'live-fixture'; $profile.Name = 'Funcom AO - Rubi-Ka'
        $profile.Server = '37.18.192.41'; $profile.Port = 7505
        $profile.MaxAccounts = 0; $profile.MaxCharactersPerAccount = 1; $profile.CharacterSlots = 6
        $config.LoginServers.Add($profile)
        $account = Add-Account $config 'Synthetic account' $profile.Server $profile.Port
        $account.ServerProfileId = $profile.Id; $account.Characters[0].Selected = $false
        $config.Language = $language
        Main-Call $form 'ApplyLanguage' | Out-Null
        $form.Size = $form.MinimumSize
        if ($scale -gt 1) {
            $form.Font = [Drawing.Font]::new($form.Font.FontFamily,$form.Font.Size*$scale,$form.Font.Style)
            $form.Scale([Drawing.SizeF]::new($scale,$scale))
        }
        $form.ShowInTaskbar=$false; $form.StartPosition='Manual'; $form.Location=[Drawing.Point]::new(-32000,-32000)
        $form.Show(); Layout $form; [Windows.Forms.Application]::DoEvents()
        Assert ((Main-Field $form 'smoke') -and (Main-Field $form 'newsPanel').GetType().GetField('offlinePreview',$instance).GetValue((Main-Field $form 'newsPanel'))) 'Capacity-label previews must use synthetic accounts and offline news.'
        $tree = Main-Field $form 'tree'; $serverNode = $tree.Nodes[0]
        $tree.SelectedNode=$serverNode; Main-Call $form 'UpdateSelection' | Out-Null; Layout $form
        $text = (Main-Field $form 'detailText').Text
        $selectedPhrase = if ($language -eq 'da') { 'konti valgt' } else { 'accounts selected' }
        Assert ($serverNode.Text.Contains('0/∞') -and $serverNode.Text.Contains($selectedPhrase)) 'The reported 0/infinity value must explicitly mean selected accounts, independent of six char slots.'
        $accountLine = (Translate 'Max. accounts online: {0}') -f (Translate 'Unlimited')
        $charLine = (Translate 'Max. chars online/account: {0}') -f 1
        $slotLine = (Translate 'Default char slots/account: {0}') -f 6
        foreach ($expected in @($accountLine,$charLine,$slotLine)) {
            Assert ($text.Contains($expected)) ('Server details must show this independent limit: ' + $expected)
            Assert ($serverNode.ToolTipText.Contains($expected)) ('Server tooltip must show this independent limit: ' + $expected)
        }
        Assert (($slotMethod.Invoke($null,[object[]]@($config,'37.18.192.41:7505'))) -eq 6) 'The server slot helper must use the configured six-slot value instead of the endpoint fallback.'
        Assert ((Limit $config '37.18.192.41:7505') -eq 0 -and (CharacterLimit $config '37.18.192.41:7505') -eq 1) 'Showing capacity must not change either login limit.'
        $snapshot = $serializer.Serialize($config)
        Save-Preview $form ('separate-server-limits-' + $language + '-' + $scale + '.png')
        Assert ($snapshot -ceq $serializer.Serialize($config)) 'Rendering the three independent limits must not change settings.'

        $account.CharacterSlots = 9
        $accountNode = @(Tree-Nodes $tree.Nodes | Where-Object { [object]::ReferenceEquals($_.Tag,$account) })[0]
        $tree.SelectedNode=$accountNode; Main-Call $form 'UpdateSelection' | Out-Null; Layout $form
        $text = (Main-Field $form 'detailText').Text
        Assert ($text.Contains('1/9') -and $text.Contains((Translate 'Slot setting: account override'))) 'Account details must show the effective nine-slot override and its source.'
        Assert (($accountSlotMethod.Invoke($null,[object[]]@($config,$account))) -eq 9 -and ($slotMethod.Invoke($null,[object[]]@($config,'37.18.192.41:7505'))) -eq 6) 'An account override must not rewrite the displayed server default.'
        $tree.SelectedNode=$serverNode; Main-Call $form 'UpdateSelection' | Out-Null
        Assert ((Main-Field $form 'detailText').Text.Contains($slotLine)) 'The server sidebar must still identify six as its default when an account overrides to nine.'

        $saved = Clone-Settings $config
        Assert ($saved.LoginServers[0].MaxAccounts -eq 0 -and $saved.LoginServers[0].MaxCharactersPerAccount -eq 1 -and $saved.LoginServers[0].CharacterSlots -eq 6 -and $saved.Accounts[0].CharacterSlots -eq 9) 'Settings roundtrip must preserve all three independent values and the account override.'
        Assert (!$saved.Accounts[0].Characters[0].Selected -and $saved.Accounts[0].Characters.Count -eq 1 -and $saved.Presets.Count -eq 0) 'Clarified labels must not alter selected chars, saved counts or squads.'
        $dialog = New-ServerDialog $config.LoginServers
        if ($scale -gt 1) { $dialog.Font=[Drawing.Font]::new($dialog.Font.FontFamily,$dialog.Font.Size*$scale,$dialog.Font.Style); $dialog.Scale([Drawing.SizeF]::new($scale,$scale)) }
        $grid=$dialog.GetType().GetField('grid',$instance).GetValue($dialog)
        foreach ($pair in @(@('MaxAccounts','Max. accounts online'),@('MaxCharactersPerAccount','Max. chars online/account'),@('CharacterSlots','Default char slots/account'))) {
            Assert ($grid.Columns[$pair[0]].HeaderText -ceq (Translate $pair[1])) 'Server editor headers must separate simultaneous login limits from default slot capacity.'
        }
        Save-Preview $dialog ('separate-server-settings-' + $language + '-' + $scale + '.png')
        Click-Editor $dialog
        Assert ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::OK -and $dialog.LoginServers[0].MaxAccounts -eq 0 -and $dialog.LoginServers[0].MaxCharactersPerAccount -eq 1 -and $dialog.LoginServers[0].CharacterSlots -eq 6) 'Resaving the server editor must preserve zero accounts, one simultaneous char and six capacity slots.'
        $dialog.Dispose(); $dialog=$null

        $profile.CharacterSlots=0
        Main-Call $form 'RefreshTree' ([object[]]@($null)) | Out-Null
        $serverNode=$tree.Nodes[0]; $tree.SelectedNode=$serverNode; Main-Call $form 'UpdateSelection' | Out-Null
        $unknownLine=(Translate 'Default char slots/account: {0}') -f (Translate 'No launcher limit / unknown')
        $text=(Main-Field $form 'detailText').Text
        Assert ($text.Contains($unknownLine) -and $text.Contains($accountLine)) 'Zero capacity must say no launcher limit/unknown, while zero simultaneous accounts remains Unlimited.'
        Assert (($accountSlotMethod.Invoke($null,[object[]]@($config,$account))) -eq 9) 'Unknown server capacity must not erase an explicit account override.'
        $account.CharacterSlots=$null
        $accountNode=@(Tree-Nodes $tree.Nodes | Where-Object { [object]::ReferenceEquals($_.Tag,$account) })[0]
        $tree.SelectedNode=$accountNode; Main-Call $form 'UpdateSelection' | Out-Null
        $text=(Main-Field $form 'detailText').Text
        Assert ($text.Contains((Translate 'Slot setting: server default')) -and ($accountSlotMethod.Invoke($null,[object[]]@($config,$account))) -eq 0) 'An inheriting account must identify the server default even when its capacity is unknown.'
        Assert (!(Main-Field $form 'running') -and $null -eq (Main-Field $form 'backendRunner')) 'Capacity-label fixtures must never start a backend.'
    } finally { if($dialog){$dialog.Dispose()};$form.Dispose() }
  }
}
'PASS: screenshot-equivalent Live limits distinguish selected accounts, simultaneous chars and total slots; account overrides, unknown capacity and persistence remain independent in English/Danish at minimum and 150% scale.'
"PASS: $script:checks server account limit checks; no real settings, credentials, game clients or login connections used."
