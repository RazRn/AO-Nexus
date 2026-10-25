param([string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'))
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Use 32-bit Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Web.Extensions
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HotkeyNativeFixture {
    [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window, int id);
}
'@
[Windows.Forms.Application]::EnableVisualStyles()
$a = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$private = [Reflection.BindingFlags]'NonPublic,Instance'
$statics = [Reflection.BindingFlags]'NonPublic,Static'
$checks = 0
function Assert($v, $m) { if (!$v) { throw $m }; $script:checks++ }
function NewObject($name) { [Activator]::CreateInstance($a.GetType('AOLauncher.' + $name), $true) }
function Binding($key, $mod) { $b = NewObject 'HotkeyBinding'; $b.Key = $key; $b.Modifiers = $mod; return $b }
function Valid($b) { $b.GetType().GetProperty('IsValid', $private).GetValue($b) }
function Field($name) { $form.GetType().GetField($name, $private).GetValue($form) }
function Call($name, $argsList = @()) { $form.GetType().GetMethod($name, $private).Invoke($form, $argsList) }
Assert (Valid (Binding 65 3)) 'Ctrl Alt A should be valid'
Assert (!(Valid (Binding 65 0))) 'Bare letter must be rejected'
Assert (!(Valid (Binding 65 4))) 'Shift letter must be rejected'
Assert (!(Valid (Binding 123 2))) 'F12 is reserved'
Assert (!(Valid (Binding 115 1))) 'Alt F4 must be rejected'
Assert (!(Valid (Binding 65 8))) 'Windows modifier must be rejected'
Assert ((Binding 117 7).ToString() -eq 'Ctrl + Alt + Shift + F6') 'Stable readable shortcut'
$form = [Activator]::CreateInstance($a.GetType('AOLauncher.MainForm'), [object[]]@($true))
try {
    $form.Show(); [Windows.Forms.Application]::DoEvents()
    $settings = Field 'settings'; $preset = $settings.Presets[0]
    Assert ($null -eq $preset.StartHotkey -and $null -eq $preset.CampHotkey) 'Old presets must start unbound'
    $start = Binding 117 3; $camp = Binding 118 3
    Assert ($null -eq (Call 'ValidatePresetHotkeys' @($preset, $start, $null))) 'Start binding should save'
    Assert ($null -ne (Call 'ValidatePresetHotkeys' @($preset, $start, $start))) 'Start and camp cannot share a shortcut'
    Assert ($null -eq (Call 'ValidatePresetHotkeys' @($preset, $start, $camp))) 'Distinct logout shortcut should save'
    $preset.StartHotkey = $start; $preset.CampHotkey = $camp
    $other = NewObject 'LaunchPreset'; $other.Name = 'Second preset'; $settings.Presets.Add($other)
    $settings.SelectedPresetId = $other.Id
    Assert ($null -ne (Call 'ValidatePresetHotkeys' @($other, $camp, $null))) 'Cross-preset shortcut conflict'
    $settings.SelectedPresetId = $preset.Id
    $serializer = New-Object Web.Script.Serialization.JavaScriptSerializer
    $json = $serializer.Serialize($settings)
    $copy = $serializer.Deserialize($json, $settings.GetType())
    Assert ($copy.Presets[0].StartHotkey.Key -eq 117 -and $copy.Presets[0].CampHotkey.Key -eq 118) 'Both shortcuts survive JSON round trip'
    $service = $a.GetType('AOLauncher.PresetService')
    $service.GetMethod('Update', $statics).Invoke($null, @($settings, $preset))
    Assert ($preset.StartHotkey.Key -eq 117 -and $preset.CampHotkey.Key -eq 118) 'Updating members must retain shortcuts'
    Call 'RefreshPresetPicker'
    $dialog = [Activator]::CreateInstance($a.GetType('AOLauncher.PresetNameDialog'), [object[]]@('Edit team preset', $preset.Name, [string[]]@('Reserved'), $start, $camp, 3, $true))
    try {
        $dialog.Show(); [Windows.Forms.Application]::DoEvents()
        $field = $dialog.GetType().GetField('startHotkeyField', $private).GetValue($dialog)
        $field.PerformClick()
        Assert ($field.GetType().GetProperty('Capturing', $private).GetValue($field)) 'Click must enter capture in the editor'
        $message = [Windows.Forms.Message]::Create($field.Handle, 0x100, [IntPtr]0, [IntPtr]0)
        $method = $field.GetType().GetMethod('ProcessCmdKey', $private)
        $method.Invoke($field, @($message, [Windows.Forms.Keys]::Escape)) | Out-Null
        Assert ($dialog.StartHotkey.Key -eq 117) 'Escape must preserve the draft shortcut'
        $field.PerformClick()
        $method.Invoke($field, @($message, ([Windows.Forms.Keys]::Control -bor [Windows.Forms.Keys]::Alt -bor [Windows.Forms.Keys]::F9))) | Out-Null
        Assert ($dialog.StartHotkey.Key -eq 120 -and $preset.StartHotkey.Key -eq 117) 'Capture changes only the draft'
        $field.PerformClick()
        $method.Invoke($field, @($message, [Windows.Forms.Keys]::Delete)) | Out-Null
        Assert ($null -eq $dialog.StartHotkey -and $dialog.LogoutHotkey.Key -eq 118) 'Delete clears only the chosen draft shortcut'
        Assert ($dialog.Controls.Find('replacePresetCharacters',$true).Count -eq 0) 'Membership is updated outside the shortcut editor'
        $dialog.DialogResult = [Windows.Forms.DialogResult]::Cancel
    } finally { $dialog.Dispose() }
    Assert ($preset.StartHotkey.Key -eq 117 -and $preset.CampHotkey.Key -eq 118) 'Cancel leaves both saved shortcuts unchanged'
    # Only registration is enabled on this synthetic form. Never invoke a launch action or save a real profile.
    $preset.StartHotkey = Binding 133 7; $preset.CampHotkey = Binding 134 7
    $form.GetType().GetField('smoke', $private).SetValue($form, $false)
    try {
        Call 'RegisterPresetHotkeys'
        Assert ((Field 'registeredHotkeys').Count -eq 2) 'Register both shortcuts with Windows'
        Call 'MinimizeToTray'; [Windows.Forms.Application]::DoEvents()
        Assert (!( [HotkeyNativeFixture]::RegisterHotKey([IntPtr]::Zero, 808, 7, 133))) 'Shortcut stays registered while hidden in tray'
        Call 'ShowLauncher'
        $other.StartHotkey = Binding 133 7
        Call 'RegisterPresetHotkeys'
        Assert ((Field 'hotkeyFailures').ContainsKey($other.Id)) 'Windows registration conflicts are reported'
        $other.StartHotkey = $null
    } finally {
        Call 'UnregisterPresetHotkeys'
        $form.GetType().GetField('smoke', $private).SetValue($form, $true)
    }
    Assert ([HotkeyNativeFixture]::RegisterHotKey([IntPtr]::Zero, 808, 7, 133)) 'Unregister releases the combination'
    [void][HotkeyNativeFixture]::UnregisterHotKey([IntPtr]::Zero, 808)
    $preset.StartHotkey = Binding 117 3; $preset.CampHotkey = Binding 118 3
    foreach ($language in @('en', 'da')) {
        $settings.Language = $language; Call 'ApplyLanguage'; [Windows.Forms.Application]::DoEvents()
        $section = $form.Controls.Find('presetSection', $true)[0]
        Assert ($section.Controls.Find('presetStartHotkey', $true).Count -eq 0) 'Main window must not expose editable shortcut fields'
        Assert ($section.Height -lt 130) 'Preset section must stay compact'
        Assert ($section.Padding.Left -eq 12 -and $section.Margin.Bottom -ge 10) 'Preset section has inset controls and separation from account actions'
        $picker = Field 'presetPicker'; $update = Field 'updatePresetButton'
        Assert ($picker.Height -eq $update.Height -and $picker.Top -eq $update.Top) ('Inline dropdown and buttons align: ' + $picker.Bounds + ' / ' + $update.Bounds)
        $header = $form.Controls.Find('accountsHeader', $true)[0]
        Assert ($header.GetPositionFromControl((Field 'expandAccountsLink')).Row -eq 0) 'Section actions share the heading row'
        if ($language -eq 'en') {
            $out = Join-Path $PSScriptRoot '../logs/preset-shortcuts-preview.png'
            $bitmap = [Drawing.Bitmap]::new($form.Width, $form.Height)
            try { $form.DrawToBitmap($bitmap, [Drawing.Rectangle]::new(0,0,$form.Width,$form.Height)); $bitmap.Save($out, [Drawing.Imaging.ImageFormat]::Png) }
            finally { $bitmap.Dispose() }
            $panel = $section
            $bitmap = [Drawing.Bitmap]::new($panel.Width, $panel.Height)
            try { $panel.DrawToBitmap($bitmap, [Drawing.Rectangle]::new(0,0,$panel.Width,$panel.Height)); $bitmap.Save((Join-Path $PSScriptRoot '../logs/preset-shortcuts.png'), [Drawing.Imaging.ImageFormat]::Png) }
            finally { $bitmap.Dispose() }
        }
    }
    $camping = $a.GetType('AOLauncher.PresetCamping')
    $client = NewObject 'PresetCamping+Client'; $account = $settings.Accounts[0]
    $client.AccountIdentity = 'id:' + $account.Id; $client.Username = $account.Username
    $client.CharacterId = $account.Characters[0].CharacterId; $client.CharacterName = 'Fixture'
    $client.GamePath = $account.GamePath; $client.Server = $account.Server; $client.Port = $account.Port
    $typed = [Array]::CreateInstance($client.GetType(), 1); $typed[0] = $client
    function Matches { @($camping.GetMethod('Match', $statics).Invoke($null, @($settings, $preset, $typed))) }
    Assert ((Matches).Count -eq 1) 'Exact preset identity matches'
    $client.Port++; Assert ((Matches).Count -eq 0) 'Wrong server port rejected'; $client.Port--
    $client.AccountIdentity = 'id:wrong'; Assert ((Matches).Count -eq 0) 'Wrong account rejected'; $client.AccountIdentity = 'id:' + $account.Id
    $client.Pending = $true; Assert ((Matches).Count -eq 0) 'Pending client rejected'; $client.Pending = $false
    $client.CreateCharacter = $true; Assert ((Matches).Count -eq 0) 'Creation client rejected'; $client.CreateCharacter = $false
    $client.GamePath = 'C:\wrong'; Assert ((Matches).Count -eq 0) 'Wrong game folder rejected'
    Assert (!$camping.GetMethod('IsSameProcess', $statics).Invoke($null, @($client))) 'Missing process must fail closed'
    Assert (!$camping.GetMethod('ValidToken', $statics).Invoke($null, @('untrusted/name'))) 'Invalid event token rejected'
} finally { $form.Dispose() }
Write-Host "PASS: $checks preset hotkey, localization, persistence and logout identity checks. No real games touched."
