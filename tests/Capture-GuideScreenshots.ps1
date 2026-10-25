param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\docs\images')
)

# Render the real application controls with in-memory examples only. No saved
# profile, credential store, game, news network request or browser is opened.
# Run with 32-bit Windows PowerShell -STA, like the other WinForms UI fixtures.
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') {
    throw 'Use 32-bit Windows PowerShell -STA.'
}
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
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
    Write-Output "Saved $Name"
}

foreach ($registration in @('GuiStrings', 'NexusStrings')) {
    $assembly.GetType('AOLauncher.' + $registration, $true).GetMethod('Register', $staticFlags).Invoke($null, @()) | Out-Null
}
$assembly.GetType('AOLauncher.Localization.Texts', $true).GetMethod('SetLanguage', $staticFlags).Invoke($null, [object[]]@('en')) | Out-Null

$dashboard = New-Dialog 'MainForm' ([object[]]@($true))
try {
    if (!(Field $dashboard 'smoke') -or !(Field (Field $dashboard 'newsPanel') 'offlinePreview')) {
        throw 'Guide screenshots require the safe offline preview fixture.'
    }
    $dashboard.ClientSize = [Drawing.Size]::new(1280, 1000)
    Show-Fixture $dashboard
    $preset = (Field $dashboard 'settings').Presets[0]
    $preset.StartHotkey = New-Model 'HotkeyBinding'; $preset.StartHotkey.Key = 117; $preset.StartHotkey.Modifiers = 3
    $preset.CampHotkey = New-Model 'HotkeyBinding'; $preset.CampHotkey.Key = 118; $preset.CampHotkey.Modifiers = 3
    $tree = Field $dashboard 'tree'
    $tree.SelectedNode = $tree.Nodes[0].Nodes[0]
    Save-GuideScreenshot $dashboard 'launcher-overview.png'
    foreach ($editing in @($true, $false)) {
        $title = if ($editing) { 'Edit team preset' } else { 'Create team preset' }
        $editor = New-Dialog 'PresetNameDialog' ([object[]]@($title, $preset.Name, [string[]]@(), $preset.StartHotkey, $preset.CampHotkey, 3, $editing))
        try {
            Show-Fixture $editor
            $name = if ($editing) { 'preset-shortcuts.png' } else { 'preset-create.png' }
            Save-GuideScreenshot $editor $name
        } finally { $editor.Dispose() }
    }
    $viewer = New-Dialog 'LogViewer' ([object[]]@())
    try {
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
    $settingsPreview.ClientSize = [Drawing.Size]::new(510, 1100)
    Show-Fixture $settingsPreview
    Save-GuideScreenshot $settingsPreview 'settings.png'
} finally { $settingsPreview.Dispose() }
$releasePreview = New-Model 'UpdateRelease'
$releasePreview.GetType().GetField('Version', $flags).SetValue($releasePreview, $assembly.GetName().Version)
$updatesPreview = New-Dialog 'UpdateDialog' ([object[]]@([Func[bool]]{ return $false }, $releasePreview))
try {
    Show-Fixture $updatesPreview
    Save-GuideScreenshot $updatesPreview 'app-updates.png'
} finally { $updatesPreview.Dispose() }
$account = New-Model 'AccountConfig'
$account.Label = 'Rubi-Ka expedition'
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
    # Expand the guide capture to expose the full scrollable Account and Prefs
    # page. Production controls and contents are unchanged; users can scroll
    # this same page in the normal dialog.
    $dialog.ClientSize = [Drawing.Size]::new(510, 850)
    Show-Fixture $dialog
    Save-GuideScreenshot $dialog 'account-setup.png'
    (Field $dialog 'accountTabs').SelectedIndex = 1
    $dialog.ClientSize = [Drawing.Size]::new(510, 620)
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
$version = $assembly.GetName().Version.ToString()
$readmePath = Join-Path $PSScriptRoot '..\README.md'
$readme = [IO.File]::ReadAllText($readmePath)
$manifest = @("# Guide screenshot provenance", "", "Generated from AO Nexus $version using Capture-GuideScreenshots.ps1.", "", "Synthetic accounts, empty password fields, offline news and an offline update fixture. All eleven README images are rendered from the actual controls.", "")
foreach ($name in @('launcher-overview.png','preset-shortcuts.png','preset-create.png','activity-log.png','settings.png','app-updates.png','account-setup.png','game-and-server.png','server-settings.png','password-options.png','update-saved-password.png')) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest += ('- ' + $name + ': ' + $hash)
    # A changed image must have a new URL, even when its filename stays the same.
    # Otherwise a cached screenshot can outlive the README text describing it.
    $pattern = [regex]::Escape('docs/images/' + $name) + '(?:\?v=[a-f0-9]+)?(?=\))'
    if (-not [regex]::IsMatch($readme, $pattern)) { throw "Missing README image reference: $name" }
    $readme = [regex]::Replace($readme, $pattern, ('docs/images/' + $name + '?v=' + $hash))
}
[IO.File]::WriteAllText($readmePath, $readme, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SCREENSHOTS.md'), (($manifest -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
Write-Output "PASS: all eleven guide screenshots regenerated from $version using synthetic accounts and offline fixtures."
