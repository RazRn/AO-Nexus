param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'),
    [string]$PreviewDirectory = (Join-Path $PSScriptRoot '..\logs\password-preference-previews'),
    [switch]$SkipUi
)
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Run in 32-bit Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Web.Extensions, System.Security, System.Windows.Forms, System.Drawing
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$store = $assembly.GetType('AOLauncher.SettingsStore', $true)
$credentials = $assembly.GetType('AOLauncher.CredentialStore', $true)
$texts = $assembly.GetType('AOLauncher.Localization.Texts', $true)
foreach ($registration in @('GuiStrings','NexusStrings')) { $assembly.GetType('AOLauncher.' + $registration, $true).GetMethod('Register', $static).Invoke($null, @()) | Out-Null }
$texts.GetMethod('SetLanguage', $static).Invoke($null, [object[]]@('en')) | Out-Null
$script:checks = 0
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message }; $script:checks++ }
function Make([string]$Name) { [Activator]::CreateInstance($assembly.GetType('AOLauncher.' + $Name, $true)) }
function Field($Control, [string]$Name) {
    $type = $Control.GetType()
    while ($type) { $field = $type.GetField($Name, $flags); if ($field) { return $field.GetValue($Control) }; $type = $type.BaseType }
    throw "Field not found: $Name"
}
function Account([string]$Id, [bool]$Remember) {
    $account = Make 'AccountConfig'; $account.Id = $Id; $account.Label = 'Synthetic ' + $Id
    $account.Username = 'synthetic-' + $Id; $account.Server = '192.0.2.25'; $account.Port = 7025
    $account.GamePath = 'C:\SyntheticGames\PasswordPreferences'; $account.RememberPassword = $Remember
    return $account
}
function Credential([string]$Method, [object[]]$Arguments) { $credentials.GetMethod($Method, $static).Invoke($null, $Arguments) }

$legacy = $store.GetMethod('Deserialize', $static).Invoke($null, [object[]]@('{"Version":2,"Accounts":[{"Id":"legacy-only","Label":"Synthetic old profile","Username":"fixture-user","GamePath":"C:\\SyntheticGames\\PasswordPreferences","Server":"192.0.2.25","Port":7025,"Characters":[]}]}'))
Assert ($legacy.Accounts[0].RememberPassword) 'Existing accounts without the preference must retain remembered-password behavior.'
$legacy.Accounts[0].RememberPassword = $false
$roundtrip = $store.GetMethod('Deserialize', $static).Invoke($null, [object[]]@($serializer.Serialize($legacy)))
Assert (!$roundtrip.Accounts[0].RememberPassword) 'Explicit opt-out must survive settings serialization and normalization.'
$second = Account 'other' $true; $roundtrip.Accounts.Add($second)
$json = $serializer.Serialize($roundtrip)
$again = $store.GetMethod('Deserialize', $static).Invoke($null, [object[]]@($json))
Assert (!$again.Accounts[0].RememberPassword -and $again.Accounts[1].RememberPassword) 'Password preference must be independent for each account.'
Assert ($json -notmatch '"Password"\s*:') 'Only the preference may appear in settings, never a plaintext password property.'

$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$testRoot = [IO.Path]::GetFullPath((Join-Path $tempParent ('AONexus-PasswordPreferences-' + [Guid]::NewGuid().ToString('N'))))
try {
    [IO.Directory]::CreateDirectory($testRoot) | Out-Null
    $first = Account 'first' $true; $second = Account 'second' $true
    Credential 'SaveToDirectory' @($first, 'synthetic-first-secret', $testRoot) | Out-Null
    Credential 'SaveToDirectory' @($second, 'synthetic-second-secret', $testRoot) | Out-Null
    Assert ((Credential 'ReadFromDirectory' @($first, $testRoot)) -ceq 'synthetic-first-secret') 'An opted-in account must keep its encrypted roundtrip.'
    $firstPath = Credential 'AccountPath' @($first, $testRoot)
    $secondPath = Credential 'AccountPath' @($second, $testRoot)
    $otherHash = (Get-FileHash -LiteralPath $secondPath).Hash
    Assert (![Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($firstPath)).Contains('synthetic-first-secret')) 'The fixture password must not be stored as plaintext.'
    $first.RememberPassword = $false
    Assert ($null -eq (Credential 'ReadFromDirectory' @($first, $testRoot))) 'Opt-out must not return an existing encrypted password.'
    Credential 'SaveToDirectory' @($first, 'synthetic-must-not-save', $testRoot) | Out-Null
    Assert (![IO.File]::Exists($firstPath)) 'The store must reject persistence for opt-out and remove its stale encrypted record.'
    Assert ((Get-FileHash -LiteralPath $secondPath).Hash -ceq $otherHash) 'Opting out one account must preserve the other account encrypted bytes.'
    Assert ((Credential 'ReadFromDirectory' @($second, $testRoot)) -ceq 'synthetic-second-secret') 'Other accounts must retain their remembered passwords.'
    Credential 'SaveToDirectory' @($first, 'synthetic-still-transient', $testRoot) | Out-Null
    Assert (![IO.File]::Exists($firstPath) -and @(Get-ChildItem -LiteralPath $testRoot -File).Count -eq 1) 'Repeated opt-out saves must never create a credential or temporary file.'
    $first.RememberPassword = $true
    Credential 'SaveToDirectory' @($first, 'synthetic-first-again', $testRoot) | Out-Null
    $first.RememberPassword = $false
    Credential 'DeleteFromDirectory' @($first, $testRoot) | Out-Null
    Credential 'DeleteFromDirectory' @($first, $testRoot) | Out-Null
    Assert (![IO.File]::Exists($firstPath) -and (Get-FileHash -LiteralPath $secondPath).Hash -ceq $otherHash) 'Explicit account deletion must be scoped and safe to repeat.'

    [string]$source = Join-Path $testRoot 'import-source'; [string]$destination = Join-Path $testRoot 'import-destination'
    [string]$sourceCredentials = Join-Path $source 'Credentials'; [string]$destinationCredentials = Join-Path $destination 'Credentials'
    $first.RememberPassword = $true
    Credential 'SaveToDirectory' @($first, 'synthetic-excluded-import-secret', $sourceCredentials) | Out-Null
    Credential 'SaveToDirectory' @($second, 'synthetic-retained-import-secret', $sourceCredentials) | Out-Null
    $first.RememberPassword = $false
    $importSettings = Make 'LauncherSettings'; $importSettings.Accounts.Add($first); $importSettings.Accounts.Add($second)
    [IO.File]::WriteAllText((Join-Path $source 'AOLauncher.json'), $serializer.Serialize($importSettings))
    $excludedSource = Credential 'AccountPath' @($first, $sourceCredentials)
    $excludedHash = (Get-FileHash -LiteralPath $excludedSource).Hash
    $imported = $assembly.GetType('AOLauncher.LauncherBootstrap', $true).GetMethod('ImportFromDirectory', $static).Invoke($null, [object[]]@($source,$destination))
    Assert ($imported) 'The synthetic profile import must succeed with mixed password choices.'
    Assert (![IO.File]::Exists((Credential 'AccountPath' @($first,$destinationCredentials)))) 'Import must not copy an opted-out account stale credential.'
    Assert ((Credential 'ReadFromDirectory' @($second,$destinationCredentials)) -ceq 'synthetic-retained-import-secret') 'Import must retain encrypted credentials for opted-in accounts.'
    Assert ((Get-FileHash -LiteralPath $excludedSource).Hash -ceq $excludedHash) 'Import must leave the original profile credential bytes untouched.'
    $importedSettings = $store.GetMethod('ReadFromDirectory', $static).Invoke($null, [object[]]@($destination))
    Assert (!$importedSettings.Accounts[0].RememberPassword -and $importedSettings.Accounts[1].RememberPassword) 'Import must preserve both per-account choices.'
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (!$resolved.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'AONexus-PasswordPreferences-*') { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

Add-Type -Path (Join-Path $PSScriptRoot 'PasswordPreferences.Tests.cs') -ReferencedAssemblies System.Core
$script:checks += [PasswordPreferencesTests]::Run($assembly)

if (!$SkipUi) {
    Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class PasswordPreferenceNative {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    public static void Click(IntPtr window) { SendMessage(window,0x00F5,IntPtr.Zero,IntPtr.Zero); }
}
'@
    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PreviewDirectory)) | Out-Null
    foreach ($language in @('en','da')) {
        $texts.GetMethod('SetLanguage', $static).Invoke($null, [object[]]@($language)) | Out-Null
        foreach ($scale in @(1.0,1.5)) {
            foreach ($kind in @('AccountDialog','PasswordDialog')) {
                $account = Account 'dialog-only' $true
                $arguments = if ($kind -eq 'AccountDialog') { [object[]]@($account, (Make 'LauncherSettings')) } else { [object[]]@($account) }
                $dialog = [Activator]::CreateInstance($assembly.GetType('AOLauncher.' + $kind, $true), $flags, $null, $arguments, $null)
                try {
                    $dialog.ShowInTaskbar = $false; $dialog.StartPosition = [Windows.Forms.FormStartPosition]::Manual
                    $dialog.Location = [Drawing.Point]::new(-32000,-32000)
                    if ($scale -ne 1) { $dialog.Scale([Drawing.SizeF]::new($scale,$scale)); $dialog.Font = [Drawing.Font]::new($dialog.Font.FontFamily,$dialog.Font.Size * $scale) }
                    $dialog.Show(); [Windows.Forms.Application]::DoEvents()
                    $checkbox = Field $dialog 'rememberPasswordInput'
                    Assert ($checkbox.Checked -and $dialog.RememberPassword) "$kind must show the saved account preference."
                    Assert ($checkbox.GetType().Name -eq 'NexusCheckBox') "$kind must use the shared dark checkbox."
                    $expected = if ($language -eq 'da') { 'Husk adgangskoden til denne konto' } else { 'Remember password for this account' }
                    Assert ($checkbox.Text -ceq $expected) "$kind password preference must be translated."
                    $fields = Field $dialog 'Fields'; $fields.ScrollControlIntoView($checkbox)
                    [Windows.Forms.Application]::DoEvents()
                    [PasswordPreferenceNative]::Click($checkbox.Handle); [Windows.Forms.Application]::DoEvents()
                    Assert (!$dialog.RememberPassword -and $account.RememberPassword) 'Changing the dialog checkbox must not mutate the original account before confirmation.'
                    $guideName = if ($kind -eq 'AccountDialog') { 'accountPasswordGuide' } else { 'passwordGuide' }
                    $guide = @($fields.Controls | Where-Object Name -eq $guideName)[0]
                    Assert ($null -ne $guide -and $guide.Text.Length -gt 0) "$kind must explain password use, persistence and removal."
                    $measure = [Windows.Forms.TextRenderer]::MeasureText($guide.Text, $guide.Font, [Drawing.Size]::new($guide.ClientSize.Width - $guide.Padding.Horizontal, 10000), [Windows.Forms.TextFormatFlags]'WordBreak,TextBoxControl,NoPrefix')
                    Assert ($guide.Height -ge $measure.Height) "$kind guide must fit English/Danish text at the selected scale."
                    Assert ($checkbox.Width -le $fields.ClientSize.Width) "$kind checkbox label must fit the account panel."
                    if ($kind -eq 'PasswordDialog') {
                        $password = Field $dialog 'passwordInput'
                        Assert ($password.UseSystemPasswordChar) 'Password typing must remain masked.'
                        (Field $dialog 'SaveButton').PerformClick()
                        Assert ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::None) 'An empty password must not submit the preference.'
                    }
                    $fields.ScrollControlIntoView($guide); [Windows.Forms.Application]::DoEvents()
                    $bitmap = [Drawing.Bitmap]::new($dialog.Width,$dialog.Height)
                    try {
                        $graphics = [Drawing.Graphics]::FromImage($bitmap); $dc = $graphics.GetHdc()
                        try { Assert ([PasswordPreferenceNative]::PrintWindow($dialog.Handle,$dc,0)) 'The owned dialog preview must render.' } finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
                        $bitmap.Save((Join-Path ([IO.Path]::GetFullPath($PreviewDirectory)) ($kind + '-' + $language + '-' + [int]($scale*100) + '.png')), [Drawing.Imaging.ImageFormat]::Png)
                    } finally { $bitmap.Dispose() }
                } finally { $dialog.Close(); $dialog.Dispose() }
            }
        }
        $newDialog = [Activator]::CreateInstance($assembly.GetType('AOLauncher.AccountDialog', $true), $flags, $null, [object[]]@($null,(Make 'LauncherSettings')), $null)
        try { Assert (!$newDialog.RememberPassword) 'New accounts must require explicit opt-in to save a password.' } finally { $newDialog.Dispose() }
        $unsaved = Account 'unsaved-dialog-only' $false
        $promptDialog = [Activator]::CreateInstance($assembly.GetType('AOLauncher.PasswordDialog', $true), $flags, $null, [object[]]@($unsaved), $null)
        try { Assert (!$promptDialog.RememberPassword) 'An opted-out account must open its password prompt unchecked.' } finally { $promptDialog.Dispose() }
    }
}
"PASS: $script:checks password-preference checks; isolated synthetic encrypted storage and injected launch policy only. No real profile, credentials, backend, network or game access."
