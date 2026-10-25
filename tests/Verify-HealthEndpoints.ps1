param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference='Stop'
if([IntPtr]::Size -ne 4){throw 'Use x86 PowerShell -STA'}
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions
[Windows.Forms.Application]::EnableVisualStyles()
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$f=[Reflection.BindingFlags]'Public,NonPublic,Instance';$s=[Reflection.BindingFlags]'Public,NonPublic,Static';$checks=0
function Assert($ok,$message){if(!$ok){throw $message};$script:checks++}
function Field($o,$n){$o.GetType().GetField($n,$f).GetValue($o)}
function Call($o,$n,[object[]]$argsList=@()){$o.GetType().GetMethod($n,$f).Invoke($o,$argsList)}
function Static($type,$n,[object[]]$argsList=@()){for($i=0;$i -lt $argsList.Length;$i++){if($null -ne $argsList[$i]){$argsList[$i]=$argsList[$i].psobject.BaseObject}};$a.GetType('AOLauncher.'+$type).GetMethod($n,$s).Invoke($null,$argsList)}
function New-Model($n){[Activator]::CreateInstance($a.GetType('AOLauncher.'+$n),$true)}
function Dialog($n,[object[]]$argsList){[Activator]::CreateInstance($a.GetType('AOLauncher.'+$n),$f,$null,$argsList,$null)}
function Show-Fixture($o){$o.ShowInTaskbar=$false;$o.StartPosition='Manual';$o.Location=[Drawing.Point]::new(-16000,-16000);$o.Show();[Windows.Forms.Application]::DoEvents()}
$json=New-Object Web.Script.Serialization.JavaScriptSerializer
$settings=New-Model 'LauncherSettings';$rebirth=$settings.LoginServers[0]
$defaults=Static 'HealthEndpoints' 'Defaults' @($rebirth)
Assert ($defaults.Zone.Port -eq 7501 -and $defaults.Chat.Port -eq 7012) 'Rebirth has its own defaults'
$prk=Static 'HealthEndpoints' 'Defaults' @(($settings.LoginServers | Where-Object Id -eq 'prk'))
Assert (!$prk.Zone.Enabled -and !$prk.Chat.Enabled -and $prk.Login.Port -eq 7000) 'PRK never inherits unverified Rebirth listeners'
$live=Static 'HealthEndpoints' 'Defaults' @(($settings.LoginServers | Where-Object Id -eq 'live-2019'))
Assert (!$live.Zone.Enabled -and $live.Chat.Port -eq 7106 -and $live.Chat.Host -eq 'chat.d1.funcom.com') 'RK2019 has its own chat port'
$settings.HealthCheckEndpoints.Add($rebirth.Id,$defaults)
$draft=Static 'HealthEndpoints' 'Clone' @($settings.HealthCheckEndpoints)
$draft[$rebirth.Id].Login.Host='localhost'
Assert ($settings.HealthCheckEndpoints[$rebirth.Id].Login.Host -eq '2.24.96.30') 'Endpoint edits are deep copies'
$before=$json.Serialize($settings)
foreach($language in @('en','da')){
    Static 'Localization.Texts' 'SetLanguage' @($language)
    foreach($scale in @(1.0,1.5)){
        $editor=Dialog 'HealthEndpointsDialog' @($settings.LoginServers,$settings.HealthCheckEndpoints)
        try {
            if($scale -ne 1){$editor.Scale([Drawing.SizeF]::new($scale,$scale))}
            Show-Fixture $editor;$grid=Field $editor 'grid'
            Assert ($grid.Rows.Count -eq 15) 'Three editable connection rows per server'
            $grid.Rows[0].Cells[3].Value='https://wrong.example/path'
            Assert (!(Call $editor 'TrySave')) 'URL cannot be used as a TCP host'
            $grid.Rows[0].Cells[3].Value='localhost';$grid.Rows[0].Cells[4].Value='65536'
            Assert (!(Call $editor 'TrySave')) 'Port bounds are enforced'
            $grid.Rows[0].Cells[4].Value='7509'
            Assert (Call $editor 'TrySave') 'Valid local endpoint can be saved'
            $saved=$editor.Endpoints
            Assert ($saved['rebirth'].Login.Host -eq 'localhost' -and $saved['rebirth'].Login.Port -eq 7509) 'Saved draft includes new host and port'
            Assert (!$saved.ContainsKey('prk')) 'Unchanged defaults remain automatic'
            Assert ($json.Serialize($settings) -ceq $before) 'Dialog save does not mutate caller before Settings save'
        }finally{$editor.Dispose()}
    }
}
$settings.HealthCheckEndpoints=$saved
$copy=$json.Deserialize($json.Serialize($settings),$settings.GetType())
Assert ($copy.HealthCheckEndpoints['rebirth'].Login.Port -eq 7509) 'Overrides survive profile serialization'
Assert ($copy.LoginServers[0].Port -eq 7500) 'Monitoring never changes login configuration'
# Probe a local listener through the production configured dispatch, without HTTP or game traffic.
$custom=New-Model 'LoginServerProfile';$custom.Server='127.0.0.1';$custom.Port=1
$endpoints=New-Model 'HealthEndpoints'
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start()
try {
    $endpoints.Chat.Enabled=$true;$endpoints.Chat.Host='127.0.0.1';$endpoints.Chat.Port=$listener.LocalEndpoint.Port
    $task=Static 'ServerHealth' 'CheckConfiguredAsync' @($custom,$endpoints,[Threading.CancellationToken]::None)
    if(!$listener.Pending()){Start-Sleep -Milliseconds 100}
    Assert ($listener.Pending()) 'Configured chat check reaches chosen port'
    $peer=$listener.AcceptTcpClient()
    try {$results=$task.GetAwaiter().GetResult();Assert (!$peer.GetStream().DataAvailable) 'Configured TCP check sends no payload'}finally{$peer.Dispose()}
    Assert (($results | Where-Object Key -eq 'chat').State.ToString() -eq 'Reachable') 'Chosen chat endpoint is reachable'
    Assert (($results | Where-Object Key -eq 'login').State.ToString() -eq 'Unknown') 'Disabled login check does not probe'
}finally{$listener.Stop()}
# Real dropdown regression: background painting must not dispose cards or close either picker.
Static 'Localization.Texts' 'SetLanguage' @('en')
$main=Dialog 'MainForm' @($true)
try {
    Show-Fixture $main;$config=Field $main 'settings';$config.HealthServerIds.Clear();$config.HealthServerIds.Add('rebirth')
    Call $main 'ConfigureSidebar';$sidebar=Field $main 'serverSidebar';$tabs=$sidebar.GetType().GetProperty('Tabs',$f).GetValue($sidebar);$tabs.SelectedIndex=1
    [Windows.Forms.Application]::DoEvents()
    $health=$sidebar.GetType().GetProperty('Health',$f).GetValue($sidebar);$picker=Field $health 'picker'
    Assert (!$picker.Visible -and (Field $health 'serverName').Visible) 'Single server uses a plain name'
    Assert ($health.Controls.Find('healthRefresh',$true).Count -eq 0) 'No manual health refresh control'
    Assert ($null -eq $health.GetType().GetField('guide',$f)) 'Health footer paragraphs are absent'
    $config.HealthServerIds.Add('live');Call $main 'ConfigureSidebar';$tabs=$sidebar.GetType().GetProperty('Tabs',$f).GetValue($sidebar);$tabs.SelectedIndex=1
    [Windows.Forms.Application]::DoEvents()
    Assert ($picker.Visible -and !(Field $health 'serverName').Visible) 'Multiple servers retain picker'
    $picker.DroppedDown=$true;[Windows.Forms.Application]::DoEvents()
    $card=(Field $health 'cards').Controls[0]
    Call $health 'Render';Call $health 'UpdateStatus'
    Assert ($picker.DroppedDown -and !$card.IsDisposed -and (Field $health 'deferredRender')) 'Health refresh preserves an open server dropdown'
    $picker.DroppedDown=$false;Call $health 'Render'
    Assert ($card.IsDisposed -and !(Field $health 'deferredRender')) 'Pending health render applies after dropdown closes'
    $other=[Windows.Forms.ComboBox]::new();$other.Items.Add('Example')|Out-Null;$main.Controls.Add($other);$other.BringToFront();$other.DroppedDown=$true
    Call $health 'Render'
    $news=Field $main 'newsPanel';Call $news 'Render'
    Assert ($other.DroppedDown -and (Field $health 'deferredRender') -and (Field $news 'deferredRender')) 'Health and news defer for dropdowns elsewhere in app'
    $other.DroppedDown=$false;$other.Dispose()
}finally{$main.Dispose()}
Write-Output "PASS: $checks health endpoint and dropdown checks. Synthetic data and local sockets only."
