param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'),
    [string]$PreviewDirectory
)
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') {
    throw 'Run with 32-bit Windows PowerShell -NoProfile -STA.'
}
Add-Type -AssemblyName System.Web.Extensions
Add-Type -AssemblyName System.Windows.Forms
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$static = [Reflection.BindingFlags]'NonPublic,Static'
$instance = [Reflection.BindingFlags]'NonPublic,Instance'
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$targets = $assembly.GetType('AOLauncher.AccountTargets')
$store = $assembly.GetType('AOLauncher.SettingsStore')
$assembly.GetType('AOLauncher.GuiStrings').GetMethod('Register', $static).Invoke($null, @()) | Out-Null
$language = $assembly.GetType('AOLauncher.Localization.Texts').GetMethod('SetLanguage', $static)
$language.Invoke($null, [object[]]@('en')) | Out-Null
$script:checks = 0
function Assert([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    $script:checks++
}
function Make([string]$Name) { [Activator]::CreateInstance($assembly.GetType('AOLauncher.' + $Name)) }
function Call([string]$Method, [object[]]$Arguments) { $targets.GetMethod($Method, $static).Invoke($null, $Arguments) }
function Clone($Value) { $store.GetMethod('Deserialize', $static).Invoke($null, [object[]]@($serializer.Serialize($Value))) }
function Reject([scriptblock]$Action, [string]$Message) {
    $failure = $null
    try { & $Action | Out-Null } catch { $failure = $_.Exception.GetBaseException() }
    Assert ($failure -is [ArgumentException]) $Message
}
function Add-Account($Settings, [string]$User, [string]$Server = '2.24.96.30', [int]$Port = 7500) {
    $account = Make 'AccountConfig'; $account.Username = $User; $account.Label = $User.Trim()
    $account.Server = $Server; $account.Port = $Port; $account.GamePath = 'C:\SyntheticGames\SlotsTest'
    $Settings.Accounts.Add($account); return $account
}
function Add-Char($Account, [int]$Id, [bool]$Selected = $false) {
    $character = Make 'CharacterConfig'; $character.CharacterId = $Id; $character.Name = 'Synthetic ' + $Id; $character.Selected = $Selected
    $Account.Characters.Add($character)
}
function Click($Dialog) {
    $button = $Dialog.GetType().BaseType.GetField('SaveButton', $instance).GetValue($Dialog)
    $button.GetType().GetMethod('OnClick', $instance).Invoke($button, [object[]]@([EventArgs]::Empty)) | Out-Null
}
function Server-Dialog($Profiles) {
    $arguments = New-Object object[] 1; $arguments[0] = $Profiles
    return [Activator]::CreateInstance($assembly.GetType('AOLauncher.LoginServersDialog'), $arguments)
}
function Preview($Dialog, [string]$Name) {
    if (!$PreviewDirectory) { return }
    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PreviewDirectory)) | Out-Null
    $Dialog.ShowInTaskbar = $false; $Dialog.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $Dialog.Location = [Drawing.Point]::new(-32000, -32000)
    $Dialog.Show(); [Windows.Forms.Application]::DoEvents()
    $bitmap = [Drawing.Bitmap]::new($Dialog.Width, $Dialog.Height)
    try {
        $Dialog.DrawToBitmap($bitmap, [Drawing.Rectangle]::new(0, 0, $Dialog.Width, $Dialog.Height))
        $bitmap.Save((Join-Path ([IO.Path]::GetFullPath($PreviewDirectory)) $Name), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose(); $Dialog.Hide() }
}

$settings = Make 'LauncherSettings'
Assert ((@($settings.LoginServers | ForEach-Object CharacterSlots) -join ',') -eq '6,0,0,0,0') 'Default slots must reflect known Rebirth baseline and unknown other servers.'
$legacy = $store.GetMethod('Deserialize', $static).Invoke($null, [object[]]@('{"Version":1,"Accounts":[],"LoginServers":[{"Id":"r","Name":"Renamed Rebirth","Server":"2.24.96.30","Port":7500},{"Id":"p","Name":"PRK","Server":"5.161.243.214","Port":7000}]}'))
Assert ($legacy.LoginServers[0].CharacterSlots -eq 6 -and $legacy.LoginServers[1].CharacterSlots -eq 0) 'Missing profile slots migrate by endpoint without relying on names.'
$legacy.LoginServers[0].CharacterSlots = 0; $legacy.LoginServers[1].CharacterSlots = 18
$roundtrip = Clone $legacy
Assert ($roundtrip.LoginServers[0].CharacterSlots -eq 0 -and $roundtrip.LoginServers[1].CharacterSlots -eq 18) 'Explicit zero and custom server capacity survive migration.'
$account = Add-Account $settings ' SameLogin '
Assert ($null -eq $account.CharacterSlots -and (Call 'CharacterSlotLimit' @($settings, $account)) -eq 6) 'New accounts inherit the server limit.'
Assert (Call 'CanCreateCharacter' @($settings, $account)) 'An empty account has creation room.'
1..5 | ForEach-Object { Add-Char $account $_ }
Assert ((Call 'KnownCharacterCount' @($settings, $account)) -eq 5) 'Unchecked saved characters consume slots.'
Assert (Call 'CanCreateCharacter' @($settings, $account)) 'One remaining slot allows creation.'
Add-Char $account 6 $true
Assert (!(Call 'CanCreateCharacter' @($settings, $account))) 'An account at its configured capacity cannot create another character.'
$account.CharacterSlots = 9
Assert ((Call 'CharacterSlotLimit' @($settings, $account)) -eq 9 -and (Call 'CanCreateCharacter' @($settings, $account))) 'Account overrides support extra purchased slots.'
$account.CharacterSlots = 0
Assert ((Call 'CharacterSlotLimit' @($settings, $account)) -eq 0 -and (Call 'CanCreateCharacter' @($settings, $account))) 'Explicit account zero disables the launcher cap.'
$account.CharacterSlots = $null
$duplicate = Add-Account $settings 'samelogin'
Add-Char $duplicate 6; Add-Char $duplicate 7
Add-Char $duplicate 0; Add-Char $duplicate -1
$other = Add-Account $settings 'other'; Add-Char $other 8
$otherServer = Add-Account $settings 'sameLOGIN' '5.161.243.214' 7000; Add-Char $otherServer 9
Assert ((Call 'KnownCharacterCount' @($settings, $account)) -eq 7) 'Count unions distinct positive IDs on same endpoint and trimmed case-insensitive username only.'
Assert (!(Call 'CanCreateCharacter' @($settings, $account))) 'Duplicate saved account entries cannot hide a full account.'
$duplicate.Characters.RemoveAt(3); $duplicate.Characters.RemoveAt(2)
$saved = Clone $settings
Assert ($null -eq $saved.Accounts[0].CharacterSlots) 'Inherited account capacity remains null after a settings roundtrip.'
$settings.Accounts[0].CharacterSlots = 12
$saved = Clone $settings
Assert ($saved.Accounts[0].CharacterSlots -eq 12) 'Positive account overrides survive a settings roundtrip.'
$settings.Accounts[0].CharacterSlots = 0
Assert ((Clone $settings).Accounts[0].CharacterSlots -eq 0) 'Explicit account zero survives a settings roundtrip.'
foreach ($invalid in @(-1, 1001)) {
    $invalidSettings = Make 'LauncherSettings'; $invalidSettings.LoginServers[0].CharacterSlots = $invalid
    Reject { Clone $invalidSettings } 'Out-of-range profile capacity must be rejected.'
    $invalidSettings = Make 'LauncherSettings'; $badAccount = Add-Account $invalidSettings 'Invalid'
    $badAccount.CharacterSlots = $invalid
    Reject { Clone $invalidSettings } 'Out-of-range account capacity must be rejected.'
}
$custom = Make 'LauncherSettings'; $custom.LoginServers.Clear()
$customAccount = Add-Account $custom 'Custom' '192.0.2.8' 7008
Assert ((Call 'CharacterSlotLimit' @($custom, $customAccount)) -eq 0) 'Unknown endpoints default to no launcher cap.'
$newAccount = Make 'AccountConfig'; $newAccount.Username = 'New'; $newAccount.Server = '2.24.96.30'; $newAccount.Port = 7500
Add-Char $newAccount 80
Assert ((Call 'KnownCharacterCount' @($custom, $newAccount)) -eq 1) 'Unsaved account candidates still count their own characters.'

$config = Make 'LauncherSettings'; $configAccount = Add-Account $config 'SaveAndCancel'; $configAccount.CharacterSlots = 14
$snapshot = $serializer.Serialize($config)
$settingsDialog = [Activator]::CreateInstance($assembly.GetType('AOLauncher.SettingsDialog'), [object[]]@($config))
try {
    Assert ($settingsDialog.LoginServers[0].CharacterSlots -eq 6) 'Settings dialog copies slot capacities.'
    $settingsDialog.LoginServers[0].CharacterSlots = 23
    Assert ($serializer.Serialize($config) -ceq $snapshot) 'Uncommitted Settings changes must not mutate the source.'
} finally { $settingsDialog.Dispose() }
$settingsDialog = [Activator]::CreateInstance($assembly.GetType('AOLauncher.SettingsDialog'), [object[]]@($config))
try {
    $settingsDialog.LoginServers[0].CharacterSlots = 23; Click $settingsDialog
    Call 'ChangeProfiles' @($config, $settingsDialog.LoginServers) | Out-Null
    Assert ($config.LoginServers[0].CharacterSlots -eq 23 -and $config.Accounts[0].CharacterSlots -eq 14) 'Saving server capacity preserves account overrides.'
} finally { $settingsDialog.Dispose() }

foreach ($locale in @('en', 'da')) {
    $language.Invoke($null, [object[]]@($locale)) | Out-Null
    foreach ($value in @('0', '8', '1000', '-1', '1001', '1.5', 'unknown')) {
        $dialog = Server-Dialog $config.LoginServers
        try {
            $grid = $dialog.GetType().GetField('grid', $instance).GetValue($dialog)
            Assert ($grid.Columns.Count -eq 6 -and $grid.Columns.Contains('CharacterSlots')) 'Slot capacity needs its own independent server column.'
            $grid.Rows[0].Cells['CharacterSlots'].Value = $value; Click $dialog
            if ($value -in @('0', '8', '1000')) {
                Assert ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::OK -and $dialog.LoginServers[0].CharacterSlots -eq [int]$value) 'Valid slot capacity was rejected.'
            } else {
                $errorText = $dialog.GetType().BaseType.GetField('ErrorLabel', $instance).GetValue($dialog).Text
                Assert ($dialog.DialogResult -ne [Windows.Forms.DialogResult]::OK -and $errorText.Contains('1000')) 'Invalid slot capacity must fail with range guidance.'
                if ($locale -eq 'da') { Assert ($errorText.StartsWith('Char-pladser')) 'Danish slot validation must be translated.' }
            }
        } finally { $dialog.Dispose() }
    }
    $dialog = Server-Dialog $config.LoginServers
    try { Preview $dialog ('character-slots-servers-' + $locale + '.png') } finally { $dialog.Dispose() }
    $configAccount.CharacterSlots = $null
    $dialog = [Activator]::CreateInstance($assembly.GetType('AOLauncher.AccountDialog'), [object[]]@($configAccount, $config))
    try {
        $checkbox = $dialog.GetType().GetField('inheritCharacterSlots', $instance).GetValue($dialog)
        $number = $dialog.GetType().GetField('characterSlotsInput', $instance).GetValue($dialog)
        Assert ($checkbox.Checked -and !$number.Enabled -and $number.Value -eq 23 -and $null -eq $dialog.CharacterSlots) 'Inherited account capacity must show current server value with disabled input.'
        $checkbox.Checked = $false; $number.Value = 17
        Assert ($number.Enabled -and $dialog.CharacterSlots -eq 17) 'Unchecking inheritance enables an account capacity override.'
        Assert ($null -eq $configAccount.CharacterSlots) 'Editing account capacity must not mutate the source before Save.'
        $checkbox.Checked = $true
        Assert ($null -eq $dialog.CharacterSlots -and $number.Value -eq 23) 'Restoring inheritance uses the server value again.'
        Preview $dialog ('character-slots-account-' + $locale + '.png')
    } finally { $dialog.Dispose() }
}
'PASS: ' + $script:checks + ' character-slot checks: defaults, migrations, capacity accounting, account overrides, settings isolation, and bilingual editors.'
