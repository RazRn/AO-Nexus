param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'),[string]$PreviewDirectory=(Join-Path $PSScriptRoot '../logs/gui-manager-previews'),[string]$PublicGuideDirectory)
$ErrorActionPreference='Stop'
if([IntPtr]::Size -ne 4){throw 'Use x86 PowerShell -STA'}
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
public static class GuiManagerFixture {
    public static int Calls, Targets; public static bool Installation, Decline;
    public static Task<bool> Execute<T>(string description,List<T> targets,bool installation,Action operation) {
        Calls++; Targets=targets.Count; Installation=installation;
        if(Decline) return Task.FromResult(false);
        operation(); return Task.FromResult(true);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Msg { public IntPtr hwnd;public uint message;public IntPtr wParam,lParam;public uint time;public Point point; }
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg msg,IntPtr hwnd,uint min,uint max,uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd,IntPtr dc,uint flags);
    public static void Pump() {var timer=Stopwatch.StartNew();Msg msg=new Msg();int quiet=0;while(timer.ElapsedMilliseconds<1800&&quiet<3){if(PeekMessage(out msg,IntPtr.Zero,0,0,1)){quiet=0;TranslateMessage(ref msg);DispatchMessage(ref msg);}else{quiet++;System.Threading.Thread.Sleep(1);}}if(quiet==0)throw new Exception("Layout did not settle; last message "+msg.message+" control "+System.Windows.Forms.Control.FromHandle(msg.hwnd));}
}
'@
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$f=[Reflection.BindingFlags]'Public,NonPublic,Instance';$s=[Reflection.BindingFlags]'Public,NonPublic,Static';$checks=0
function Assert($ok,$message){if(!$ok){throw $message};$script:checks++}
function Field($o,$n){$t=$o.GetType();while($t){$field=$t.GetField($n,$f);if($field){return $field.GetValue($o)};$t=$t.BaseType};throw "Field missing: $n"}
function Call($o,$n,[object[]]$v=@()){for($i=0;$i -lt $v.Length;$i++){if($null -ne $v[$i]){$v[$i]=$v[$i].psobject.BaseObject}};$o.GetType().GetMethod($n,$f).Invoke($o,$v)}
function Static($type,$n,[object[]]$v=@()){for($i=0;$i -lt $v.Length;$i++){if($null -ne $v[$i]){$v[$i]=$v[$i].psobject.BaseObject}};$a.GetType('AOLauncher.'+$type).GetMethod($n,$s).Invoke($null,$v)}
function Model($n){[Activator]::CreateInstance($a.GetType('AOLauncher.'+$n),$true)}
function Dialog($n,[object[]]$v){for($i=0;$i -lt $v.Length;$i++){if($null -ne $v[$i]){$v[$i]=$v[$i].psobject.BaseObject}};[Activator]::CreateInstance($a.GetType('AOLauncher.'+$n),$f,$null,$v,$null)}
function Show($o){$o.ShowInTaskbar=$false;$o.StartPosition='Manual';$o.Location=[Drawing.Point]::new(-18000,-18000);$o.Show();[GuiManagerFixture]::Pump()}
function Children($o){foreach($child in $o.Controls){$child;Children $child}}
function Write-Fixture($path,$text){[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))|Out-Null;[IO.File]::WriteAllText($path,$text,[Text.UTF8Encoding]::new($false))}
function Screenshot($form,$name){$bitmap=[Drawing.Bitmap]::new($form.ClientSize.Width,$form.ClientSize.Height);$graphics=[Drawing.Graphics]::FromImage($bitmap);$dc=$graphics.GetHdc();try{if(![GuiManagerFixture]::PrintWindow($form.Handle,$dc,1)){throw 'PrintWindow failed'}}finally{$graphics.ReleaseHdc($dc);$graphics.Dispose()};try{$bitmap.Save((Join-Path $PreviewDirectory $name),[Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('AONexus-GuiManager-'+[guid]::NewGuid().ToString('N'));$library=Join-Path $root 'Library'
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PreviewDirectory))|Out-Null
$json=New-Object Web.Script.Serialization.JavaScriptSerializer
try{
    $settings=Model 'LauncherSettings'
    for($i=0;$i -lt 3;$i++){
        $account=Model 'AccountConfig';$account.Label='Example account '+$i;$account.Username='example'+$i;$account.Server='2.24.96.30';$account.Port=7500;$account.GamePath=Join-Path $root ('Game'+[Math]::Min($i,1))
        $account.PrefsPath=Join-Path $root ('Installation'+[Math]::Min($i,1)+'/Prefs/example'+$i)
        Write-Fixture (Join-Path ([IO.Path]::GetDirectoryName($account.PrefsPath)) 'Prefs.xml') '<Root><Value name="GUIName" value="&quot;Default&quot;"/></Root>'
        for($j=0;$j -lt 3;$j++){$char=Model 'CharacterConfig';$char.Name='Character '+$i+'-'+$j;$char.CharacterId=100+$i*10+$j;$char.Selected=($j -eq 0);$account.Characters.Add($char);Write-Fixture (Join-Path $account.PrefsPath ('Char'+$char.CharacterId+'/Prefs.xml')) ('<Root><Value name="NumHotbars" value="'+($j+1)+'"/><Value name="PetName1" value="Keep-my-pet"/></Root>')}
        $settings.Accounts.Add($account)
    }
    $source=Join-Path $settings.Accounts[0].PrefsPath 'Char100'
    $template=Static 'GuiLayouts' 'Capture' @($library,'Team layout',$source,'Example source','Synthetic')
    Static 'GuiLayouts' 'Apply' @($library,$template.Id,(Join-Path $settings.Accounts[0].PrefsPath 'Char101')) | Out-Null
    $settings.DefaultGuiTemplateId=$template.Id
    $before=$json.Serialize($settings)
    foreach($language in @('en','da')){
        Static 'Localization.Texts' 'SetLanguage' @($language)
        foreach($scale in @(1.0,1.5)){
            $dialog=Dialog 'GuiManagerDialog' @($settings,$library,$true)
            try{
                if($scale -gt 1){$dialog.Scale([Drawing.SizeF]::new($scale,$scale))}
                Write-Output ($language+' '+$scale);Show $dialog;$tabs=Field $dialog 'tabs'
                Assert ($tabs.TabCount -eq 3) 'Manager has Layouts, Skins and Backups tabs'
                Assert ((Field $dialog 'templates').Items.Count -eq 2) 'Built-in AO default and saved template are listed'
                Assert (!(Field $dialog 'defaultTemplate').Enabled) 'Built-in reset cannot become a queued personal default'
                if($PublicGuideDirectory -and $language -eq 'en' -and $scale -eq 1){
                    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PublicGuideDirectory))|Out-Null
                    $oldPreview=$PreviewDirectory;$PreviewDirectory=$PublicGuideDirectory
                    try{Screenshot $dialog 'gui-default-layout.png'}finally{$PreviewDirectory=$oldPreview}
                }
                (Field $dialog 'templates').SelectedIndex=1
                Assert ((Field $dialog 'defaultTemplate').Checked) 'Existing GUI default is shown'
                $check=Field $dialog 'defaultTemplate';Assert ($check.Width -gt [Windows.Forms.TextRenderer]::MeasureText($check.Text,$check.Font).Width+16) 'Default checkbox text has enough width'
                Assert ((Field $dialog 'skinTarget').Items.Count -eq 2) 'Shared Prefs installation is deduplicated'
                Assert (@(Call $dialog 'SelectedCharacters').Count -eq 0) 'Targets begin empty, independently of login selections'
                Call $dialog 'SetAllTargets' @($true)
                Assert (@(Call $dialog 'SelectedCharacters').Count -eq 9) 'All chars can be selected across accounts'
                Assert ($json.Serialize($settings) -ceq $before) 'GUI target checks never mutate login selections or presets'
                Assert ($dialog.Controls.Find('applyGuiLayout',$true)[0].Enabled) 'Apply enables after template and targets chosen'
                Assert ($dialog.Controls.Find('applyGuiLayout',$true)[0].Height -ge 34) 'Primary button retains standard height'
                Assert ($dialog.Controls.Find('applyGuiComponentHotbars',$true)[0].Enabled -eq $false) 'Uncaptured components cannot be selected'
                Call $dialog 'SetBusy' @($true)
                $labels=@(Children $tabs | Where-Object { $_ -is [Windows.Forms.Label] -and $_ -isnot [Windows.Forms.LinkLabel] })
                Assert (@($labels | Where-Object { !$_.Enabled -or $_.ForeColor.GetBrightness() -lt .3 }).Count -eq 0) 'Busy headings and help retain readable enabled theme colours'
                Assert (!(Field $dialog 'targets').Enabled -and !$dialog.Controls.Find('captureGuiLayout',$true)[0].Enabled) 'Busy operation locks interactive target and capture controls'
                Assert ((Field $dialog 'CancelAction').Enabled) 'Operation cancellation remains available'
                Screenshot $dialog ($language+'-'+$scale+'-busy.png')
                Call $dialog 'SetBusy' @($false)
                Assert ($dialog.Controls.Find('applyGuiLayout',$true)[0].Enabled -and !$dialog.Controls.Find('applyGuiComponentHotbars',$true)[0].Enabled) 'Idle restores controls while preserving component restrictions'
                for($i=0;$i -lt 3;$i++){
                    $tabs.SelectedIndex=$i;[GuiManagerFixture]::Pump();Assert ($tabs.GetTabRect($i).Right -le $tabs.ClientSize.Width) 'Tabs fit at normal and enlarged scale';Screenshot $dialog ($language+'-'+$scale+'-tab'+$i+'.png')
                    if($PublicGuideDirectory -and $language -eq 'en' -and $scale -eq 1){
                        [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PublicGuideDirectory))|Out-Null
                        $originalPreview=$PreviewDirectory;$PreviewDirectory=$PublicGuideDirectory
                        try{(Field $dialog 'skinInfo').Text="Current skin: Default`r`nC:\ExamplePrefs\AO-Rebirth\Prefs";(Field $dialog 'backupInfo').Text="Character 0-1 - Example account 0 / AO Rebirth`r`nC:\ExamplePrefs\AO-Rebirth\Prefs\example0\Char101`r`nApplied";Screenshot $dialog (@('gui-layouts.png','gui-skins.png','gui-backups.png')[$i])}finally{$PreviewDirectory=$originalPreview}
                    }
                }
                $tabs.SelectedIndex=0;Call $dialog 'SetAllTargets' @($false)
                Assert (!$dialog.Controls.Find('applyGuiLayout',$true)[0].Enabled) 'Empty target selection disables Apply'
                foreach($control in Children $dialog){if($control -is [Windows.Forms.TreeView]){Assert (!$control.ShowNodeToolTips) 'No tree tooltips'}}
            }finally{$dialog.Dispose()}
            $choices=[Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($a.GetType('AOLauncher.GuiCharacterChoice')))
            $choice=Model 'GuiCharacterChoice';$choice.Account=$settings.Accounts[0];$choice.Character=$settings.Accounts[0].Characters[0];$choice.CharacterPath=$source;$choice.Label='Aster - Example account / AO Rebirth';$choices.Add($choice)
            $capture=Dialog 'GuiCaptureDialog' @($choices,$false)
            try{
                if($scale -gt 1){$capture.Scale([Drawing.SizeF]::new($scale,$scale))};Show $capture
                Assert ($capture.GetType().GetProperty('Components',$f).GetValue($capture).ToString() -eq 'Layout') 'Capture defaults to layout only'
                $sourceControl=Field $capture 'source';$sourceControl.SelectedIndex=0;(Field $capture 'name').Text='Team layout'
                Assert ($capture.Controls.Find('captureGuiComponentHotbars',$true)[0].Visible) 'Optional hotbars are available'
                Screenshot $capture ($language+'-'+$scale+'-capture.png')
                if($PublicGuideDirectory -and $language -eq 'en' -and $scale -eq 1){$originalPreview=$PreviewDirectory;$PreviewDirectory=$PublicGuideDirectory;try{Screenshot $capture 'gui-capture.png'}finally{$PreviewDirectory=$originalPreview}}
            }finally{$capture.Dispose()}
        }
    }
    Static 'Localization.Texts' 'SetLanguage' @('en')
    $dialog=Dialog 'GuiManagerDialog' @($settings,$library,$false)
    try{
        $property=$dialog.GetType().GetProperty('ExecuteOperation',$f);$method=[GuiManagerFixture].GetMethod('Execute').MakeGenericMethod($a.GetType('AOLauncher.GuiCharacterChoice'))
        $property.SetValue($dialog,[Delegate]::CreateDelegate($property.PropertyType,$method));Show $dialog
        $target=Join-Path $settings.Accounts[1].PrefsPath 'Char111/Prefs.xml';$original=[IO.File]::ReadAllText($target)
        [GuiManagerFixture]::Decline=$true
        $task=Call $dialog 'ApplyTemplate' @($template.Id,$true);$task.GetAwaiter().GetResult()|Out-Null
        Assert ([GuiManagerFixture]::Targets -eq 9 -and !$([GuiManagerFixture]::Installation)) 'Rollout asks lifecycle callback about every char'
        Assert ([IO.File]::ReadAllText($target) -ceq $original) 'Declining lifecycle callback prevents writes'
        [GuiManagerFixture]::Decline=$false
        $task=Call $dialog 'ApplyTemplate' @($template.Id,$true);$task.GetAwaiter().GetResult()|Out-Null
        Assert ([IO.File]::ReadAllText($target).Contains('value="1"')) 'Confirmed rollout applies chosen template to all targets'
        Assert ([IO.File]::ReadAllText($target).Contains('Keep-my-pet')) 'Default layout application preserves pet names'
        Assert ((Field $dialog 'backups').Items.Count -eq 9) 'Each layout target receives a visible backup'
        Assert ($json.Serialize($settings) -ceq $before) 'Applying layouts preserves unrelated settings'
        # First target already matches; second changes; third fails. Only real backups may be rolled back.
        $batch=[Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($a.GetType('AOLauncher.GuiCharacterChoice')))
        foreach($item in (Field $dialog 'characters') | Select-Object -First 3){$batch.Add($item)}
        $second=Join-Path $batch[1].CharacterPath 'Prefs.xml';$third=Join-Path $batch[2].CharacterPath 'Prefs.xml'
        Write-Fixture $second '<Root><Value name="NumHotbars" value="3"/></Root>';Write-Fixture $third '<Root><Value name="NumHotbars" value="4"/></Root>'
        $secondBefore=[IO.File]::ReadAllText($second);$script:writeCount=0
        $a.GetType('AOLauncher.GuiLayouts').GetField('BeforeWrite',$s).SetValue($null,[Action[int]]{param($i)$script:writeCount++;if($script:writeCount -eq 2){throw 'Synthetic third-target failure'}})
        $failure='';try{Call $dialog 'ApplyLayoutBatch' @($template.Id,$batch,[Enum]::ToObject($a.GetType('AOLauncher.GuiLayoutComponents'),1))}catch{$failure=$_.Exception.ToString()}finally{$a.GetType('AOLauncher.GuiLayouts').GetField('BeforeWrite',$s).SetValue($null,$null)}
        Assert ($failure.Contains('Earlier targets in this batch were restored.') -and !$failure.Contains('manual backup')) 'Unchanged targets do not generate null-ID rollback errors'
        Assert ([IO.File]::ReadAllText($second) -ceq $secondBefore) 'Earlier changed target is restored after later failure'
        $callsBefore=[GuiManagerFixture]::Calls
        $task=Call $dialog 'ApplySkinName' @('Default');$task.GetAwaiter().GetResult()|Out-Null
        Assert ([GuiManagerFixture]::Calls -eq $callsBefore) 'Selecting the current skin does not relog clients'
        Write-Fixture (Join-Path $root 'Installation0/Prefs/Prefs.xml') '<Root><Value name="GUIName" value="&quot;Example&quot;"/></Root>'
        $task=Call $dialog 'ApplySkinName' @('Default');$task.GetAwaiter().GetResult()|Out-Null
        Assert ([GuiManagerFixture]::Installation -and [GuiManagerFixture]::Targets -eq 3) 'Skin activation uses installation-wide lifecycle callback'
        Write-Fixture (Join-Path $root 'Installation0/Prefs/Prefs.xml') '<Root><Value name="GUIName" value="&quot;Example&quot;"/></Root>'
        Write-Fixture (Join-Path $root 'Installation1/Prefs/Prefs.xml') '<Root><Value name="GUIName" value="&quot;Example&quot;"/></Root>'
        $task=Call $dialog 'RolloutSkin';$task.GetAwaiter().GetResult()|Out-Null
        Assert ([GuiManagerFixture]::Installation -and [GuiManagerFixture]::Targets -eq 9) 'Skin rollout covers all affected installation chars'
    }finally{$dialog.Dispose()}
    $empty=Model 'LauncherSettings';$dialog=Dialog 'GuiManagerDialog' @($empty,$library,$true)
    try{Show $dialog;Assert ((Field $dialog 'skinTarget').Items.Count -eq 0) 'Empty account list is supported';Assert (!$dialog.Controls.Find('downloadGuiSkin',$true)[0].Enabled) 'Downloads require a valid target'}finally{$dialog.Dispose()}
    $missing=Model 'AccountConfig';$missing.Label='Not initialized';$missing.Username='missing';$missing.Server='127.0.0.1';$missing.Port=7500;$missing.PrefsPath=Join-Path $root 'Missing/Prefs/account';$settings.Accounts.Add($missing)
    $dialog=Dialog 'GuiManagerDialog' @($settings,$library,$true)
    try{Show $dialog;Assert ((Field $dialog 'skinTarget').Items.Count -eq 2) 'One missing Prefs folder does not hide valid installations';Assert ((Field $dialog 'skinInfo').Text.Contains('initialized Prefs')) 'Missing Prefs has actionable guidance'}finally{$dialog.Dispose()}
}finally{
    $full=[IO.Path]::GetFullPath($root);$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$full.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($full).StartsWith('AONexus-GuiManager-')){throw 'Unexpected cleanup path'}
    if(Test-Path -LiteralPath $full){Remove-Item -LiteralPath $full -Recurse -Force}
}
Write-Output "PASS: $checks GUI manager workflow/layout/callback checks. Only synthetic temp files; no downloads, real profiles or clients touched."
