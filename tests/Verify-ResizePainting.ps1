param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ResizePaintNative {
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
  [DllImport("user32.dll")] public static extern bool GetUpdateRect(IntPtr hwnd,out Rect rect,bool erase);
  [DllImport("user32.dll")] public static extern bool ValidateRect(IntPtr hwnd,IntPtr rect);
}
'@
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$checks=0
function Assert($ok,$message){if(!$ok){throw $message};$script:checks++}
$hostForm=[Windows.Forms.Form]::new();$hostForm.StartPosition='Manual';$hostForm.Location=[Drawing.Point]::new(-18000,-18000);$hostForm.ClientSize=[Drawing.Size]::new(850,400);$hostForm.ShowInTaskbar=$false
try {
  $hostForm.Show();[Windows.Forms.Application]::DoEvents()
  foreach($name in @('NexusPanel','NexusHero','NexusButton','NexusComboBox')) {
    $control=[Activator]::CreateInstance($a.GetType('AOLauncher.'+$name),$true)
    try {
      $control.Location=[Drawing.Point]::new(10,10);$control.Size=[Drawing.Size]::new(320,80)
      if($control -is [Windows.Forms.ComboBox]){$control.DropDownStyle='DropDownList';$control.Items.Add('Example team')|Out-Null;$control.SelectedIndex=0}
      $hostForm.Controls.Add($control);$control.CreateControl();$control.Refresh();[Windows.Forms.Application]::DoEvents()
      foreach($width in @(560,400,740,320)) {
        $oldWidth=$control.Width
        [ResizePaintNative]::ValidateRect($control.Handle,[IntPtr]::Zero)|Out-Null
        $control.Width=$width
        $dirty=[ResizePaintNative+Rect]::new();$hasDirty=[ResizePaintNative]::GetUpdateRect($control.Handle,[ref]$dirty,$false)
        $sampleX=[Math]::Min($oldWidth-1,$width-2)
        Assert ($hasDirty -and $dirty.Left -le $sampleX -and $dirty.Right -gt $sampleX -and $dirty.Top -le 2 -and $dirty.Bottom -ge $control.Height-1) "$name must repaint previous border/arrow positions after resize to $width"
        $control.Update();[Windows.Forms.Application]::DoEvents()
      }
    } finally {$hostForm.Controls.Remove($control);$control.Dispose()}
  }
} finally {$hostForm.Dispose()}
$main=[Activator]::CreateInstance($a.GetType('AOLauncher.MainForm'),[object[]]@($true))
try {
  $main.ShowInTaskbar=$false;$main.StartPosition='Manual';$main.Location=[Drawing.Point]::new(-18000,-18000);$main.Show();[Windows.Forms.Application]::DoEvents()
  for($cycle=0;$cycle -lt 3;$cycle++) {
    $main.WindowState='Maximized';[Windows.Forms.Application]::DoEvents()
    foreach($name in @('presetSection','accountsByServer')) {
      $panel=$main.Controls.Find($name,$true)[0]
      Assert ($panel.Visible -and $panel.Width -gt 100 -and $panel.Parent.ClientRectangle.Contains($panel.Bounds)) "$name stays inside the maximized workspace on cycle $cycle"
    }
    $main.WindowState='Normal';$main.Location=[Drawing.Point]::new(-18000,-18000);[Windows.Forms.Application]::DoEvents()
  }
} finally {$main.Dispose()}
Write-Output "PASS: $checks native resize invalidation checks across repeated grow/shrink operations. No real app profile or game touched."
