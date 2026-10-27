param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\logs\guide-captures')
)

# Render the real application controls with in-memory examples only. No saved
# profile, credential store, game, news network request or browser is opened.
# Run with 32-bit Windows PowerShell -STA, like the other WinForms UI fixtures.
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') {
    throw 'Use 32-bit Windows PowerShell -STA.'
}
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
. (Join-Path $PSScriptRoot 'Render-GuideCapture.ps1')
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing @'
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
public static class GuideScreenshotNative {
    [StructLayout(LayoutKind.Sequential)] private struct Msg { public IntPtr hwnd; public uint message; public IntPtr wParam,lParam; public uint time; public Point point; }
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg msg,IntPtr hwnd,uint min,uint max,uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Msg msg);
    public static void Pump() {
        var timer=Stopwatch.StartNew(); Msg msg; int quiet=0;
        while(timer.ElapsedMilliseconds<1800 && quiet<3) {
            if(PeekMessage(out msg,IntPtr.Zero,0,0,1)) { quiet=0; TranslateMessage(ref msg); DispatchMessage(ref msg); }
            else { quiet++; System.Threading.Thread.Sleep(1); }
        }
        if(quiet==0 && timer.ElapsedMilliseconds>=1800) throw new Exception("Screenshot layout did not settle.");
    }
}
'@
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$staticFlags = [Reflection.BindingFlags]'Public,NonPublic,Static'
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null

function Field($Control, [string]$Name) {
    $type = $Control.GetType()
    while ($type) {
        $field = $type.GetField($Name, $flags)
        if ($field) { return $field.GetValue($Control) }
        $type = $type.BaseType
    }
    throw "Field not found: $Name"
}
function New-Model([string]$Name) {
    [Activator]::CreateInstance($assembly.GetType('AOLauncher.' + $Name, $true))
}
function New-Dialog([string]$Name, [object[]]$Arguments) {
    [Activator]::CreateInstance($assembly.GetType('AOLauncher.' + $Name, $true), $flags, $null, $Arguments, $null)
}
function Show-Fixture($Dialog) {
    $Dialog.ShowInTaskbar = $false
    $Dialog.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $Dialog.Location = [Drawing.Point]::new(-32000, -32000)
    $Dialog.Show()
    $Dialog.PerformLayout()
    [GuideScreenshotNative]::Pump()
}
function Save-GuideScreenshot($Dialog, [string]$Name) {
    $Dialog.PerformLayout()
    [GuideScreenshotNative]::Pump()
    $bitmap = [Drawing.Bitmap]::new($Dialog.ClientSize.Width, $Dialog.ClientSize.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try {
        if (![GuideScreenshotNative]::PrintWindow($Dialog.Handle, $dc, 1)) {
            throw "Could not render screenshot: $Name"
        }
    } finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    try { $bitmap.Save((Join-Path $OutputDirectory $Name), [Drawing.Imaging.ImageFormat]::Png) }
    finally { $bitmap.Dispose() }
    Export-GuideCapture $Dialog $Name (Join-Path $OutputDirectory 'curated')
    Write-Output "Saved $Name"
}

foreach ($registration in @('GuiStrings', 'NexusStrings')) {
    $assembly.GetType('AOLauncher.' + $registration, $true).GetMethod('Register', $staticFlags).Invoke($null, @()) | Out-Null
}
$assembly.GetType('AOLauncher.Localization.Texts', $true).GetMethod('SetLanguage', $staticFlags).Invoke($null, [object[]]@('en')) | Out-Null

$setupSettings = New-Model 'LauncherSettings'; $setupSettings.Language = 'en'
$setup = New-Dialog 'SetupWizard' ([object[]]@($setupSettings,$false,$true))
try {
    Show-Fixture $setup
    $setup.Controls.Find('setupNext',$true)[0].PerformClick()
    $setup.Controls.Find('setupNext',$true)[0].PerformClick()
    Save-GuideScreenshot $setup 'setup-preferences.png'
} finally { $setup.Dispose(); [GuideScreenshotNative]::Pump() }
$dashboard = New-Dialog 'MainForm' ([object[]]@($true))
try {
    if (!(Field $dashboard 'smoke') -or !(Field (Field $dashboard 'newsPanel') 'offlinePreview')) {
        throw 'Guide screenshots require the safe offline preview fixture.'
    }
    $dashboard.ClientSize = [Drawing.Size]::new(1280, 1000)
    $exampleAccounts=(Field $dashboard 'settings').Accounts
    $exampleAccounts[0].Label='Main account'
    $exampleAccounts[1].Label='Weekend account'
    $exampleAccounts[2].Label='Ithaca account'
    $dashboard.GetType().GetMethod('RefreshTree',$flags).Invoke($dashboard,[object[]]@($null)) | Out-Null
    Show-Fixture $dashboard
    $preset = (Field $dashboard 'settings').Presets[0]
    (Field $dashboard 'settings').DefaultPresetId=$preset.Id
    $dashboard.GetType().GetMethod('RefreshPresetPicker',$flags).Invoke($dashboard,@()) | Out-Null
    $preset.StartHotkey = New-Model 'HotkeyBinding'; $preset.StartHotkey.Key = 117; $preset.StartHotkey.Modifiers = 3
    $preset.CampHotkey = New-Model 'HotkeyBinding'; $preset.CampHotkey.Key = 118; $preset.CampHotkey.Modifiers = 3
    $tree = Field $dashboard 'tree'
    $tree.SelectedNode = $tree.Nodes[0].Nodes[0]
    Save-GuideScreenshot $dashboard 'launcher-overview.png'
    (Field $dashboard 'characterSearch').Text='Aster'
    Save-GuideScreenshot $dashboard 'character-search.png'
    (Field $dashboard 'characterSearch').Clear()
    $hero = Field $dashboard 'header'
    $hero.GetType().GetMethod('BeginActivity',$flags).Invoke($hero,[object[]]@([int]3)) | Out-Null
    $hero.GetType().GetMethod('ReportActivity',$flags).Invoke($hero,[object[]]@([int]1)) | Out-Null
    Save-GuideScreenshot $dashboard 'launch-progress.png'
    $hero.GetType().GetMethod('EndActivity',$flags).Invoke($hero,@()) | Out-Null
    $sidebar = Field $dashboard 'serverSidebar'
    (Field $dashboard 'settings').HealthServerIds.Clear()
    (Field $dashboard 'settings').HealthServerIds.Add('rebirth')
    $dashboard.GetType().GetMethod('ConfigureSidebar',$flags).Invoke($dashboard,@()) | Out-Null
    $tabs = $sidebar.GetType().GetProperty('Tabs',$flags).GetValue($sidebar)
    $tabs.SelectedIndex=1
    Save-GuideScreenshot $dashboard 'server-health.png'
    $tabs.SelectedIndex=0
    foreach ($editing in @($true, $false)) {
        $title = if ($editing) { 'Edit team preset' } else { 'Create team preset' }
        $editor = New-Dialog 'PresetNameDialog' ([object[]]@($title, $preset.Name, [string[]]@(), $preset.StartHotkey, $preset.CampHotkey, 3, $editing))
        try {
            $editor.UseAsStartupDefault=$editing
            Show-Fixture $editor
            $name = if ($editing) { 'preset-shortcuts.png' } else { 'preset-create.png' }
            Save-GuideScreenshot $editor $name
        } finally { $editor.Dispose() }
    }
    $viewer = New-Dialog 'LogViewer' ([object[]]@())
    try {
        $viewer.ClientSize = [Drawing.Size]::new(900,340)
        $sampleLog = "[10:15:00] Ready. Choose a team preset to begin.`r`n[10:15:12] Example account: login started.`r`n[10:15:17] Aster: logged in.`r`n[10:15:22] Orion: logged in.`r`n[10:15:27] Vesper: logged in.`r`n[10:15:27] Done. Game windows remain open."
        $viewer.GetType().GetMethod('SetLogText', $flags).Invoke($viewer, [object[]]@($sampleLog)) | Out-Null
        Show-Fixture $viewer
        (Field $viewer 'search').Text = 'logged in'
        $viewer.GetType().GetMethod('RefreshLog', $flags).Invoke($viewer, @()) | Out-Null
        Save-GuideScreenshot $viewer 'activity-log.png'
    } finally { $viewer.Dispose() }
} finally { $dashboard.Dispose(); [GuideScreenshotNative]::Pump() }

$settings = New-Model 'LauncherSettings'
$settingsPreview = New-Dialog 'SettingsDialog' ([object[]]@($settings))
try {
    $settingsPreview.ClientSize = [Drawing.Size]::new(640, 580)
    Show-Fixture $settingsPreview
    Save-GuideScreenshot $settingsPreview 'settings.png'
    foreach ($entry in @(@(1,'settings-accounts.png'),@(2,'settings-sidebar.png'),@(3,'settings-updates.png'))) {
        (Field $settingsPreview 'settingsTabs').SelectedIndex=$entry[0]
        Save-GuideScreenshot $settingsPreview $entry[1]
    }
} finally { $settingsPreview.Dispose() }
$privacyPreview = New-Dialog 'PrivacyDialog' ([object[]]@())
try { Show-Fixture $privacyPreview; Save-GuideScreenshot $privacyPreview 'privacy-security.png' } finally { $privacyPreview.Dispose() }
$releasePreview = New-Model 'UpdateRelease'
$releasePreview.GetType().GetField('Version', $flags).SetValue($releasePreview, $assembly.GetName().Version)
$updatesPreview = New-Dialog 'UpdateDialog' ([object[]]@([Func[bool]]{ return $false }, $releasePreview))
try {
    Show-Fixture $updatesPreview
    Save-GuideScreenshot $updatesPreview 'app-updates.png'
} finally { $updatesPreview.Dispose() }
$currentVersion=$assembly.GetName().Version
$releasePreview.GetType().GetField('Version', $flags).SetValue($releasePreview, [version]::new($currentVersion.Major,$currentVersion.Minor,$currentVersion.Build+1,0))
$releasePreview.GetType().GetField('Notes', $flags).SetValue($releasePreview, "# Example release notes`n`n- **Clear headings** and readable spacing.`n- Review the changes before installing.`n`nThis screenshot uses an offline fixture, not a published update.")
$updatesPreview = New-Dialog 'UpdateDialog' ([object[]]@([Func[bool]]{ return $false }, $releasePreview))
try { Show-Fixture $updatesPreview; Save-GuideScreenshot $updatesPreview 'update-available.png' } finally { $updatesPreview.Dispose() }
foreach ($minimize in @($true,$false)) {
    $confirmation = New-Dialog 'WindowActionDialog' ([object[]]@($minimize))
    try {
        Show-Fixture $confirmation
        $filename = if ($minimize) { 'minimize-confirmation.png' } else { 'quit-confirmation.png' }
        Save-GuideScreenshot $confirmation $filename
    } finally { $confirmation.Dispose() }
}
$loginConfirmation = New-Dialog 'LaunchConfirmationDialog' ([object[]]@("Log in 3 characters across 1 account?`r`nAO Rebirth - Live: Main account"))
try {
    $loginConfirmation.ClientSize=[Drawing.Size]::new(620,340)
    Show-Fixture $loginConfirmation
    $loginConfirmation.Controls.Find('dontShowAgain',$true)[0].Checked=$true
    Save-GuideScreenshot $loginConfirmation 'login-confirmation.png'
} finally { $loginConfirmation.Dispose() }
$monitorRows=[Array]::CreateInstance($assembly.GetType('AOLauncher.ClientObservation'),3)
foreach($index in 0..2){
    $row=New-Dialog 'ClientObservation' ([object[]]@())
    $type=$row.GetType()
    $type.GetField('Name',$flags).SetValue($row,@('Aster','Orion','Vesper')[$index])
    $type.GetField('Pid',$flags).SetValue($row,[int](100+$index))
    $type.GetField('StartTicks',$flags).SetValue($row,[long](1000+$index))
    $type.GetField('Measured',$flags).SetValue($row,($index -ne 2))
    $type.GetField('Condition',$flags).SetValue($row,[Enum]::Parse($assembly.GetType('AOLauncher.ClientCondition'),@('Healthy','Delayed','Unmeasured')[$index]))
    if($index -eq 0){
        $type.GetField('CastWhileFalling',$flags).SetValue($row,$true)
        $type.GetField('CastIssueConfirmed',$flags).SetValue($row,$true)
        $type.GetField('CastIssueCount',$flags).SetValue($row,[int]4)
    }
    $monitorRows.SetValue($row,$index)
}
$rowFactory=[scriptblock]{return ,$monitorRows} -as ([Func``1].MakeGenericType($monitorRows.GetType()))
$monitorDialog=New-Dialog 'ClientStatusDialog' ([object[]]@([Func[string]]{return 'Offline monitoring example'},[Func[string]]{return '{}'},$rowFactory))
try {
    $monitorDialog.ClientSize=[Drawing.Size]::new(720,640)
    Show-Fixture $monitorDialog
    Save-GuideScreenshot $monitorDialog 'character-status.png'
} finally { $monitorDialog.Dispose() }
$account = New-Model 'AccountConfig'
$whatsNew = New-Dialog 'WhatsNewDialog' ([object[]]@())
try {
    Show-Fixture $whatsNew
    Save-GuideScreenshot $whatsNew 'whats-new.png'
} finally { $whatsNew.Dispose() }
$account.Label = 'Main account'
$account.Username = 'example-account'
$account.GamePath = 'C:\Games\AO-Rebirth'
$account.ServerProfileId = 'rebirth'
$account.PrefsPath = 'C:\ExamplePrefs\AO-Rebirth\Prefs\example-account'
$account.RememberPassword = $true
$profile = @($settings.LoginServers | Where-Object Id -eq 'rebirth')[0]
$account.Server = $profile.Server
$account.Port = $profile.Port

$dialog = New-Dialog 'AccountDialog' ([object[]]@($account, $settings))
try {
    Show-Fixture $dialog
    Save-GuideScreenshot $dialog 'account-setup.png'
    (Field $dialog 'accountTabs').SelectedIndex = 1

    Save-GuideScreenshot $dialog 'game-and-server.png'
} finally { $dialog.Dispose(); [GuideScreenshotNative]::Pump() }

$arguments = New-Object object[] 1
$arguments[0] = $settings.LoginServers
$dialog = New-Dialog 'LoginServersDialog' $arguments
try {
    Show-Fixture $dialog
    (Field $dialog 'grid').ClearSelection()
    Save-GuideScreenshot $dialog 'server-settings.png'
} finally { $dialog.Dispose(); [GuideScreenshotNative]::Pump() }

$dialog = New-Dialog 'HealthEndpointsDialog' ([object[]]@($settings.LoginServers,$settings.HealthCheckEndpoints))
try { Show-Fixture $dialog; (Field $dialog 'grid').ClearSelection(); Save-GuideScreenshot $dialog 'health-endpoints.png' }
finally { $dialog.Dispose(); [GuideScreenshotNative]::Pump() }

foreach ($fixture in @(
    @{ Type = 'PasswordDialog'; File = 'password-options.png' },
    @{ Type = 'SavedPasswordDialog'; File = 'update-saved-password.png' }
)) {
    $dialog = New-Dialog $fixture.Type ([object[]]@($account))
    try {
        Show-Fixture $dialog
        Save-GuideScreenshot $dialog $fixture.File
    } finally { $dialog.Dispose(); [GuideScreenshotNative]::Pump() }
}
$guiCaptureHost = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
& $guiCaptureHost -NoProfile -ExecutionPolicy Bypass -STA -File (Join-Path $PSScriptRoot 'Verify-GameGuiUsability.ps1') -AssemblyPath $AssemblyPath
if ($LASTEXITCODE -ne 0) { throw 'Game GUI screenshot verification failed.' }
$version = $assembly.GetName().Version.ToString()
& (Join-Path $PSScriptRoot 'Publish-GuideImages.ps1') -CaptureDirectory $OutputDirectory -Version $version
Write-Output "PASS: $($GuideImagePlan.Count) distinct guide images captured from $version with example accounts and offline fixtures."
