param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\src\Launcher\bin\Release\AONexus.exe'),
    [string]$PreviewDirectory = (Join-Path $PSScriptRoot '..\logs\dialog-theme-previews')
)
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Use 32-bit Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Web.Extensions
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing @'
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
public static class DialogThemeNative {
    [StructLayout(LayoutKind.Sequential)] private struct Msg { public IntPtr hwnd; public uint message; public IntPtr wParam,lParam; public uint time; public Point point; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int left,top,right,bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct ComboInfo { public int size; public Rect item,button; public int state; public IntPtr combo,edit,list; }
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    [DllImport("user32.dll")] private static extern bool GetComboBoxInfo(IntPtr window,ref ComboInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out Rect rectangle);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg msg,IntPtr hwnd,uint min,uint max,uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Msg msg);
    // Bounded native pumping makes a layout feedback regression fail rather than
    // trapping Application.DoEvents forever. These are this process's windows.
    public static void Pump(int milliseconds) {
        var timer=Stopwatch.StartNew(); Msg msg; int quiet=0;
        while(timer.ElapsedMilliseconds<milliseconds && quiet<3) {
            if(PeekMessage(out msg,IntPtr.Zero,0,0,1)) { quiet=0; TranslateMessage(ref msg); DispatchMessage(ref msg); }
            else { quiet++; System.Threading.Thread.Sleep(1); }
        }
        if(quiet==0 && timer.ElapsedMilliseconds>=milliseconds) throw new Exception("Dialog layout did not settle within the bounded message-pump budget.");
    }
    public static void Text(IntPtr window,string text) { foreach(char c in text) SendMessage(window,0x0102,new IntPtr(c),IntPtr.Zero); }
    public static void Key(IntPtr window,int key) { SendMessage(window,0x0100,new IntPtr(key),IntPtr.Zero); SendMessage(window,0x0101,new IntPtr(key),IntPtr.Zero); }
    public static int TreeStateImage(IntPtr tree,IntPtr node) { return (int)SendMessage(tree,0x1127,node,new IntPtr(0xf000))&0xf000; }
    public static void Click(IntPtr window,int x,int y) {
        var point=new IntPtr((y<<16)|(x&0xffff));
        SendMessage(window,0x0200,IntPtr.Zero,point); SendMessage(window,0x0201,new IntPtr(1),point); SendMessage(window,0x0202,IntPtr.Zero,point);
    }
    public static Bitmap ComboList(ComboBox combo) {
        var info=new ComboInfo { size=Marshal.SizeOf(typeof(ComboInfo)) }; Rect rect;
        if(!combo.DroppedDown||!GetComboBoxInfo(combo.Handle,ref info)||!GetWindowRect(info.list,out rect)) throw new Exception("Owned combo list is unavailable.");
        var bitmap=new Bitmap(rect.right-rect.left,rect.bottom-rect.top);
        using(var graphics=Graphics.FromImage(bitmap)) { var dc=graphics.GetHdc(); try { if(!PrintWindow(info.list,dc,0)) throw new Exception("Owned combo list printing failed."); } finally { graphics.ReleaseHdc(dc); } }
        return bitmap;
    }
    public static double LightFraction(Bitmap bitmap,Rectangle area) {
        area.Intersect(new Rectangle(Point.Empty,bitmap.Size)); if(area.Width<=0||area.Height<=0) return 0;
        long light=0,total=(long)area.Width*area.Height;
        for(int y=area.Top;y<area.Bottom;y++) for(int x=area.Left;x<area.Right;x++) { var c=bitmap.GetPixel(x,y); if(c.R>205&&c.G>205&&c.B>205) light++; }
        return (double)light/total;
    }
    public static int LongestLightRun(Bitmap bitmap,Rectangle area) {
        area.Intersect(new Rectangle(Point.Empty,bitmap.Size)); int longest=0;
        for(int y=area.Top;y<area.Bottom;y++) { int run=0; for(int x=area.Left;x<area.Right;x++) { var c=bitmap.GetPixel(x,y); run=c.R>205&&c.G>205&&c.B>205?run+1:0; longest=Math.Max(longest,run); } }
        return longest;
    }
}
'@
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$staticFlags = [Reflection.BindingFlags]'Public,NonPublic,Static'
$textsType = $assembly.GetType('AOLauncher.Localization.Texts',$true)
foreach ($registration in @('GuiStrings','NexusStrings')) { $assembly.GetType('AOLauncher.'+$registration,$true).GetMethod('Register',$staticFlags).Invoke($null,@()) | Out-Null }
$script:checks = 0
$script:failures = New-Object 'System.Collections.Generic.List[string]'
function Assert-Check([bool]$Value,[string]$Message) { if (!$Value) { throw "FAIL: $Message" }; $script:checks++ }
function Field($Control,[string]$Name) {
    $type=$Control.GetType()
    while ($type) { $field=$type.GetField($Name,$flags); if ($field) { return $field.GetValue($Control) }; $type=$type.BaseType }
    throw "Field not found: $Name"
}
function New-Model([string]$Name) { [Activator]::CreateInstance($assembly.GetType('AOLauncher.'+$Name,$true)) }
function New-Dialog([string]$Name,[object[]]$Arguments) { [Activator]::CreateInstance($assembly.GetType('AOLauncher.'+$Name,$true),$flags,$null,$Arguments,$null) }
function Descendants($Control) { foreach ($child in $Control.Controls) { $child; Descendants $child } }
function Pump { [DialogThemeNative]::Pump(1800) }
function Layout($Control) { $Control.PerformLayout(); foreach ($child in $Control.Controls) { $child.PerformLayout() }; Pump }
function Command-Key($Control,[Windows.Forms.Keys]$Key) {
    # WinForms TabControl handles Ctrl+Tab in OnKeyDown, not ProcessCmdKey.
    # Supply modifier state directly without changing the desktop keyboard state.
    $Control.GetType().GetMethod('OnKeyDown',$flags).Invoke($Control,[object[]]@([Windows.Forms.KeyEventArgs]::new($Key)))
}
function Dialog-Key($Control,[Windows.Forms.Keys]$Key) { $Control.GetType().GetMethod('ProcessDialogKey',$flags).Invoke($Control,[object[]]@($Key)) }
function Show-Fixture($Dialog,[double]$Scale) {
    $Dialog.ShowInTaskbar=$false; $Dialog.StartPosition=[Windows.Forms.FormStartPosition]::Manual
    $Dialog.Location=[Drawing.Point]::new(-32000,-32000)
    if ($Scale -gt 1) {
        $Dialog.Font=[Drawing.Font]::new($Dialog.Font.FontFamily,$Dialog.Font.Size*$Scale,$Dialog.Font.Style)
        $Dialog.Scale([Drawing.SizeF]::new($Scale,$Scale))
    }
    $Dialog.Show(); Layout $Dialog
}
function Control-Rect($Dialog,$Control) { [Drawing.Rectangle]::new($Dialog.PointToClient($Control.PointToScreen([Drawing.Point]::Empty)),$Control.ClientSize) }
function Preview($Dialog,[string]$Name) {
    Layout $Dialog
    $bitmap=[Drawing.Bitmap]::new($Dialog.ClientSize.Width,$Dialog.ClientSize.Height)
    $graphics=[Drawing.Graphics]::FromImage($bitmap); $dc=$graphics.GetHdc()
    try { Assert-Check ([DialogThemeNative]::PrintWindow($Dialog.Handle,$dc,1)) "Native rendering succeeds: $Name" }
    finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    if ($PreviewDirectory) {
        [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PreviewDirectory)) | Out-Null
        $bitmap.Save((Join-Path $PreviewDirectory $Name),[Drawing.Imaging.ImageFormat]::Png)
    }
    return $bitmap
}
function Check-Theme($Dialog,[string]$Name) {
    Assert-Check ($Dialog.BackColor.GetBrightness() -lt .25) "$Name has a dark dialog surface."
    foreach ($control in (Descendants $Dialog)) {
        if ($control -is [Windows.Forms.TextBoxBase] -or $control -is [Windows.Forms.ComboBox] -or $control -is [Windows.Forms.NumericUpDown]) {
            Assert-Check ($control.BackColor.GetBrightness() -lt .30) "$Name/$($control.GetType().Name) uses a dark input surface."
        }
        if ($control -is [Windows.Forms.Button] -and $control.Visible -and
            ($control -eq $Dialog.AcceptButton -or $control -eq $Dialog.CancelButton -or $control.Name -like 'messageAction*')) {
            $bounds=Control-Rect $Dialog $control
            Assert-Check ($bounds.Left -ge 0 -and $bounds.Right -le $Dialog.ClientSize.Width -and $bounds.Bottom -le $Dialog.ClientSize.Height) "$Name action buttons fit the dialog."
        }
    }
}
function Check-ArrowPixels($Dialog,$Control,$Bitmap,[string]$Description) {
    $bounds=Control-Rect $Dialog $Control
    $width=[Math]::Min(24,$bounds.Width)
    $arrow=[Drawing.Rectangle]::new($bounds.Right-$width,$bounds.Top,$width,$bounds.Height)
    $light=[DialogThemeNative]::LightFraction($Bitmap,$arrow)
    Assert-Check ($light -lt .25) "$Description has no white native arrow/spinner background (light fraction $light)."
}
function Check-TabPixels($Dialog,$Tabs,$Bitmap) {
    $bounds=Control-Rect $Dialog $Tabs
    $last=$Tabs.GetTabRect($Tabs.TabCount-1)
    $blank=[Drawing.Rectangle]::new($bounds.Left+$last.Right+5,$bounds.Top+2,[Math]::Max(1,$bounds.Width-$last.Right-7),[Math]::Max(1,$last.Height-3))
    Assert-Check ([DialogThemeNative]::LightFraction($Bitmap,$blank) -lt .03) 'The unused tab header stays dark.'
    Assert-Check ([DialogThemeNative]::LongestLightRun($Bitmap,$bounds) -lt 65) 'The tab page has no long white native frame or header line.'
    for ($i=0;$i -lt $Tabs.TabCount;$i++) {
        $needed=[Windows.Forms.TextRenderer]::MeasureText($Tabs.TabPages[$i].Text,$Tabs.Font)
        Assert-Check ($Tabs.GetTabRect($i).Width -ge $needed.Width+12) 'Every account tab title fits at its current font size without ellipsis.'
    }
}
function Check-Dropdown($Combo,[string]$Name) {
    $Combo.DroppedDown=$true; Pump
    try {
        $bitmap=[DialogThemeNative]::ComboList($Combo)
        try {
            $content=[Drawing.Rectangle]::new(2,2,$bitmap.Width-4,$bitmap.Height-4)
            Assert-Check ([DialogThemeNative]::LightFraction($bitmap,$content) -lt .15) 'Expanded combo choices use dark item surfaces.'
            if ($PreviewDirectory) { $bitmap.Save((Join-Path $PreviewDirectory $Name),[Drawing.Imaging.ImageFormat]::Png) }
        } finally { $bitmap.Dispose() }
    } finally { $Combo.DroppedDown=$false; Pump }
}
function Scroll-To($Dialog,$Control) {
    $fields=Field $Dialog 'Fields'; $fields.ScrollControlIntoView($Control)
    $assembly.GetType('AOLauncher.SlimScrolling').GetMethod('Refresh',$staticFlags).Invoke($null,[object[]]@($fields)) | Out-Null
    Layout $Dialog
}
function All-Nodes($Nodes) { foreach ($node in $Nodes) { $node; All-Nodes $node.Nodes } }
function Check-NoTooltip($Control) {
    if ($Control -is [Windows.Forms.TreeView]) {
        Assert-Check (!$Control.ShowNodeToolTips) 'Tree node tooltips are disabled.'
        $parameters=$Control.GetType().GetProperty('CreateParams',$flags).GetValue($Control)
        Assert-Check (($parameters.Style -band 0x80) -ne 0) 'Native clipped-label hints are disabled.'
    } else { Assert-Check (!$Control.ShowCellToolTips) 'All grid tooltips are disabled.' }
}
function Hit-Point($Tree,$Node,[Windows.Forms.TreeViewHitTestLocations]$Kind) {
    $Node.EnsureVisible(); Pump
    $middle=$Node.Bounds.Top+[int]($Node.Bounds.Height/2)
    for ($x=0;$x -lt $Node.Bounds.Left;$x++) {
        $hit=$Tree.HitTest($x,$middle)
        if ($hit.Node -eq $Node -and ($hit.Location -band $Kind)) { return [Drawing.Point]::new($x,$middle) }
    }
    return $null
}
function Check-Tree([string]$Language,[double]$Scale) {
    $form=New-Dialog 'MainForm' ([object[]]@($true))
    try {
        Assert-Check ((Field $form 'smoke') -and (Field (Field $form 'newsPanel') 'offlinePreview')) 'The tree fixture uses sample accounts and offline news.'
        (Field $form 'settings').Language=$Language
        $form.GetType().GetMethod('ApplyLanguage',$flags).Invoke($form,@()) | Out-Null
        $form.Size=$form.MinimumSize; Show-Fixture $form $Scale
        $tree=Field $form 'tree'; $tree.ExpandAll(); Pump
        Check-NoTooltip $tree
        $accountNode=@(All-Nodes $tree.Nodes | Where-Object { $_.Tag -and $_.Tag.GetType().Name -eq 'AccountConfig' })[0]
        Assert-Check ($null -ne $accountNode) 'The tree contains a synthetic account to exercise.'
        $point=Hit-Point $tree $accountNode ([Windows.Forms.TreeViewHitTestLocations]::PlusMinus)
        Assert-Check ($null -ne $point -and $accountNode.IsExpanded) 'The dark expand glyph retains its native hit region.'
        [DialogThemeNative]::Click($tree.Handle,$point.X,$point.Y); Pump
        Assert-Check (!$accountNode.IsExpanded) 'The native expand-glyph click collapses its account.'
        $point=Hit-Point $tree $accountNode ([Windows.Forms.TreeViewHitTestLocations]::PlusMinus)
        [DialogThemeNative]::Click($tree.Handle,$point.X,$point.Y); Pump
        Assert-Check ($accountNode.IsExpanded) 'The native expand-glyph hit region expands its account again after scroll position changes.'
        $charNode=@($accountNode.Nodes | Where-Object { $_.Tag -and $_.Tag.GetType().Name -ne 'CreationAction' })[0]
        $point=Hit-Point $tree $charNode ([Windows.Forms.TreeViewHitTestLocations]::StateImage)
        Assert-Check ($null -ne $point) 'The dark character checkbox retains a native hit region.'
        $wasChecked=$charNode.Checked
        [DialogThemeNative]::Click($tree.Handle,$point.X,$point.Y); Pump
        Assert-Check ($charNode.Checked -ne $wasChecked) 'Native checkbox clicking toggles the character selection.'
        $tree.SelectedNode=$charNode; $charNode.EnsureVisible(); $tree.Focus() | Out-Null; Pump
        $bitmap=Preview $form "MainForm-tree-focused-$Language-$Scale.png"
        try {
            $origin=$form.PointToClient($tree.PointToScreen([Drawing.Point]::new(0,$charNode.Bounds.Top)))
            $row=[Drawing.Rectangle]::new($origin.X,$origin.Y,$tree.ClientSize.Width,$charNode.Bounds.Height)
            Assert-Check ([DialogThemeNative]::LightFraction($bitmap,$row) -lt .15) 'The focused selected tree row has a dark fill.'
        } finally { $bitmap.Dispose() }
        $button=@(Descendants $form | Where-Object { $_ -is [Windows.Forms.Button] -and $_.CanFocus })[0]
        $button.Focus() | Out-Null; Pump
        Assert-Check (!$tree.Focused) 'The inactive-selection preview removes focus from the tree.'
        $bitmap=Preview $form "MainForm-tree-inactive-$Language-$Scale.png"
        try { Assert-Check ([DialogThemeNative]::LightFraction($bitmap,$row) -lt .15) 'The inactive selected tree row remains dark.' } finally { $bitmap.Dispose() }
        $creation=@(All-Nodes $tree.Nodes | Where-Object { $_.Tag -and $_.Tag.GetType().Name -eq 'CreationAction' })[0]
        Assert-Check ($null -ne $creation) 'Synthetic accounts expose a create-character action.'
        $creation.EnsureVisible(); Pump
        Assert-Check ([DialogThemeNative]::TreeStateImage($tree.Handle,$creation.Handle) -eq 0) 'The creation row keeps its native checkbox hidden.'
        Assert-Check ($null -eq (Hit-Point $tree $creation ([Windows.Forms.TreeViewHitTestLocations]::StateImage))) 'The creation row does not expose a painted checkbox hit target.'
        $tree.SelectedNode=$creation; Pump
        $bitmap=Preview $form "MainForm-tree-creation-$Language-$Scale.png"; $bitmap.Dispose()
    } finally {
        $form.Dispose(); Pump
    }
}

# Construct only in-memory data. No discovery, profile store, credential store,
# game executable, browser or network path is invoked by these fixtures.
$settings=New-Model 'LauncherSettings'
$account=New-Model 'AccountConfig'; $account.Label='Synthetic team'; $account.Username='SyntheticAccount'
$account.GamePath='C:\SyntheticGames\AO'; $account.Server='127.0.0.1'; $account.Port=7500; $account.ServerProfileId='rebirth-local'
$character=New-Model 'CharacterConfig'; $character.Name='SyntheticChar'; $character.CharacterId=123456
$candidateType=$assembly.GetType('AOLauncher.PrefsCandidate',$true)
$candidates=[Array]::CreateInstance($candidateType,3)
for ($i=0;$i -lt 3;$i++) {
    $candidate=[Activator]::CreateInstance($candidateType)
    $values=@{ InstallationName="Synthetic client $i"; AccountName="SyntheticAccount$i"; PrefsPath="C:\SyntheticPrefs\Client$i\Prefs\SyntheticAccount$i"; GamePath="C:\SyntheticGames\Client$i"; CharacterCount=($i+2); GameExists=($i -ne 2); MatchesGamePath=($i -eq 0) }
    foreach ($entry in $values.GetEnumerator()) { $candidateType.GetProperty($entry.Key).SetValue($candidate,$entry.Value,$null) }
    $candidates.SetValue($candidate,$i)
}
$serializer=[Web.Script.Serialization.JavaScriptSerializer]::new()
$originalSettings=$serializer.Serialize($settings)
$originalAccount=$serializer.Serialize($account)
function Candidate-Snapshot {
    $values=New-Object 'System.Collections.Generic.List[string]'
    foreach ($candidate in $candidates) {
        foreach ($property in ($candidateType.GetProperties() | Sort-Object Name)) { $values.Add($property.Name+':'+$serializer.Serialize($property.GetValue($candidate,$null))) }
    }
    return [string]::Join('|',$values)
}
$originalCandidates=Candidate-Snapshot

foreach ($language in @('en','da')) {
    $textsType.GetMethod('SetLanguage',$staticFlags).Invoke($null,[object[]]@($language)) | Out-Null
    $saveText=$textsType.GetMethod('T',$staticFlags).Invoke($null,[object[]]@('Save'))
    Assert-Check ($saveText -ceq $(if ($language -eq 'da') { 'Gem' } else { 'Save' })) 'The fixture registers and applies production GUI translations.'
    foreach ($scale in @(1,1.5)) {
        $suffix="$language-$scale.png"
        foreach ($kind in @('AccountDialog','CharacterDialog','PresetNameDialog','PasswordDialog','SettingsDialog','LoginServersDialog','PrefsPickerDialog','LaunchConfirmationDialog','SelectedTargetsDialog')) {
            switch ($kind) {
                'AccountDialog' { $arguments=[object[]]@($null,$settings) }
                'CharacterDialog' { $arguments=[object[]]@('SyntheticAccount',$character) }
                'PresetNameDialog' { $arguments=[object[]]@('Synthetic preset','Synthetic team',[string[]]@('Reserved')) }
                'PasswordDialog' { $arguments=[object[]]@($account) }
                'SettingsDialog' { $arguments=[object[]]@($settings) }
                'LoginServersDialog' { $arguments=New-Object object[] 1; $arguments[0]=$settings.LoginServers }
                'PrefsPickerDialog' { $arguments=New-Object object[] 2; $arguments[0]=$candidates; $arguments[1]='SyntheticAccount0' }
                'LaunchConfirmationDialog' { $arguments=[object[]]@('3 characters across 2 accounts on 2 servers. Synthetic confirmation only.') }
                'SelectedTargetsDialog' { $arguments=[object[]]@("Synthetic account A - Local development`r`nSynthetic account B - Another server") }
            }
            $dialog=New-Dialog $kind $arguments
            try {
                Show-Fixture $dialog $scale
                Check-Theme $dialog $kind
                $bitmap=Preview $dialog "$kind-$suffix"
                try {
                    if ($kind -eq 'AccountDialog') { Check-TabPixels $dialog (Field $dialog 'accountTabs') $bitmap }
                    if ($kind -eq 'SettingsDialog') { Check-ArrowPixels $dialog (Field $dialog 'languageInput') $bitmap 'Language selection' }
                    foreach ($grid in @(Descendants $dialog | Where-Object { $_ -is [Windows.Forms.DataGridView] })) {
                        Assert-Check (!$grid.EnableHeadersVisualStyles -and $grid.BackgroundColor.GetBrightness() -lt .25) "$kind grid and header use the dark theme."
                        Assert-Check ([DialogThemeNative]::LightFraction($bitmap,(Control-Rect $dialog $grid)) -lt .20) "$kind grid has no large white native fill."
                        Check-NoTooltip $grid
                    }
                } finally { $bitmap.Dispose() }
                switch ($kind) {
                    'AccountDialog' {
                        $tabs=Field $dialog 'accountTabs'
                        Assert-Check ($tabs -is [Windows.Forms.TabControl]) 'The themed tabs preserve native tab-control semantics.'
                        $tabs.Focus() | Out-Null
                        Command-Key $tabs ([Windows.Forms.Keys]::Control -bor [Windows.Forms.Keys]::Tab) | Out-Null
                        Assert-Check ($tabs.SelectedIndex -eq 1) 'Ctrl+Tab switches from Account and Prefs to Game and server.'
                        Layout $dialog
                        $combo=Field $dialog 'serverProfile'; $combo.SelectedIndex=0; $combo.Focus() | Out-Null
                        [DialogThemeNative]::Key($combo.Handle,0x28); Pump
                        Assert-Check ($combo.SelectedIndex -eq 1) 'The themed server combo supports native Down-key selection.'
                        $bitmap=Preview $dialog "AccountDialog-game-$suffix"
                        try { Check-TabPixels $dialog $tabs $bitmap; Check-ArrowPixels $dialog $combo $bitmap 'Server selection' } finally { $bitmap.Dispose() }
                        Check-Dropdown $combo "AccountDialog-server-choices-$suffix"
                        $tabs.Focus() | Out-Null
                        Command-Key $tabs ([Windows.Forms.Keys]::Control -bor [Windows.Forms.Keys]::Shift -bor [Windows.Forms.Keys]::Tab) | Out-Null
                        Assert-Check ($tabs.SelectedIndex -eq 0) 'Ctrl+Shift+Tab returns to Account and Prefs.'
                        (Field $dialog 'inheritCharacterSlots').Checked=$false
                        $number=Field $dialog 'characterSlotsInput'; Scroll-To $dialog $number.Parent
                        $edit=@($number.Controls | Where-Object { $_ -is [Windows.Forms.TextBoxBase] })[0]
                        Assert-Check ($null -ne $edit -and $number -is [Windows.Forms.NumericUpDown]) 'The themed number input retains its native editable child.'
                        $edit.Focus() | Out-Null; $edit.SelectAll(); [DialogThemeNative]::Text($edit.Handle,'12'); $number.UpButton()
                        Assert-Check ($number.Value -eq 13 -and $dialog.CharacterSlots -eq 13) 'Typed numeric input is parsed and incremented without changing its value contract.'
                        $spinner=@($number.Controls | Where-Object { $_ -isnot [Windows.Forms.TextBoxBase] })[0]
                        [DialogThemeNative]::Click($spinner.Handle,[int]($spinner.Width/2),[int]($spinner.Height/4)); Pump
                        Assert-Check ($number.Value -eq 14) 'Clicking the native spinner still increments the value.'
                        $bitmap=Preview $dialog "AccountDialog-slots-$suffix"
                        try { Check-ArrowPixels $dialog $number $bitmap 'Character-slot spinner'; Check-TabPixels $dialog $tabs $bitmap } finally { $bitmap.Dispose() }
                        (Field $dialog 'inheritCharacterSlots').Checked=$true
                        Assert-Check (!$number.Enabled -and $null -eq $dialog.CharacterSlots) 'The server-slot override can still be disabled.'
                        $check=Field $dialog 'inheritCharacterSlots'; $check.Enabled=$false
                        $bitmap=Preview $dialog "AccountDialog-disabled-$suffix"
                        try { Check-ArrowPixels $dialog $number $bitmap 'Disabled character-slot spinner' } finally { $bitmap.Dispose() }
                        $check.Enabled=$true; $check.Focus() | Out-Null; [DialogThemeNative]::Key($check.Handle,0x20); Pump
                        Assert-Check (!$check.Checked -and $number.Enabled) 'The themed checkbox keeps native Space-key toggling and its dependent input state.'
                    }
                    'CharacterDialog' {
                        $name=Field $dialog 'nameInput'; $name.SelectAll(); [DialogThemeNative]::Text($name.Handle,'EditedSynthetic')
                        Assert-Check ($dialog.CharacterName -eq 'EditedSynthetic') 'The themed text input accepts native character messages.'
                        $name.Focus() | Out-Null; Pump
                        $bitmap=Preview $dialog "CharacterDialog-focused-$suffix"
                        try {
                            $point=$dialog.PointToClient($name.Parent.PointToScreen($name.Location))
                            $border=[Drawing.Rectangle]::new($point.X,$point.Y,$name.Width,1)
                            Assert-Check ([DialogThemeNative]::LightFraction($bitmap,$border) -lt .03) 'The focused textbox has no white native top border.'
                        } finally { $bitmap.Dispose() }
                    }
                    'PresetNameDialog' {
                        (Field $dialog 'nameInput').Text='Reserved'; (Field $dialog 'SaveButton').PerformClick()
                        Assert-Check ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::None -and (Field $dialog 'ErrorLabel').Text.Length -gt 0) 'Duplicate preset validation remains visible.'
                    }
                    'PasswordDialog' {
                        $password=Field $dialog 'passwordInput'
                        Assert-Check ($password.UseSystemPasswordChar) 'The themed password control keeps native password masking.'
                        Assert-Check (@(Descendants $dialog | Where-Object Name -eq 'passwordGuide').Count -eq 1) 'The password usage and removal guide remains present.'
                    }
                    'SettingsDialog' {
                        $combo=Field $dialog 'languageInput'; $combo.SelectedIndex=0; [DialogThemeNative]::Key($combo.Handle,0x28); Pump
                        Assert-Check ($dialog.Language -eq 'da') 'Language selection still updates through native keyboard input.'
                        Check-Dropdown $combo "SettingsDialog-language-choices-$suffix"
                    }
                    'LoginServersDialog' {
                        $grid=Field $dialog 'grid'; $grid.CurrentCell=$grid.Rows[0].Cells[0]
                        Assert-Check ($grid.BeginEdit($true)) 'The themed server grid enters edit mode.'
                        Assert-Check ($grid.EditingControl.BackColor.GetBrightness() -lt .30) 'The live server-grid editor stays dark.'
                        $grid.EditingControl.Text='Synthetic edited server'; $grid.EndEdit() | Out-Null
                        Assert-Check ($grid.Rows[0].Cells[0].Value -eq 'Synthetic edited server') 'Grid editing commits without changing the original settings object.'
                    }
                    'PrefsPickerDialog' {
                        $grid=Field $dialog 'folders'
                        Assert-Check (!(Field $dialog 'SaveButton').Enabled) 'Prefs are not selected or imported automatically.'
                        $grid.CurrentCell=$grid.Rows[0].Cells[0]; $grid.Rows[0].Selected=$true; Pump
                        Assert-Check ((Field $dialog 'SaveButton').Enabled -and (Field $dialog 'folderDetails').Text.Contains('C:\SyntheticPrefs\Client0')) 'Selecting a synthetic Prefs candidate updates the details and enables its action.'
                    }
                }
                "PASS: $kind $language at $scale scale."
            } catch {
                $failure="$kind $language scale $scale : $($_.Exception.GetBaseException().Message)"
                $script:failures.Add($failure); Write-Output "FAIL: $failure"
            } finally {
                $dialog.Dispose(); Pump
            }
        }
        $message=New-Dialog 'NexusMessageDialog' ([object[]]@("Synthetic warning.`r`nThis fixture never starts a game or reads a saved profile.",'AO Nexus',[Windows.Forms.MessageBoxButtons]::YesNoCancel,[Windows.Forms.MessageBoxIcon]::Warning,[Windows.Forms.MessageBoxDefaultButton]::Button2))
        try {
            Show-Fixture $message $scale; Check-Theme $message 'NexusMessageDialog'
            $bitmap=Preview $message "NexusMessageDialog-$suffix"; $bitmap.Dispose()
            $default=$message.GetType().GetProperty('DefaultResult',$flags).GetValue($message,$null)
            $dismiss=$message.GetType().GetProperty('DismissResult',$flags).GetValue($message,$null)
            Assert-Check ($default -eq [Windows.Forms.DialogResult]::No -and $dismiss -eq [Windows.Forms.DialogResult]::Cancel) 'The dark message dialog preserves default and dismissal choices.'
            Dialog-Key $message ([Windows.Forms.Keys]::Escape) | Out-Null
            Assert-Check ($message.DialogResult -eq [Windows.Forms.DialogResult]::Cancel) 'Escape dismisses a cancelable themed prompt without accepting it.'
        } finally { $message.Dispose(); Pump }
        Check-Tree $language $scale
    }
}
$textsType.GetMethod('SetLanguage',$staticFlags).Invoke($null,[object[]]@('en')) | Out-Null
foreach ($case in @(
    @{ Buttons='OK'; Default='Button1'; Result='OK'; Dismiss='OK' },
    @{ Buttons='OKCancel'; Default='Button2'; Result='Cancel'; Dismiss='Cancel' },
    @{ Buttons='YesNo'; Default='Button2'; Result='No'; Dismiss='None' },
    @{ Buttons='YesNoCancel'; Default='Button3'; Result='Cancel'; Dismiss='Cancel' },
    @{ Buttons='RetryCancel'; Default='Button2'; Result='Cancel'; Dismiss='Cancel' },
    @{ Buttons='AbortRetryIgnore'; Default='Button3'; Result='Ignore'; Dismiss='None' }
)) {
    $message=New-Dialog 'NexusMessageDialog' ([object[]]@('Synthetic close and default-button regression.','AO Nexus',[Windows.Forms.MessageBoxButtons]$case.Buttons,[Windows.Forms.MessageBoxIcon]::Information,[Windows.Forms.MessageBoxDefaultButton]$case.Default))
    try {
        Show-Fixture $message 1
        $expected=[Windows.Forms.DialogResult]$case.Result
        Assert-Check ($message.AcceptButton.DialogResult -eq $expected) "$($case.Buttons) preserves the requested default action."
        if ($case.Dismiss -eq 'None') {
            Assert-Check (!$message.ControlBox) "$($case.Buttons) does not offer an implicit close choice."
            Dialog-Key $message ([Windows.Forms.Keys]::Escape) | Out-Null
            Assert-Check ($message.Visible -and $message.DialogResult -eq [Windows.Forms.DialogResult]::None) "$($case.Buttons) ignores Escape."
            $message.DialogResult=[Windows.Forms.DialogResult]::Cancel
            $closing=[Windows.Forms.FormClosingEventArgs]::new([Windows.Forms.CloseReason]::UserClosing,$false)
            $message.GetType().GetMethod('OnFormClosing',$flags).Invoke($message,[object[]]@($closing)) | Out-Null
            Assert-Check ($closing.Cancel -and $message.DialogResult -eq [Windows.Forms.DialogResult]::None) "$($case.Buttons) rejects Windows' implicit Cancel on close (cancel=$($closing.Cancel), result=$($message.DialogResult))."
        } else {
            $message.Close(); Pump
            Assert-Check ($message.DialogResult -eq [Windows.Forms.DialogResult]$case.Dismiss) "$($case.Buttons) maps its close action to the permitted result."
        }
    } finally { $message.Dispose(); Pump }
}
Assert-Check ($serializer.Serialize($settings) -ceq $originalSettings) 'Dialog exercises leave synthetic input settings unchanged.'
Assert-Check ($serializer.Serialize($account) -ceq $originalAccount) 'Dialog exercises leave synthetic account data unchanged.'
Assert-Check ((Candidate-Snapshot) -ceq $originalCandidates) 'Prefs selection leaves injected discovery metadata unchanged.'
if ($script:failures.Count) { throw ("Dialog regression failures: `r`n" + [string]::Join("`r`n",$script:failures)) }
"PASS: $script:checks dialog-theme and input checks. Version: $($assembly.GetName().Version)."
"Previews: $([IO.Path]::GetFullPath($PreviewDirectory))"
