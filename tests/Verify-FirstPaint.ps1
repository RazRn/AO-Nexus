param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'),[switch]$Baseline)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
public sealed class FirstPaintObserver {
    public readonly List<string> LightEditors = new List<string>();
    public int Handles;
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr window,int index);
    public void Watch(Control c) {
        c.HandleCreated += (s,e) => Check((Control)s);
        if(c.IsHandleCreated) Check(c);
        foreach(Control child in c.Controls) Watch(child);
    }
    private void Check(Control c) {
        Handles++;
        if(c is TextBoxBase || c is ComboBox || c is ListBox || c is DataGridView) {
            Color color = c is DataGridView ? ((DataGridView)c).BackgroundColor : c.BackColor;
            if(color.R>180 && color.G>180 && color.B>180) LightEditors.Add(c.GetType().Name+":"+c.Name);
        }
    }
}
'@
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Instance'
function NewModel($name){[Activator]::CreateInstance($a.GetType('AOLauncher.'+$name),$true)}
$settings=NewModel 'LauncherSettings'
$rows=@()
foreach($scale in @(1,1.5)) {
 foreach($kind in @('MainForm','SettingsDialog','AccountDialog','CharacterDialog','NexusMessageDialog','LogViewer')) {
  $arguments=switch($kind) {
   'MainForm' {,[object[]]@($true)}
   'SettingsDialog' {,[object[]]@($settings)}
   'AccountDialog' {,[object[]]@($null,$settings)}
   'CharacterDialog' {,[object[]]@('Synthetic',$null)}
   'NexusMessageDialog' {,[object[]]@('Synthetic message','Synthetic',[Windows.Forms.MessageBoxButtons]::OK,[Windows.Forms.MessageBoxIcon]::Information,[Windows.Forms.MessageBoxDefaultButton]::Button1)}
   'LogViewer' {,[object[]]@()}
  }
  $form=[Activator]::CreateInstance($a.GetType('AOLauncher.'+$kind),$flags,$null,$arguments,$null)
  try {
   $observer=[FirstPaintObserver]::new();$observer.Watch($form)
   $form.ShowInTaskbar=$false;$form.StartPosition='Manual';$form.Location=[Drawing.Point]::new(-18000,-18000)
   if($scale -ne 1){$form.Scale([Drawing.SizeF]::new($scale,$scale))}
   $watch=[Diagnostics.Stopwatch]::StartNew()
   $form.Show();[Windows.Forms.Application]::DoEvents();$form.Refresh();[Windows.Forms.Application]::DoEvents()
   $watch.Stop()
   $composited=([FirstPaintObserver]::GetWindowLong($form.Handle,-20) -band 0x02000000) -ne 0
   if(!$Baseline -and (!$composited -or $observer.LightEditors.Count -gt 0)){throw "$kind first-paint regression: composed=$composited; light editors=$($observer.LightEditors -join ', ')"}
   if($form.BackColor.GetBrightness() -gt 0.25){throw "$kind has a light window background"}
   $form.Hide();$form.Show();[Windows.Forms.Application]::DoEvents()
   if(!$form.Visible){throw "$kind did not reopen"}
   $rows += [pscustomobject]@{Form=$kind;Scale=$scale;OpenMs=[Math]::Round($watch.Elapsed.TotalMilliseconds,1);Handles=$observer.Handles;LightEditors=$observer.LightEditors.Count;Composited=$composited}
  } finally {$form.Dispose()}
 }
}
$rows | Format-Table -AutoSize
Write-Output 'PASS: initial native-editor colours, window composition, opening and reopening; synthetic profiles only.'
