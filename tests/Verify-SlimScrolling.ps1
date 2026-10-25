param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\src\Launcher\bin\Release\AONexus.exe'),
    [string]$PreviewDirectory = (Join-Path $PSScriptRoot '..\logs\slim-previews'),
    [switch]$SettingsProbe
)
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Use 32-bit Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SlimTestNative {
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    public static void Wheel(IntPtr window, int delta) { SendMessage(window, 0x020A, new IntPtr(delta << 16), IntPtr.Zero); }
    public static void Key(IntPtr window, int key) { SendMessage(window, 0x0100, new IntPtr(key), IntPtr.Zero); SendMessage(window, 0x0101, new IntPtr(key), IntPtr.Zero); }
    public static int FirstLine(IntPtr window) { return SendMessage(window, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32(); }
}
'@
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$allFlags = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
$instanceFlags = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$scrollType = $assembly.GetType('AOLauncher.SlimScrolling', $true)
$themeType = $assembly.GetType('AOLauncher.Theme', $true)
$surface = $themeType.GetField('Surface').GetValue($null)
$foreground = $themeType.GetField('Foreground').GetValue($null)
$script:checks = 0
function Assert([bool]$Value, [string]$Message) { if (!$Value) { throw "FAIL: $Message" }; $script:checks++ }
function Property($Object, [string]$Name) { $Object.GetType().GetProperty($Name, $instanceFlags).GetValue($Object, $null) }
function Invoke-Instance($Object, [string]$Name, [object[]]$Arguments = @()) { $Object.GetType().GetMethod($Name, $instanceFlags).Invoke($Object, $Arguments) }
function Field($Object, [string]$Name) { $Object.GetType().GetField($Name, $instanceFlags).GetValue($Object) }
function Wrap($Control) { $scrollType.GetMethod('Wrap', $allFlags).Invoke($null, [object[]]@($Control)) }
function Sync($Control) { $scrollType.GetMethod('Refresh', $allFlags).Invoke($null, [object[]]@($Control)) | Out-Null; [Windows.Forms.Application]::DoEvents() }
function Descendants($Control) { foreach ($child in $Control.Controls) { $child; Descendants $child } }
function Layout($Control) { $Control.PerformLayout(); foreach ($child in $Control.Controls) { $child.PerformLayout() }; [Windows.Forms.Application]::DoEvents() }
function Show-Offscreen($Control) {
    $Control.ShowInTaskbar = $false
    $Control.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $Control.Location = [Drawing.Point]::new(-32000,-32000)
    $Control.Show(); Layout $Control
}
function Preview($Control, [string]$Name) {
    if (!$PreviewDirectory) { return }
    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PreviewDirectory)) | Out-Null
    $bitmap = [Drawing.Bitmap]::new($Control.Width, $Control.Height)
    try {
        # Print only this synthetic form. DrawToBitmap paints nested native child windows
        # outside their parent clipping rectangles and therefore fabricates pale gutters.
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $dc = $graphics.GetHdc()
        try { Assert ([SlimTestNative]::PrintWindow($Control.Handle,$dc,0)) 'Native rendering of the owned synthetic fixture must succeed.' }
        finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        $bitmap.Save((Join-Path $PreviewDirectory $Name), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
}
function Native-No-Gutter($Form, $Hosts) {
    $bitmap = [Drawing.Bitmap]::new($Form.ClientSize.Width,$Form.ClientSize.Height)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap); $dc = $graphics.GetHdc()
        try { Assert ([SlimTestNative]::PrintWindow($Form.Handle,$dc,1)) 'Native client rendering of the synthetic dashboard must succeed.' }
        finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        foreach ($hostControl in $Hosts) {
            $origin = $Form.PointToClient($hostControl.PointToScreen([Drawing.Point]::Empty))
            $bright = 0
            # Check both the custom rail and the space immediately outside its host.
            # A native scrollbar leaking past the viewport would leave a white strip.
            for ($y=$origin.Y+3; $y -lt $origin.Y+$hostControl.Height-3; $y++) {
                for ($x=$origin.X+$hostControl.Width-2; $x -lt [Math]::Min($bitmap.Width,$origin.X+$hostControl.Width+7); $x++) {
                    $pixel = $bitmap.GetPixel($x,$y)
                    if ($pixel.R -gt 155 -and $pixel.G -gt 155 -and $pixel.B -gt 155) { $bright++ }
                }
            }
            Assert ($bright -eq 0) ($hostControl.Name + ' must have no native pale gutter inside or beyond its viewport in native rendering.')
        }
    } finally { $bitmap.Dispose() }
}
function Dark-Rail($HostControl, [string]$Description) {
    $bitmap = [Drawing.Bitmap]::new($HostControl.Width, $HostControl.Height)
    try {
        $HostControl.DrawToBitmap($bitmap, [Drawing.Rectangle]::new(0,0,$HostControl.Width,$HostControl.Height))
        $bright = 0
        # The far right edge is the rail/background, never content text. Native Windows tracks are pale here.
        for ($y = 3; $y -lt $bitmap.Height - 3; $y++) {
            for ($x = $bitmap.Width - 2; $x -lt $bitmap.Width; $x++) {
                $pixel = $bitmap.GetPixel($x,$y)
                if ($pixel.R -gt 155 -and $pixel.G -gt 155 -and $pixel.B -gt 155) { $bright++ }
            }
        }
        if ($bright -gt 0 -and $PreviewDirectory) {
            [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PreviewDirectory)) | Out-Null
            $bitmap.Save((Join-Path $PreviewDirectory ('failed-rail-' + ($Description -replace '[^a-zA-Z0-9]', '-') + '.png')), [Drawing.Imaging.ImageFormat]::Png)
        }
        Assert ($bright -eq 0) "$Description must have no white native scrollbar track at its visible right edge."
    } finally { $bitmap.Dispose() }
}
function Scroll-To($Bar, [int]$Value) { Invoke-Instance $Bar 'ScrollTo' ([object[]]@($Value)) | Out-Null; [Windows.Forms.Application]::DoEvents() }
function Mouse($Bar, [string]$Method, [int]$X, [int]$Y) {
    $args = [Windows.Forms.MouseEventArgs]::new([Windows.Forms.MouseButtons]::Left, 1, $X, $Y, 0)
    Invoke-Instance $Bar $Method ([object[]]@($args)) | Out-Null
    [Windows.Forms.Application]::DoEvents()
}

if ($SettingsProbe) {
    # This branch runs in an isolated, timed child process. A single DoEvents can
    # otherwise never return when a callback continually enqueues another layout.
    $main = [Activator]::CreateInstance($assembly.GetType('AOLauncher.MainForm', $true), [object[]]@($true))
    $dialog = $null
    try {
        $config = Field $main 'settings'
        $config.Language = 'en'
        Invoke-Instance $main 'ApplyLanguage' | Out-Null
        Assert ((Field $main 'smoke') -and (Field (Field $main 'newsPanel') 'offlinePreview')) 'Settings regression must use fictional accounts and offline news.'
        $settingsType = $assembly.GetType('AOLauncher.SettingsDialog', $true)
        $dialog = [Activator]::CreateInstance($settingsType, [object[]]@($config))
        $dialog.Font = [Drawing.Font]::new($dialog.Font.FontFamily, $dialog.Font.Size * 1.5, $dialog.Font.Style)
        $dialog.Scale([Drawing.SizeF]::new(1.5,1.5))
        Show-Offscreen $dialog
        $dialog.GetType().GetField('settingsTabs', [Reflection.BindingFlags]'NonPublic,Instance').GetValue($dialog).SelectedIndex = 1
        [Windows.Forms.Application]::DoEvents()
        $button = @(Descendants $dialog | Where-Object Name -eq 'forgetSavedPasswords')[0]
        $status = @(Descendants $dialog | Where-Object Name -eq 'passwordActionStatus')[0]
        Assert ($null -ne $button -and $null -ne $status) 'Settings must include its saved-password controls at 150% scale.'
        $fields = $status.Parent
        $hostControl = $fields.Parent.Parent
        Assert ($hostControl.GetType().Name -eq 'SlimScrollHost') 'Settings fields must use the actual scrollbar wrapper.'
        Assert (!(Property $hostControl 'HorizontalBar').Visible) 'Settings text must wrap without horizontal overflow at 150% scale.'
        $callback = [Func[bool]]{ return $true }
        $settingsType.GetProperty('ForgetPasswordsAction').SetValue($dialog, $callback)
        $button.PerformClick(); Layout $dialog; Sync $fields
        Assert ($status.Text -ceq 'Saved passwords cleared. Accounts and presets are unchanged.') 'A synthetic password callback must show its result without hanging layout.'
        $vertical = Property $hostControl 'VerticalBar'
        if ($vertical.Visible) { Scroll-To $vertical ([int]::MaxValue); Sync $fields }
        Assert ($status.Bottom -le $fields.ClientSize.Height + 1 -and $status.Bottom -gt 0) 'Scrolling Settings must reach the complete action result.'
        $size = $fields.Size
        $clientSize = $fields.ClientSize
        for ($i = 0; $i -lt 20; $i++) { Layout $dialog; Sync $fields }
        Assert ($fields.Size -eq $size -and $fields.ClientSize -eq $clientSize) 'Repeated Settings layout must settle without toggling native scrollbar geometry.'
        Native-No-Gutter $dialog @($hostControl)
        Preview $dialog 'slim-settings-en-1.5.png'
        Assert (!(Field $main 'running') -and $null -eq (Field $main 'backendRunner')) 'Settings regression must never start a backend.'
        "PASS: $script:checks isolated Settings EN/150% checks with the app's GDI text rendering."
    } finally {
        if ($dialog) { $dialog.Dispose() }
        $main.Dispose()
    }
    exit 0
}

# Bound the complete Settings fixture, including synchronous native callbacks;
# counting messages alone cannot interrupt an endlessly draining BeginInvoke.
$probeInfo = [Diagnostics.ProcessStartInfo]::new()
$probeInfo.FileName = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
$probeInfo.Arguments = '-NoProfile -ExecutionPolicy Bypass -STA -File "' + $PSCommandPath + '" -AssemblyPath "' + (Resolve-Path -LiteralPath $AssemblyPath).Path + '" -PreviewDirectory "' + $PreviewDirectory + '" -SettingsProbe'
$probeInfo.UseShellExecute = $false
$probeInfo.CreateNoWindow = $true
$probeInfo.RedirectStandardOutput = $true
$probeInfo.RedirectStandardError = $true
$probe = [Diagnostics.Process]::Start($probeInfo)
try {
    $probeOutput = $probe.StandardOutput.ReadToEndAsync()
    $probeError = $probe.StandardError.ReadToEndAsync()
    if (!$probe.WaitForExit(30000)) {
        # Only terminate the synthetic subprocess created immediately above.
        $probe.Kill(); $probe.WaitForExit()
        throw 'FAIL: Settings EN/150% did not finish within 30 seconds; possible scrollbar/layout feedback loop.'
    }
    $probeText = $probeOutput.Result.Trim()
    $probeErrors = $probeError.Result.Trim()
    Assert ($probe.ExitCode -eq 0) ('Settings EN/150% subprocess failed: ' + $probeErrors + ' ' + $probeText)
    if ($probeText) { $probeText }
} finally { $probe.Dispose() }

foreach ($kind in @('Tree','Log','Cards','Fields','Grid')) {
    $form = [Windows.Forms.Form]::new()
    $form.ClientSize = [Drawing.Size]::new(330,240)
    $form.BackColor = $surface
    $control = $null
    switch ($kind) {
        'Tree' {
            $control = [Windows.Forms.TreeView]::new()
            $control.BorderStyle = [Windows.Forms.BorderStyle]::None
            for ($i = 0; $i -lt 90; $i++) { $control.Nodes.Add(('Synthetic char {0:00}' -f $i)) | Out-Null }
        }
        'Log' {
            $control = [Windows.Forms.TextBox]::new()
            $control.Multiline = $true; $control.ReadOnly = $true; $control.WordWrap = $true
            $control.ScrollBars = [Windows.Forms.ScrollBars]::Vertical
            $control.BorderStyle = [Windows.Forms.BorderStyle]::None
            $control.Text = ((0..99 | ForEach-Object { 'Synthetic log entry ' + $_ }) -join "`r`n")
        }
        'Cards' {
            $control = [Windows.Forms.FlowLayoutPanel]::new()
            $control.AutoScroll = $true; $control.WrapContents = $false; $control.FlowDirection = [Windows.Forms.FlowDirection]::TopDown
            for ($i = 0; $i -lt 35; $i++) {
                $card = [Windows.Forms.Label]::new()
                $card.Text = 'Synthetic article ' + $i; $card.Size = [Drawing.Size]::new(260,48)
                $card.Margin = [Windows.Forms.Padding]::new(0,0,0,8)
                $control.Controls.Add($card)
            }
        }
        'Fields' {
            $control = [Windows.Forms.Panel]::new(); $control.AutoScroll = $true
            $field = [Windows.Forms.Label]::new(); $field.Text = 'Last synthetic field'; $field.Bounds = [Drawing.Rectangle]::new(0,1460,240,40)
            $control.Controls.Add($field)
        }
        'Grid' {
            $control = [Windows.Forms.DataGridView]::new()
            $control.AllowUserToAddRows = $false; $control.RowHeadersVisible = $false
            $control.BackgroundColor = $surface; $control.BorderStyle = [Windows.Forms.BorderStyle]::None
            $control.EnableHeadersVisualStyles = $false
            $control.DefaultCellStyle.BackColor = $surface; $control.DefaultCellStyle.ForeColor = $foreground
            $control.ColumnHeadersDefaultCellStyle.BackColor = $surface
            for ($i = 0; $i -lt 5; $i++) { $column = $control.Columns.Add('Column' + $i, 'Field ' + $i); $control.Columns[$column].Width = 120 }
            for ($i = 0; $i -lt 70; $i++) { $control.Rows.Add([object[]]@('Row ' + $i,'One','Two','Three','Four')) | Out-Null }
        }
    }
    $control.BackColor = $surface; $control.ForeColor = $foreground
    $control.Dock = [Windows.Forms.DockStyle]::Fill
    $hostControl = Wrap $control
    $hostControl.Dock = [Windows.Forms.DockStyle]::Fill
    $form.Controls.Add($hostControl)
    try {
        Show-Offscreen $form; Sync $control
        $bar = Property $hostControl 'VerticalBar'
        Assert ($bar.Visible) "$kind overflowing content must expose a vertical thumb."
        Assert ($bar.Width -le 14) "$kind rail should be narrow at 100% scale."
        Dark-Rail $hostControl $kind
        Scroll-To $bar ([int]::MaxValue); Sync $control
        switch ($kind) {
            'Tree' { Assert ($control.Nodes[89].IsVisible) 'Dragging the accounts scrollbar to its bottom must expose the last char.' }
            'Log' { Assert ([SlimTestNative]::FirstLine($control.Handle) -gt 80) 'Log scrolling must reach its final lines.' }
            'Cards' { Assert ($control.Controls[34].Bottom -le $control.ClientSize.Height + 1) 'News scrolling must reveal the final full card.' }
            'Fields' { Assert ($control.Controls[0].Bottom -le $control.ClientSize.Height + 1) 'Dialog scrolling must reveal its final field.' }
            'Grid' { Assert ($control.Rows[69].Displayed) 'Server-grid scrolling must reach the final server row.' }
        }
        Scroll-To $bar 0; Sync $control
        Assert ((Property $bar 'Position') -eq 0) "$kind scrollbar returns exactly to the top."
        if ($kind -ne 'Grid') {
            [SlimTestNative]::Wheel($control.Handle, -120); Sync $control
            Assert ((Property $bar 'Position') -gt 0) "$kind preserves mouse-wheel scrolling on its content."
        }
        Scroll-To $bar 0; Sync $control
        Mouse $bar 'OnMouseDown' ([int]($bar.Width / 2)) ($bar.Height - 5)
        Mouse $bar 'OnMouseUp' ([int]($bar.Width / 2)) ($bar.Height - 5)
        Assert ((Property $bar 'Position') -gt 0) "$kind clicking below the thumb must page through content."
        Scroll-To $bar 0; Sync $control
        Mouse $bar 'OnMouseDown' ([int]($bar.Width / 2)) 5
        Mouse $bar 'OnMouseMove' ([int]($bar.Width / 2)) ($bar.Height - 2)
        Mouse $bar 'OnMouseUp' ([int]($bar.Width / 2)) ($bar.Height - 2)
        Assert ((Property $bar 'Position') -gt 0) "$kind thumb drag must actually scroll content."
        if ($kind -eq 'Tree') {
            $control.SelectedNode = $control.Nodes[0]
            [SlimTestNative]::Key($control.Handle, 0x23); Sync $control
            Assert ($control.SelectedNode -eq $control.Nodes[89] -and $control.Nodes[89].IsVisible) 'Accounts keyboard End must select and expose the final char.'
            [SlimTestNative]::Key($control.Handle, 0x24); Sync $control
            Assert ($control.SelectedNode -eq $control.Nodes[0] -and $control.TopNode -eq $control.Nodes[0]) 'Accounts keyboard Home must restore the first char.'
        }
        if ($kind -eq 'Grid') {
            $horizontal = Property $hostControl 'HorizontalBar'
            Assert ($horizontal.Visible) 'Wide server grids must retain horizontal scrolling.'
            Scroll-To $horizontal ([int]::MaxValue); Sync $control
            Assert ($control.HorizontalScrollingOffset -gt 0 -and $control.Columns[4].Displayed) 'Horizontal thumb must expose the final server field.'
        }
        $size = $hostControl.Size
        for ($i = 0; $i -lt 15; $i++) { $form.ClientSize = [Drawing.Size]::new(430,340); Layout $form; Sync $control; $form.ClientSize = [Drawing.Size]::new(330,240); Layout $form; Sync $control }
        Assert ($hostControl.Size -eq $size) "$kind repeated resizing must not accumulate clipping margins."
        Preview $form ('slim-' + $kind.ToLowerInvariant() + '.png')
        switch ($kind) {
            'Tree' { $control.Nodes.Clear() }
            'Log' { $control.Clear() }
            'Grid' { $control.Rows.Clear(); foreach ($column in $control.Columns) { $column.Width = 35 } }
            default { foreach ($child in @($control.Controls)) { $child.Dispose() }; $control.Controls.Clear() }
        }
        Layout $control; Sync $control
        Assert (!$bar.Visible) "$kind must hide its scrollbar after all overflowing content is removed."
        Assert ((Property $bar 'Position') -eq 0) "$kind clearing content must reset the scrollbar position."
        Dark-Rail $hostControl ($kind + ' empty')
    } finally { $form.Dispose() }
}

# The actual dashboard, with only its built-in fictional accounts and offline news.
$mainType = $assembly.GetType('AOLauncher.MainForm', $true)
foreach ($language in @('en','da')) {
    foreach ($scale in @(1,1.5)) {
        $main = [Activator]::CreateInstance($mainType, [object[]]@($true))
        try {
            (Field $main 'settings').Language = $language
            Invoke-Instance $main 'ApplyLanguage' | Out-Null
            $main.Size = $main.MinimumSize
            if ($scale -gt 1) { $main.Font = [Drawing.Font]::new($main.Font.FontFamily, $main.Font.Size * $scale, $main.Font.Style); $main.Scale([Drawing.SizeF]::new($scale,$scale)) }
            Show-Offscreen $main
            $tree = Field $main 'tree'; $log = Field $main 'log'; $news = Field $main 'newsPanel'
            Assert ((Field $main 'smoke') -and (Field $news 'offlinePreview')) 'Dashboard scrollbar checks must use synthetic accounts and offline news.'
            for ($i = 0; $i -lt 60; $i++) { $tree.Nodes.Add('Scrollbar fixture ' + $i) | Out-Null }
            $log.Text = ((0..99 | ForEach-Object { 'Synthetic login message ' + $_ }) -join "`r`n")
            Sync $tree
            $nativeHosts = @()
            foreach ($content in @($tree,(Field $news 'cards'))) {
                $owner = $content.Parent
                while ($owner -and $owner.GetType().Name -ne 'SlimScrollHost') { $owner = $owner.Parent }
                Assert ($null -ne $owner) "$($content.Name) must use slim scrolling on the actual dashboard."
                if ($owner) {
                    $nativeHosts += $owner
                    $rail = Property $owner 'VerticalBar'
                    Assert ($rail.Width -le 15 * $scale) 'Dashboard rails must remain narrow at minimum size and 150% scale.'
                    Dark-Rail $owner ($content.Name + ' ' + $language + ' ' + $scale)
                }
            }
            Native-No-Gutter $main $nativeHosts
            $news.SetServerFilter('custom'); Layout $main
            $news.SetServerFilter('all'); Layout $main
            Assert ((Field $news 'cards').Controls.Count -eq 3) 'News filter changes must restore all three preview cards after scrollbar relayout.'
            Assert (!(Field $main 'running') -and $null -eq (Field $main 'backendRunner')) 'Scrollbar tests must never launch a game or backend.'
            Preview $main ('slim-dashboard-' + $language + '-' + $scale + '.png')
        } finally { $main.Dispose() }
    }
}
"PASS: $script:checks slim-scroll behavior and integration checks; synthetic offscreen controls, English/Danish, minimum size and simulated 150% scale. No real profiles, passwords, network or games were accessed."
