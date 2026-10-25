param([string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'))
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Use 32-bit Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Web.Extensions
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TrayNative {
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
'@
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$formType = $assembly.GetType('AOLauncher.MainForm')
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$script:checks = 0
function Assert([bool]$Value, [string]$Message) { if (!$Value) { throw $Message }; $script:checks++ }
function Field([string]$Name) { $formType.GetField($Name, $flags).GetValue($script:form) }
function Call([string]$Name) { $formType.GetMethod($Name, $flags).Invoke($script:form, @()) }
function Pump { [Windows.Forms.Application]::DoEvents() }

foreach ($language in @('en', 'da')) {
    $script:form = [Activator]::CreateInstance($formType, [object[]]@($true))
    $cancel = [Threading.CancellationTokenSource]::new()
    try {
        (Field 'settings').Language = $language
        Call 'ApplyLanguage'
        $form.Show()
        Pump
        $handle = $form.Handle
        $icon = Field 'trayIcon'
        $menu = Field 'trayMenu'
        $open = Field 'trayOpenItem'
        $exit = Field 'trayExitItem'
        Assert (!$icon.Visible) 'A visible launcher should not duplicate itself in the tray.'
        Assert ($open.Text -eq $(if ($language -eq 'da') { 'Åbn AO Nexus' } else { 'Open AO Nexus' })) 'Open must use the active language.'
        Assert ($exit.Text -eq $(if ($language -eq 'da') { 'Afslut AO Nexus' } else { 'Exit AO Nexus' })) 'Exit must use the active language.'
        $formType.GetField('preparationCancellation', $flags).SetValue($form, $cancel)
        $formType.GetField('running', $flags).SetValue($form, $true)
        (Field 'passwords').Add('synthetic-account', 'synthetic-password')
        foreach ($state in @('Normal', 'Maximized', 'Normal')) {
            $form.WindowState = [Windows.Forms.FormWindowState]::$state
            Pump
            [TrayNative]::SendMessage($handle, 0x0112, [IntPtr]0xF020, [IntPtr]::Zero) | Out-Null
            Pump
            Assert (!$form.Visible -and ![TrayNative]::IsWindowVisible($handle)) 'Minimize must hide the native window, excluding it from taskbar and Alt+Tab.'
            Assert ($icon.Visible) 'A hidden launcher must remain accessible through its tray icon.'
            Assert ($form.Handle -eq $handle) 'Minimize must preserve the window handle for asynchronous callbacks.'
            Assert (!(Field 'closing') -and !(Field 'stopRequested') -and (Field 'running')) 'Minimize must not run shutdown or stop login.'
            Assert (!$cancel.IsCancellationRequested -and (Field 'passwords').Count -eq 1) 'Minimize must preserve the active login preparation and session password.'
            # A real UI callback still runs while hidden, as backend progress callbacks must.
            $callback = $form.BeginInvoke([Action]{ $script:callbackRan = $true })
            $script:callbackRan = $false
            Pump
            Assert ($callback.IsCompleted -and $script:callbackRan) 'The hidden form must continue processing UI callbacks.'
            # Exercise the real menu event rather than calling the restore helper directly.
            $open.PerformClick()
            Pump
            Assert ($form.Visible -and [TrayNative]::IsWindowVisible($handle) -and !$icon.Visible) 'Open must restore the native window and remove the tray duplicate.'
            Assert ($form.WindowState.ToString() -eq $state) 'Restore must preserve normal/maximized state.'
        }
        $form.WindowState = [Windows.Forms.FormWindowState]::Minimized
        Pump
        # A real menu dismissal must complete before the form disposes the menu.
        $menu.Show([Drawing.Point]::new(10, 10)); Pump
        $point = [IntPtr](($exit.Bounds.Top + 8) * 65536 + $exit.Bounds.Left + 8)
        [TrayNative]::SendMessage($menu.Handle, 0x0201, [IntPtr]1, $point) | Out-Null
        [TrayNative]::SendMessage($menu.Handle, 0x0202, [IntPtr]::Zero, $point) | Out-Null
        Pump
        Assert ($form.IsDisposed -and $menu.IsDisposed -and !$icon.Visible) 'Tray Exit must close the form and dispose its tray resources.'
        Assert ($cancel.IsCancellationRequested -and (Field 'passwords').Count -eq 0) 'Tray Exit must perform the normal shutdown cleanup.'
    }
    finally { $form.Dispose(); $cancel.Dispose() }
}

# Normal Close (the window X / OS close path) must still exit, not hide.
$script:form = [Activator]::CreateInstance($formType, [object[]]@($true))
try {
    $form.Show(); Pump
    $menu = Field 'trayMenu'; $icon = Field 'trayIcon'
    $form.Close(); Pump
    Assert ($form.IsDisposed -and $menu.IsDisposed -and !$icon.Visible) 'Normal close must exit and remove tray resources.'
}
finally { $form.Dispose() }

# Direct disposal is also used by fixture hosts and must not leave a ghost icon.
$script:form = [Activator]::CreateInstance($formType, [object[]]@($true))
try {
    $form.Show(); Pump
    $form.WindowState = [Windows.Forms.FormWindowState]::Minimized; Pump
    $menu = Field 'trayMenu'; $icon = Field 'trayIcon'
    $form.Dispose(); Pump
    Assert ($menu.IsDisposed -and !$icon.Visible) 'Disposing a hidden form must clean up the tray.'
}
finally { $form.Dispose() }

$settingsType = $assembly.GetType('AOLauncher.LauncherSettings')
$dialogType = $assembly.GetType('AOLauncher.SettingsDialog')
foreach ($language in @('en', 'da')) {
    $script:form = [Activator]::CreateInstance($formType, [object[]]@($true))
    (Field 'settings').Language = $language; Call 'ApplyLanguage'
    $settings = [Activator]::CreateInstance($settingsType)
    $dialog = [Activator]::CreateInstance($dialogType, [object[]]@($settings))
    try {
        $dialog.Show(); Pump
        $check = $dialog.Controls.Find('startWithWindows', $true)[0]
        Assert (!$dialog.StartWithWindows) 'Startup must default to opt-in.'
        Assert ($check.Text -eq $(if ($language -eq 'da') { 'Start med Windows' } else { 'Start with Windows' })) 'Startup preference must be translated.'
        $dialog.StartWithWindows = $true
        $script:savedStartup = $false
        $dialog.SaveStartupAction = [Func[bool,string]]{ param($enabled) $script:savedStartup = $enabled; return 'Synthetic failure' }
        $dialog.AcceptButton.PerformClick(); Pump
        Assert ($script:savedStartup -and $dialog.DialogResult -ne [Windows.Forms.DialogResult]::OK) 'A registry failure must keep Settings open.'
        $dialog.SaveStartupAction = [Func[bool,string]]{ param($enabled) $script:savedStartup = $enabled; return $null }
        $dialog.AcceptButton.PerformClick(); Pump
        Assert ($script:savedStartup -and $dialog.DialogResult -eq [Windows.Forms.DialogResult]::OK) 'Save must apply the selected startup preference.'
    }
    finally { $dialog.Dispose(); $form.Dispose() }
    $dialog = [Activator]::CreateInstance($dialogType, [object[]]@($settings))
    try {
        $script:saveCalled = $false
        $dialog.SaveStartupAction = [Func[bool,string]]{ param($enabled) $script:saveCalled = $true; return $null }
        $dialog.Show(); Pump
        $dialog.StartWithWindows = $true
        $dialog.CancelButton.PerformClick(); Pump
        Assert (!$script:saveCalled) 'Cancel must not change automatic startup.'
    }
    finally { $dialog.Dispose() }
}
Write-Host "PASS: $script:checks tray lifecycle checks, synthetic accounts only."
