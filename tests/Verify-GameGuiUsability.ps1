param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'),[string]$PreviewDirectory=(Join-Path $PSScriptRoot '../logs/game-gui-usability'),[switch]$CaptureOnly)
$ErrorActionPreference='Stop'
if([IntPtr]::Size -ne 4){throw 'Use x86 PowerShell -STA'}
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class GameGuiUsabilityCapture {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd,IntPtr dc,uint flags);
    public static int Calls, Targets; public static bool Decline, Installation;
    public static System.Threading.Tasks.Task<bool> Execute<T>(string description,System.Collections.Generic.List<T> targets,bool installation,Action action) {
        Calls++; Targets=targets.Count; Installation=installation;
        if (Decline) return System.Threading.Tasks.Task.FromResult(false);
        action(); return System.Threading.Tasks.Task.FromResult(true);
    }
}
'@
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$f=[Reflection.BindingFlags]'Public,NonPublic,Instance';$s=[Reflection.BindingFlags]'Public,NonPublic,Static';$checks=0;$failures=[Collections.Generic.List[string]]::new()
function Assert($ok,$message){if(!$ok){if($CaptureOnly){$script:failures.Add($message);return};throw $message};$script:checks++}
function Model($name){[Activator]::CreateInstance($a.GetType('AOLauncher.'+$name),$true)}
function Field($o,$name){$type=$o.GetType();while($type){$field=$type.GetField($name,$f);if($field){return ,$field.GetValue($o)};$type=$type.BaseType};throw "Missing field $name"}
function Call($o,$name,[object[]]$values=@()){for($i=0;$i -lt $values.Count;$i++){if($null -ne $values[$i]){$values[$i]=$values[$i].PSObject.BaseObject}};$o.GetType().GetMethod($name,$f).Invoke($o,$values)}
function Static($type,$name,[object[]]$values=@()){for($i=0;$i -lt $values.Count;$i++){if($null -ne $values[$i]){$values[$i]=$values[$i].PSObject.BaseObject}};$a.GetType('AOLauncher.'+$type).GetMethod($name,$s).Invoke($null,$values)}
function Dialog($settings,$library,$preview=$true){[Activator]::CreateInstance($a.GetType('AOLauncher.GuiManagerDialog'),$f,$null,[object[]]@($settings.PSObject.BaseObject,[string]$library,[bool]$preview),$null)}
function Control($dialog,$name){$found=$dialog.Controls.Find($name,$true);if($found.Count -ne 1){throw "Expected one control $name, found $($found.Count)"};return $found[0]}
function Children($parent){foreach($child in $parent.Controls){$child;Children $child}}
function Pump{for($i=0;$i -lt 3;$i++){[Windows.Forms.Application]::DoEvents()}}
function Show($dialog){$dialog.ShowInTaskbar=$false;$dialog.StartPosition='Manual';$dialog.Location=[Drawing.Point]::new(-18000,-18000);$dialog.Show();Pump}
function TextFile($path,$value){[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))|Out-Null;[IO.File]::WriteAllText($path,$value,[Text.UTF8Encoding]::new($false))}
function Snapshot($dialog,$name){
 $bitmap=[Drawing.Bitmap]::new($dialog.ClientSize.Width,$dialog.ClientSize.Height);$graphics=[Drawing.Graphics]::FromImage($bitmap);$dc=$graphics.GetHdc()
 try{if(![GameGuiUsabilityCapture]::PrintWindow($dialog.Handle,$dc,1)){throw 'PrintWindow failed'}}finally{$graphics.ReleaseHdc($dc);$graphics.Dispose()}
 try{$bitmap.Save((Join-Path $PreviewDirectory ($name+'.png')),[Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}
}
function ButtonFill($button){$bitmap=[Drawing.Bitmap]::new($button.Width,$button.Height);try{$button.DrawToBitmap($bitmap,[Drawing.Rectangle]::new(0,0,$bitmap.Width,$bitmap.Height));return $bitmap.GetPixel(2,2).ToArgb()}finally{$bitmap.Dispose()}}
function CheckVisibleActions($dialog,$page,$context){
 foreach($control in @(Children $page | Where-Object { $_.Visible -and ($_ -is [Windows.Forms.Button] -or $_ -is [Windows.Forms.ComboBox]) })){
  $rect=$page.RectangleToClient($control.RectangleToScreen($control.ClientRectangle))
  Assert ($rect.Width -gt 0 -and $rect.Height -gt 0) "$context $($control.Name) has no usable area"
  Assert ($rect.Left -ge -1 -and $rect.Top -ge -1 -and $rect.Right -le $page.ClientSize.Width+1 -and $rect.Bottom -le $page.ClientSize.Height+1) "$context $($control.Name) is clipped by page ($rect / $($page.ClientSize))"
  $formRect=$dialog.RectangleToClient($control.RectangleToScreen($control.ClientRectangle))
  Assert ($formRect.Left -ge -1 -and $formRect.Top -ge -1 -and $formRect.Right -le $dialog.ClientSize.Width+1 -and $formRect.Bottom -le $dialog.ClientSize.Height+1) "$context $($control.Name) is outside the actual window ($formRect / $($dialog.ClientSize))"
  if($control -is [Windows.Forms.Button]){
   $preferred=[Windows.Forms.TextRenderer]::MeasureText($control.Text,$control.Font)
   Assert ($control.ClientSize.Width -ge $preferred.Width+8) "$context $($control.Name) clips its action label"
  }
 }
 foreach($name in @('guiTemplates','guiTargets','guiScripts','guiScriptPreview','guiMacros','guiMacroTargets','guiBackups','guiBackupPreview')){
  $control=Control $dialog $name
  if($control.Visible){Assert ($control.ClientSize.Height -ge 100) "$context $name needs a usable list or preview height (actual $($control.ClientSize.Height))"}
 }
 foreach($name in @('guiScriptInfo','guiScriptNextStep','guiMacroInfo','guiMacroNextStep')){
  $control=Control $dialog $name
  if($control.Visible){Assert ($control.Width -ge [Math]::Min(200,$control.Parent.ClientSize.Width-24)) "$context $name wraps inside an unexpectedly narrow width ($($control.Width))"}
 }
 $close=Field $dialog CancelAction;$closeRect=$dialog.RectangleToClient($close.RectangleToScreen($close.ClientRectangle))
 Assert ($closeRect.Bottom -le $dialog.ClientSize.Height) "$context Close remains reachable"
}
function DumpLayout($dialog,$page,$name){
 $rows=@(Children $page | Where-Object {$_.Visible -and ($_ -is [Windows.Forms.TableLayoutPanel] -or $_.Name -like 'gui*')} | ForEach-Object {
  $control=$_;$row=[ordered]@{Type=$control.GetType().Name;Name=$control.Name;Bounds=$control.Bounds.ToString();ParentType=$control.Parent.GetType().Name;ParentName=$control.Parent.Name;ParentBounds=$control.Parent.Bounds.ToString();AutoSize=$control.AutoSize;Dock=$control.Dock.ToString();Minimum=$control.MinimumSize.ToString();Maximum=$control.MaximumSize.ToString();Preferred=$control.GetPreferredSize([Drawing.Size]::new($control.Parent.ClientSize.Width,0)).ToString()}
  if($control -is [Windows.Forms.TableLayoutPanel]){$row.RowHeights=$control.GetRowHeights();$row.RowStyles=@($control.RowStyles|ForEach-Object {$_.SizeType.ToString()+':'+$_.Height});$row.ColumnWidths=$control.GetColumnWidths()}
  [pscustomobject]$row
 })
 $rows|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $PreviewDirectory ($name+'.json')) -Encoding UTF8
}
function BE([uint32]$value){$bytes=[BitConverter]::GetBytes($value);[Array]::Reverse($bytes);return ,$bytes}
function MacroFile($path,$name){$bytes=[Collections.Generic.List[byte]]::new();$bytes.AddRange([byte[]](BE 1));$bytes.AddRange([byte[]](BE 1));foreach($text in @('Assist',('/assist '+$name))){$textBytes=[Text.Encoding]::ASCII.GetBytes($text);$bytes.AddRange([byte[]](BE $textBytes.Length));$bytes.AddRange($textBytes)};[IO.File]::WriteAllBytes($path,$bytes.ToArray())}
$root=Join-Path ([IO.Path]::GetTempPath()) ('AONexus-GuiUsability-'+[guid]::NewGuid().ToString('N'));$library=Join-Path $root 'Library'
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PreviewDirectory))|Out-Null
$json=[Web.Script.Serialization.JavaScriptSerializer]::new()
try{
 $settings=Model LauncherSettings
 for($i=0;$i -lt 3;$i++){
  $installation=[Math]::Min($i,1);$account=Model AccountConfig;$account.Label=@('Main team','Second team','Weekend team')[$i];$account.Username='synthetic'+$i
  $account.ServerProfileId='rebirth';$server=@($settings.LoginServers|Where-Object Id -eq 'rebirth')[0];$account.Server=$server.Server;$account.Port=$server.Port;$account.GamePath=Join-Path $root ('Game'+$installation)
  $account.PrefsPath=Join-Path $root ('Installation'+$installation+'/Prefs/example'+$i)
  TextFile (Join-Path ([IO.Path]::GetDirectoryName($account.PrefsPath)) 'Prefs.xml') '<Root><Value name="GUIName" value="&quot;Default&quot;"/></Root>'
  for($j=0;$j -lt 2;$j++){$character=Model CharacterConfig;$character.Name=@('Aster','Orion','Nova','Luna','Atlas','Lyra')[$i*2+$j];$character.CharacterId=100+$i*10+$j;$character.Selected=($j -eq 0);$account.Characters.Add($character)
   $characterPath=Join-Path $account.PrefsPath ('Char'+$character.CharacterId)
   TextFile (Join-Path $characterPath 'Prefs.xml') ('<Root><Value name="NumHotbars" value="'+($j+1)+'"/><Value name="PetName1" value="Keep my pet"/></Root>')
   MacroFile (Join-Path $characterPath 'TextMacro.bin') $character.Name
  }
  $settings.Accounts.Add($account)
 }
 $source=Join-Path $settings.Accounts[0].PrefsPath 'Char100';$target=Join-Path $settings.Accounts[0].PrefsPath 'Char101'
 $template=Static GuiLayouts Capture @($library,'Team windows',$source,'Aster','Synthetic fixture')
 Static GuiLayouts Apply @($library,$template.Id,$target)|Out-Null
 $prefs0=Join-Path $root 'Installation0/Prefs';$prefs1=Join-Path $root 'Installation1/Prefs'
 TextFile (Join-Path $root 'Installation0/Gui/Synthetic/Graphics.uvga') 'synthetic test texture marker'
 TextFile (Join-Path $root 'Installation0/Gui/Synthetic/Graphics.uvgi') 'synthetic test index marker'
 TextFile (Join-Path $root 'Installation0/scripts/assist') '/assist Aster'
 TextFile (Join-Path $root 'Installation0/scripts/greet') '/say Hello, Rubi-Ka!'
 TextFile (Join-Path $root 'Installation1/scripts/assist') '/assist Nova'
 Static GuiScripts Copy @($prefs0,[string[]]@('assist'),$prefs1,(Join-Path $library 'ScriptBackups'))|Out-Null
 Static GuiSkins SelectSkin @($prefs0,'Default',(Join-Path $library 'SkinBackups'))|Out-Null
 $before=$json.Serialize($settings)
 foreach($language in @('en','da')){
  Static Localization.Texts SetLanguage @($language)|Out-Null
  foreach($scale in @(1,1.5)){
   foreach($size in @('default','minimum')){
    $dialog=Dialog $settings $library
    try{
     if($size -eq 'minimum'){$dialog.Size=$dialog.MinimumSize}
     if($scale -ne 1){$dialog.Font=[Drawing.Font]::new($dialog.Font.FontFamily,$dialog.Font.Size*$scale);$dialog.Scale([Drawing.SizeF]::new($scale,$scale))}
     Show $dialog;$tabs=Field $dialog tabs;$context="$language $scale $size"
     Assert ($tabs.TabCount -eq 4) "$context has four understandable sections"
     Assert (@(Call $dialog SelectedCharacters).Count -eq 0) "$context GUI recipients start independent from login selection"
     Assert (!(Control $dialog applyGuiLayout).Enabled) "$context Apply requires recipients"
     Assert ((ButtonFill (Control $dialog applyGuiLayout)) -eq $a.GetType('AOLauncher.Theme').GetField('Surface',$s).GetValue($null).ToArgb()) "$context disabled primary action visibly uses the muted surface"
     (Field $dialog templates).SelectedIndex=1
     Call $dialog SetAllTargets @($true)|Out-Null
     Assert ((Control $dialog applyGuiLayout).Enabled) "$context selecting a layout and recipients enables Apply"
     Assert ((ButtonFill (Control $dialog applyGuiLayout)) -eq (Control $dialog applyGuiLayout).BackColor.ToArgb()) "$context enabled primary action regains its action colour"
     $component=Control $dialog applyGuiComponentLayout;$component.Checked=$false
     Assert (!(Control $dialog applyGuiLayout).Enabled) "$context Apply requires at least one included component"
     $component.Checked=$true
     Assert ($json.Serialize($settings) -ceq $before) "$context UI selection cannot mutate login selections or saved settings"
     for($pageIndex=0;$pageIndex -lt $tabs.TabCount;$pageIndex++){
      $tabs.SelectedIndex=$pageIndex;Pump;$page=$tabs.TabPages[$pageIndex]
      if($pageIndex -eq 1){
       Assert (!(Control $dialog applyGuiSkin).Enabled -and !(Control $dialog defaultGuiSkin).Enabled) "$context already active Default skin does not offer a no-op change"
       (Field $dialog skins).SelectedItem='Synthetic';Pump
       Assert ((Control $dialog applyGuiSkin).Enabled) "$context choosing a different installed skin enables review"
       (Field $dialog skins).SelectedItem='Default';Pump
       Assert (!(Control $dialog applyGuiSkin).Enabled) "$context returning to the active skin disables Apply again"
      }
      Snapshot $dialog "$language-$scale-$size-tab$pageIndex"
      if($language -eq 'en' -and $scale -eq 1 -and $size -eq 'default'){DumpLayout $dialog $page "layout-dump-tab$pageIndex"}
      CheckVisibleActions $dialog $page "$context tab $pageIndex"
     }
     $tabs.SelectedIndex=2;Pump
     $scripts=Field $dialog scriptList;$scripts.ClearSelected();Pump
     Assert (!(Control $dialog copyGuiScripts).Enabled -and !(Control $dialog deleteGuiScripts).Enabled) "$context script changes require a script selection"
     $scripts.SetSelected(0,$true);(Field $dialog scriptCopyTarget).SelectedIndex=(Field $dialog scriptTarget).SelectedIndex;Pump
     Assert (!(Control $dialog copyGuiScripts).Enabled) "$context cannot copy scripts to the source installation"
     (Field $dialog scriptCopyTarget).SelectedIndex=1;Pump
     Assert ((Control $dialog copyGuiScripts).Enabled) "$context different destination enables script copy"
     $modes=Control $dialog guiScriptModes;$modes.SelectedIndex=1;Pump
     Assert (!(Control $dialog copyGuiMacros).Enabled) "$context macro copy requires its own recipients"
     $macroTargets=Control $dialog guiMacroTargets
     $macroLeaves=@($macroTargets.Nodes | ForEach-Object {$_.Nodes})
     Assert ($macroLeaves.Count -eq 5) "$context macro source is excluded from destinations"
     $macroLeaves[0].Checked=$true;Pump
     Assert ((Control $dialog copyGuiMacros).Enabled) "$context selected macro recipient enables review"
     Assert (@(Call $dialog SelectedCharacters).Count -eq 6 -and @(Call $dialog SelectedMacroTargets).Count -eq 1) "$context macro and layout recipients stay independent"
     CheckVisibleActions $dialog $modes.TabPages[1] "$context macros"
     Snapshot $dialog "$language-$scale-$size-macros"
     (Field $dialog macroSource).SelectedIndex=1;Pump
     Assert (@(Call $dialog SelectedMacroTargets).Count -eq 0 -and !(Control $dialog copyGuiMacros).Enabled) "$context changing macro source removes self-copy from the retained recipients"
     Assert (@(Call $dialog SelectedCharacters).Count -eq 6) "$context changing macro source never changes layout recipients"
     $tabs.SelectedIndex=3;Pump
     Assert ((Field $dialog backups).Items.Count -eq 3) "$context recovery includes layout, skin and script history"
     Assert ((Control $dialog restoreGuiBackup).Enabled) "$context selected recovery record has a restore action"
     $filter=Control $dialog guiBackupFilter
     foreach($kind in @(1,2,3)){$filter.SelectedIndex=$kind;Pump;Assert ((Field $dialog backups).Items.Count -eq 1) "$context recovery filter $kind shows only its kind";Assert ((Control $dialog guiBackupPreview).Text.Length -gt 40) "$context recovery filter $kind explains what will be restored"}
     $filter.SelectedIndex=0;Pump
     Call $dialog SetBusy @($true)|Out-Null
     Assert (!(Control $dialog restoreGuiBackup).Enabled -and !(Control $dialog applyGuiLayout).Enabled -and !(Control $dialog copyGuiScripts).Enabled) "$context running operation locks competing writes"
     Assert ((Field $dialog CancelAction).Enabled) "$context operation cancellation remains available"
     Call $dialog SetBusy @($false)|Out-Null
     Assert ($json.Serialize($settings) -ceq $before) "$context browsing every section preserves profile"
    }finally{$dialog.Dispose()}
   }
  }
  $empty=Model LauncherSettings;$emptyLibrary=Join-Path $root ('Empty-'+$language);$dialog=Dialog $empty $emptyLibrary
  try{
   Show $dialog;$tabs=Field $dialog tabs
   Assert (!(Control $dialog applyGuiLayout).Enabled -and !(Control $dialog applyGuiSkin).Enabled -and !(Control $dialog copyGuiScripts).Enabled -and !(Control $dialog restoreGuiBackup).Enabled) "$language empty state never offers a write without a target"
   Assert (!(Control $dialog captureGuiLayout).Enabled) "$language an empty account list cannot start character capture"
   Assert ((Control $dialog guiLayoutTargetsEmpty).Visible -and (Control $dialog guiLayoutTargetsEmpty).Text.Length -gt 30) "$language empty character list explains how to get started"
   for($pageIndex=0;$pageIndex -lt $tabs.TabCount;$pageIndex++){$tabs.SelectedIndex=$pageIndex;Pump;CheckVisibleActions $dialog $tabs.TabPages[$pageIndex] "$language empty tab $pageIndex";Snapshot $dialog "$language-empty-tab$pageIndex"}
  }finally{$dialog.Dispose()}
 }
 $dialog=Dialog $settings $library $false
 try{
  $property=$dialog.GetType().GetProperty('ExecuteOperation',$f);$callback=[GameGuiUsabilityCapture].GetMethod('Execute').MakeGenericMethod($a.GetType('AOLauncher.GuiCharacterChoice'))
  $property.SetValue($dialog,[Delegate]::CreateDelegate($property.PropertyType,$callback));Show $dialog
  Call $dialog SetAllTargets @($true)|Out-Null
  $macroTargets=Control $dialog guiMacroTargets;$macroTargets.Nodes[0].Nodes[0].Checked=$true
  $macroPath=Join-Path $target 'TextMacro.bin';$macroBefore=[Convert]::ToBase64String([IO.File]::ReadAllBytes($macroPath));$sourceMacroBefore=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $source 'TextMacro.bin')))
  $prefsBefore=[IO.File]::ReadAllText((Join-Path $target 'Prefs.xml'));$otherPath=Join-Path $settings.Accounts[1].PrefsPath 'Char110/TextMacro.bin';$otherBefore=[Convert]::ToBase64String([IO.File]::ReadAllBytes($otherPath))
  [GameGuiUsabilityCapture]::Decline=$true;$task=Call $dialog CopyMacroSelection;$task.GetAwaiter().GetResult()|Out-Null
  Assert ([GameGuiUsabilityCapture]::Targets -eq 2 -and ![GameGuiUsabilityCapture]::Installation) 'Macro confirmation lifecycle includes source and selected recipient only'
  Assert ([Convert]::ToBase64String([IO.File]::ReadAllBytes($macroPath)) -eq $macroBefore) 'Declining macro review leaves saved macros unchanged'
  [GameGuiUsabilityCapture]::Decline=$false;$task=Call $dialog CopyMacroSelection;$task.GetAwaiter().GetResult()|Out-Null
  Assert ([Convert]::ToBase64String([IO.File]::ReadAllBytes($macroPath)) -eq [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $source 'TextMacro.bin')))) 'Reviewed macro copy transfers the source macro set'
  Assert ([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $source 'TextMacro.bin'))) -eq $sourceMacroBefore) 'Macro copy never alters the source macro set'
  Assert ([IO.File]::ReadAllText((Join-Path $target 'Prefs.xml')) -ceq $prefsBefore) 'Macro-only workflow preserves the recipient window layout and other settings'
  Assert ([Convert]::ToBase64String([IO.File]::ReadAllBytes($otherPath)) -eq $otherBefore) 'Layout recipients do not leak into independent macro workflow'
  Assert (@(Static GuiLayouts ListTemplates @($library)).Count -eq 1) 'Temporary macro template is removed after operation'
  Assert (@(Static GuiLayouts ListBackups @($library,$target) | Where-Object {[int]$_.Components -eq 16}).Count -eq 1) 'Macro operation retains a recoverable original macro set'
  Assert ($json.Serialize($settings) -ceq $before) 'Reviewed macro operation preserves account selections and preset settings'
  (Field $dialog tabs).SelectedIndex=2;(Control $dialog guiScriptModes).SelectedIndex=0;Pump
  TextFile (Join-Path $root 'Installation0/scripts/newfile') '/say added outside Nexus'
  (Control $dialog refreshGuiScripts).PerformClick();Pump
  Assert ((Field $dialog scriptList).Items.Count -eq 3) 'Refresh files discovers a script added outside Nexus without reopening the manager'
  (Control $dialog guiScriptModes).SelectedIndex=1;Pump
  MacroFile (Join-Path $source 'TextMacro.bin') 'UpdatedAster'
  (Control $dialog refreshGuiMacros).PerformClick();Pump
  Assert ((Field $dialog macroList).Items[0].ToString().Contains('UpdatedAster')) 'Refresh macros rereads the character saved macro set'
 }finally{$dialog.Dispose()}
 if($failures.Count){$failures|Set-Content -LiteralPath (Join-Path $PreviewDirectory 'layout-failures.txt');throw "Diagnostic capture completed with $($failures.Count) failed assertions; see layout-failures.txt. No passing result claimed."}
 [IO.File]::WriteAllText((Join-Path $PreviewDirectory 'layout-failures.txt'),'No failed assertions in the latest run.')
 Write-Output "PASS: $checks usability, action gating, profile isolation, empty-state and EN/DA layout checks. Screenshots: $PreviewDirectory. Synthetic files only."
}finally{
 $full=[IO.Path]::GetFullPath($root);$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$full.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($full).StartsWith('AONexus-GuiUsability-')){throw 'Unexpected cleanup path'}
 if(Test-Path -LiteralPath $full){Remove-Item -LiteralPath $full -Recurse -Force}
}
