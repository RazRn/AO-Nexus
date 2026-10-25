param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'),[switch]$Live)
$ErrorActionPreference='Stop'
if([IntPtr]::Size -ne 4){throw 'Run in x86 Windows PowerShell -STA'}
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions
[Windows.Forms.Application]::EnableVisualStyles()
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$f=[Reflection.BindingFlags]'Public,NonPublic,Instance';$s=[Reflection.BindingFlags]'Public,NonPublic,Static';$checks=0
function Assert($ok,$message){if(!$ok){throw $message};$script:checks++}
function Field($o,$n){$o.GetType().GetField($n,$f).GetValue($o)}
function Call($o,$n,[object[]]$arguments=@()){$o.GetType().GetMethod($n,$f).Invoke($o,$arguments)}
function Static($type,$n,[object[]]$arguments=@()){for($i=0;$i -lt $arguments.Length;$i++){if($null -ne $arguments[$i]){$arguments[$i]=$arguments[$i].psobject.BaseObject}};$a.GetType('AOLauncher.'+$type).GetMethod($n,$s).Invoke($null,$arguments)}
function New-Model($n){[Activator]::CreateInstance($a.GetType('AOLauncher.'+$n),$true)}
function Show-Fixture($o){$o.ShowInTaskbar=$false;$o.StartPosition='Manual';$o.Location=[Drawing.Point]::new(-16000,-16000);$o.Show();[Windows.Forms.Application]::DoEvents()}
$json=New-Object Web.Script.Serialization.JavaScriptSerializer
$config=New-Model 'LauncherSettings';$server=$config.LoginServers[0];$now=[DateTime]::UtcNow
$report=@{ok=$true;stale=$false;ageSeconds=1;generatedAt=$now.ToString('o');services=@{loginEngine=$true;zoneEngine=$true;chatEngine=$true;accountBroker=$true}}
function Parse-Rebirth {Static 'ServerHealth' 'ParseReport' @('rebirth',$json.Serialize($report),$server,$now)}
Assert ((Parse-Rebirth).State.ToString() -eq 'ReportedOnline') 'Complete fresh service report is online'
$report.services.chatEngine=$false
Assert ((Parse-Rebirth).State.ToString() -eq 'ReportedIssue') 'Explicit chat outage remains an issue'
$report.services.chatEngine=$true;$report.services.Remove('zoneEngine')
Assert ((Parse-Rebirth).State.ToString() -eq 'Unknown') 'Missing component never appears healthy'
$report.services.zoneEngine=$true;$report.stale=$true
Assert ((Parse-Rebirth).State.ToString() -eq 'Unknown') 'Stale response is unknown'
$report.stale=$false;$report.ageSeconds=181
Assert ((Parse-Rebirth).State.ToString() -eq 'Unknown') 'Source-reported age is bounded'
$report.ageSeconds=1;$report.generatedAt=$now.AddMinutes(-10).ToString('o')
Assert ((Parse-Rebirth).State.ToString() -eq 'Unknown') 'Old timestamp cannot be hidden by a fresh age field'
$report.generatedAt=$now.AddMinutes(5).ToString('o')
Assert ((Parse-Rebirth).State.ToString() -eq 'Unknown') 'Future clock timestamp is not fresh'
foreach($body in @('{}','bad json','{"ok":true}',('x'*65537))){Assert ((Static 'ServerHealth' 'ParseReport' @('rebirth',$body,$server,$now)).State.ToString() -eq 'Unknown') 'Malformed, missing or oversized reports are unknown'}
$prk=$config.LoginServers | Where-Object Id -eq 'prk'
Assert ((Static 'ServerHealth' 'ParseReport' @('prk','{"data":[{"loginIp":"5.161.243.214","loginPort":7000,"count":0}]}',$prk,$now)).State.ToString() -eq 'ReportedOnline') 'PRK with zero players still reports online'
Assert ((Static 'ServerHealth' 'ParseReport' @('prk','{"data":[]}',$prk,$now)).State.ToString() -eq 'Unknown') 'Absent PRK server is not asserted offline'
$custom=New-Model 'LoginServerProfile';$custom.Id='rebirth';$custom.Name='AO Rebirth';$custom.Server='127.0.0.1';$custom.Port=7500
Assert ((Static 'ServerHealth' 'Kind' @($custom)) -eq 'custom') 'Names and IDs cannot associate custom endpoints with public status'
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port
try {
    $task=Static 'ServerHealth' 'TcpAsync' @('login','127.0.0.1',$port,[Threading.CancellationToken]::None)
    $peer=$listener.AcceptTcpClient()
    try {Assert ($task.GetAwaiter().GetResult().State.ToString() -eq 'Reachable') 'Local TCP listener is reachable';Assert (!$peer.GetStream().DataAvailable) 'Probe sends no authentication or protocol payload'}finally{$peer.Dispose()}
}finally{$listener.Stop()}
$task=Static 'ServerHealth' 'TcpAsync' @('login','127.0.0.1',$port,[Threading.CancellationToken]::None)
Assert ($task.GetAwaiter().GetResult().State.ToString() -eq 'Unreachable') 'Closed local port is unreachable'
$cts=[Threading.CancellationTokenSource]::new();$cts.Cancel();$cancelled=$false
try{$task=Static 'ServerHealth' 'TcpAsync' @('login','127.0.0.1',$port,$cts.Token);$task.GetAwaiter().GetResult()|Out-Null}catch{$cancelled=$true}finally{$cts.Dispose()}
Assert $cancelled 'Cancelled probes do not produce false outage samples'
$notes=Static 'AppUpdates' 'FormatReleaseNotes' @("# Version`n`n- **First** change`n- Second change")
Assert ($notes.Contains("`r`n`r`n") -and $notes.Contains('• First change') -and !$notes.Contains('**') -and !$notes.Contains('# Version')) 'Release notes normalize line breaks, headings and bullets'

$main=[Activator]::CreateInstance($a.GetType('AOLauncher.MainForm'),[object[]]@($true))
try {
    Show-Fixture $main;$settings=Field $main 'settings';$sidebar=Field $main 'serverSidebar';$news=Field $main 'newsPanel'
    Assert ($news.Controls.Find('newsSourceFilter',$true).Count -eq 0) 'No front-page news-source picker remains'
    $accounts=$json.Serialize($settings.Accounts);$presets=$json.Serialize($settings.Presets)
    foreach($language in @('en','da')){
        $settings.Language=$language;Call $main 'ApplyLanguage'
        foreach($mode in @(@($true,$true),@($true,$false),@($false,$true),@($false,$false))){
            $settings.ShowServerNews=$mode[0];$settings.ShowServerHealth=$mode[1];Call $main 'ConfigureSidebar';[Windows.Forms.Application]::DoEvents()
            $tabs=$sidebar.GetType().GetProperty('Tabs',$f).GetValue($sidebar)
            Assert (($null -ne $tabs) -eq ($mode[0] -and $mode[1])) 'Tabs appear exactly when both panels are enabled'
            Assert ($sidebar.Visible -eq ($mode[0] -or $mode[1])) 'Turning both off hides the sidebar'
            Assert (((Field $main 'sidebarLayout').ColumnStyles[1].Width -gt 0) -eq ($mode[0] -or $mode[1])) 'Hidden sidebar returns its width to accounts'
        }
        $dialog=[Activator]::CreateInstance($a.GetType('AOLauncher.SettingsDialog'),[object[]]@($settings))
        try{
            Show-Fixture $dialog;$tabs=Field $dialog 'settingsTabs'
            Assert ($tabs.TabCount -eq 4) 'Settings groups controls into four tabs'
            for($i=0;$i -lt 4;$i++){$tabs.SelectedIndex=$i;[Windows.Forms.Application]::DoEvents();Assert ($tabs.GetTabRect($i).Right -le $tabs.ClientSize.Width) 'Every settings tab fits without clipping'}
            $draft=Field $dialog 'sidebarOptions'
            $before=$json.Serialize($settings);$dialog.Controls.Find('showServerNews',$true)[0].Checked=$true
            $dialog.Controls.Find('newsSource-prk',$true)[0].Checked=$false
            Assert ($json.Serialize($settings) -ceq $before) 'Settings edits remain a draft until saved'
            Assert (!$draft.GetType().GetProperty('NewsIds',$f).GetValue($draft).Contains('prk')) 'Individual news source can be deselected in Settings'
        }finally{$dialog.Dispose()}
    }
    Assert ($json.Serialize($settings.Accounts) -ceq $accounts -and $json.Serialize($settings.Presets) -ceq $presets) 'Sidebar changes preserve accounts and teams'
    $settings.ShowServerNews=$false;$settings.ShowServerHealth=$false;$settings.NewsSourceIds.Clear();$settings.HealthServerIds.Clear()
    $copy=$json.Deserialize($json.Serialize($settings),$settings.GetType())
    Assert (!$copy.ShowServerNews -and !$copy.ShowServerHealth -and $copy.NewsSourceIds.Count -eq 0 -and $copy.HealthServerIds.Count -eq 0) 'Disabled panels and explicitly empty selections survive reload'
}finally{$main.Dispose()}
# Exercise the real visibility/scheduling code with an injected inert probe.
Add-Type @'
using System;
using System.Threading;
using System.Threading.Tasks;
public static class HealthProbeFixture {
    public static int Calls;
    public static object Results, Pending;
    public static bool Hang;
    public static CancellationToken Token;
    public static Task<T[]> Probe<T>(object server, CancellationToken token) {
        Calls++; Token=token;
        if (Hang) { var source=new TaskCompletionSource<T[]>(); Pending=source; return source.Task; }
        return Task.FromResult((T[])Results);
    }
}
'@
$health=[Activator]::CreateInstance($a.GetType('AOLauncher.HealthPanel'),$f,$null,[object[]]@($false),$null)
$hostForm=[Windows.Forms.Form]::new()
try {
    $health.Visible=$false;$health.Dock='Fill';$hostForm.Controls.Add($health)
    $checkField=$health.GetType().GetField('Check',$f)
    $method=[HealthProbeFixture].GetMethod('Probe').MakeGenericMethod($a.GetType('AOLauncher.HealthResult'))
    $checkField.SetValue($health,[Delegate]::CreateDelegate($checkField.FieldType,$method))
    [HealthProbeFixture]::Results=$health.GetType().GetMethod('Preview',$f).Invoke($health,[object[]]@($server))
    Call $health 'Configure' @($config.LoginServers,[string[]]@('rebirth'))
    Show-Fixture $hostForm
    Assert ([HealthProbeFixture]::Calls -eq 0) 'Hidden health panel sends no requests'
    $health.Visible=$true;[Windows.Forms.Application]::DoEvents()
    Assert ([HealthProbeFixture]::Calls -eq 1) 'Visible health panel performs its initial probe'
    $task=Call $health 'RefreshAsync' @($true);[Windows.Forms.Application]::DoEvents()
    Assert ([HealthProbeFixture]::Calls -eq 1) 'Immediate manual refresh is throttled'
    $health.Visible=$false;[Windows.Forms.Application]::DoEvents()
    Assert (!(Field $health 'timer').Enabled) 'Hiding health stops its polling timer'
    foreach($sample in [HealthProbeFixture]::Results){$sample.CheckedUtc=[DateTime]::UtcNow.AddMinutes(-3)}
    $health.GetType().GetField('lastAttempt',$f).SetValue($health,[DateTime]::MinValue)
    [HealthProbeFixture]::Hang=$true;$health.Visible=$true;[Windows.Forms.Application]::DoEvents()
    Assert ([HealthProbeFixture]::Calls -eq 2) 'Showing old samples starts a fresh probe'
    $task=Call $health 'RefreshAsync' @($true)
    Assert ([HealthProbeFixture]::Calls -eq 2) 'A pending probe cannot overlap another batch'
    $health.Visible=$false;[Windows.Forms.Application]::DoEvents()
    Assert ([HealthProbeFixture]::Token.IsCancellationRequested) 'Hiding cancels a pending probe'
    [HealthProbeFixture]::Pending.SetCanceled();[Windows.Forms.Application]::DoEvents()
    Assert (!(Field $health 'busy')) 'Cancelled check releases the busy guard'
}finally{$hostForm.Dispose()}
if($Live){
    foreach($id in @('rebirth','prk','live')){
        $target=$config.LoginServers | Where-Object Id -eq $id
        $result=(Static 'ServerHealth' 'CheckAsync' @($target,[Threading.CancellationToken]::None)).GetAwaiter().GetResult()
        Assert ($result.Count -eq 4) 'Live check completes with four bounded results'
        $result | ForEach-Object { Write-Output ($id+' '+$_.Key+': '+$_.State+' — '+$_.Detail) }
    }
}
Write-Output "PASS: $checks health parser, local TCP, source binding, settings, sidebar and release-note checks. No game authentication."
