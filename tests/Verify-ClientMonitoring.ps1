param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../src/Launcher/bin/Release/AONexus.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions,System.IO.Compression,System.IO.Compression.FileSystem
[Windows.Forms.Application]::EnableVisualStyles()
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$f=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$checks=0
function Assert($ok,$why){if(!$ok){throw $why};$script:checks++}
function New($name){[Activator]::CreateInstance($a.GetType('AOLauncher.'+$name),$true)}
function Call($obj,$method,$values=@()){ $obj.GetType().GetMethod($method,$f).Invoke($obj,[object[]]$values) }
function Static($name,$method,$values=@()){
 $argsForCall=New-Object object[] $values.Count
 for($i=0;$i -lt $values.Count;$i++){if($null -ne $values[$i]){$argsForCall[$i]=$values[$i].PSObject.BaseObject}}
 try{$a.GetType('AOLauncher.'+$name).GetMethod($method,$f).Invoke($null,$argsForCall)}catch{throw "$name.$method : $_"}
}
function SetField($obj,$name,$value){$obj.GetType().GetField($name,$f).SetValue($obj,$value)}
function Field($obj,$name){,$obj.GetType().GetField($name,$f).GetValue($obj)}
function Show($form){$form.ShowInTaskbar=$false;$form.StartPosition='Manual';$form.Location=[Drawing.Point]::new(-18000,-18000);$form.Show();[Windows.Forms.Application]::DoEvents()}
$frequency=[Diagnostics.Stopwatch]::Frequency;$now=[Diagnostics.Stopwatch]::GetTimestamp()
$pulse=New ClientPulse;$pulse.Version=1;$pulse.Active=$true;$pulse.PublishedQpc=$now;$pulse.LastUpdateQpc=$now
$ping=New ClientPing;$ping.PublishedQpc=$now;$ping.LastReplyQpc=$now;$ping.LastMs=12
function State($p,$r,$time=$now,$first=$now){(Static ClientMonitor Evaluate @($p,$r,[long]$time,[long]$first)).ToString()}
Assert ((State $null $null) -eq 'Unmeasured') 'Missing plugin must never be healthy'
Assert ((State $pulse $null) -eq 'Waiting') 'First pulse waits for independent IPC reply'
Assert ((State $pulse $ping) -eq 'Healthy') 'Fresh ticks and independent reply are healthy'
$pulse.RecentMaxMs=150;Assert ((State $pulse $ping) -eq 'Delayed') 'A recent long gap must remain visible even with fresh ticks'
$pulse.RecentMaxMs=0;$ping.LastMs=120;Assert ((State $pulse $ping) -eq 'Delayed') 'Slow IPC is shown independently'
$ping.LastMs=12;$pulse.LastUpdateQpc=$now-3*$frequency;Assert ((State $pulse $ping) -eq 'NoResponse') 'Fresh worker heartbeat cannot hide frozen Game.OnUpdate'
$pulse.LastUpdateQpc=$now-4*$frequency;$pulse.PublishedQpc=$now-4*$frequency;Assert ((State $pulse $ping) -eq 'MeasurementUnavailable') 'Stale file is an observer problem, not proof of a frozen game'
$pulse.LastUpdateQpc=$now-[long](1.5*$frequency);$pulse.PublishedQpc=$pulse.LastUpdateQpc;Assert ((State $pulse $ping) -eq 'Healthy') 'Snapshot delivery age alone must not invent a game update gap'
$pulse.LastUpdateQpc=$now;$pulse.PublishedQpc=$now;$ping.PublishedQpc=$now-4*$frequency;Assert ((State $pulse $ping $now ($now-5*$frequency)) -eq 'MeasurementUnavailable') 'Dead helper must be distinguished from missing client replies'
$ping.PublishedQpc=$now;$ping.LastReplyQpc=$now-4*$frequency;Assert ((State $pulse $ping $now ($now-5*$frequency)) -eq 'NoResponse') 'Missing reply must be detected with a live observer'
Assert ((State $pulse $ping) -eq 'Waiting') 'A previous plugin incarnation reply must not mark the new instance healthy'
$ping.LastReplyQpc=$now;Assert ((State $pulse $ping) -eq 'Healthy') 'Recovery clears current alert'
Assert ((State $pulse $null $now ($now-5*$frequency)) -eq 'MeasurementUnavailable') 'Missing helper is not proof of missing client replies'
$pulse.PublishedQpc=$now-4*$frequency;$pulse.LastUpdateQpc=$pulse.PublishedQpc;$ping.LastReplyQpc=$now-4*$frequency
Assert ((State $pulse $ping $now ($now-5*$frequency)) -eq 'NoResponse') 'Fresh helper still exposes missing IPC replies when heartbeat delivery fails'
$pulse.PublishedQpc=$now;$pulse.LastUpdateQpc=$now;$ping.LastReplyQpc=$now
$pulse.Active=$false;Assert ((State $pulse $null) -eq 'Unloaded') 'Explicit plugin teardown is distinct from an unexplained stall'
$pulse.Active=$true;$pulse.LastUpdateQpc=0;Assert ((State $pulse $ping) -eq 'Waiting') 'No game ticks cannot be healthy'
Assert ((State $pulse $ping $now ($now-5*$frequency)) -eq 'NoResponse') 'Fresh snapshots with no first update eventually expose stalled update callbacks'
Assert ((Static MainForm LimitTrayText @(('x'*80))).Length -le 63) 'Tray text respects WinForms limit'
$surrogate='x'*61+[char]0xD83D+[char]0xDE00+'more';$truncated=Static MainForm LimitTrayText @($surrogate)
Assert (![char]::IsHighSurrogate($truncated[$truncated.Length-2])) 'Truncation must not split surrogate pairs'

$settings=New LauncherSettings;$settings.HasCompletedSetup=$true;$settings.FastStartup=$false;$settings.QuietLaunch=$false;$settings.MinimizeInactiveClients=$true
Assert (!$settings.MonitorClientHealth -and !$settings.CollectClientExitReports) 'New telemetry/report connections are opt in'
Assert (Static SetupNewOptions NeedsReview @($settings)) 'Old profiles are eligible for new options only'
$serializer=[Web.Script.Serialization.JavaScriptSerializer]::new();$before=$serializer.Serialize($settings)
foreach($language in @('en','da')) {
 Static Localization.Texts SetLanguage @($language)|Out-Null
 $settings.Language=$language;$before=$serializer.Serialize($settings)
 $wizard=[Activator]::CreateInstance($a.GetType('AOLauncher.SetupWizard'),$f,$null,[object[]]@($settings,$false,$false,$true),$null)
 try {
  Show $wizard
  foreach($name in @('quietLaunch','fastStartup','minimizeInactiveClients','monitorClientHealth','collectClientExitReports')){Assert ($wizard.Controls.Find('setup-'+$name,$true).Count -eq 1) "Upgrade wizard exposes $name in $language"}
  Assert (!$wizard.Controls.Find('setup-fastStartup',$true)[0].Checked -and $wizard.Controls.Find('setup-minimizeInactiveClients',$true)[0].Checked) 'Review preserves prior choices'
  Assert ($wizard.Controls.Find('setupServer-rebirth',$true).Count -eq 0) 'Upgrade review does not replay server/account setup'
  $wizard.Controls.Find('setup-monitorClientHealth',$true)[0].Checked=$true
  $wizard.Controls.Find('setup-collectClientExitReports',$true)[0].Checked=$true
  $wizard.Controls.Find('setupNext',$true)[0].PerformClick()
  $result=$wizard.GetType().GetProperty('Result',$f).GetValue($wizard)
  Assert ($result.MonitorClientHealth -and $result.CollectClientExitReports -and $result.OptionsReviewVersion -eq 1) 'Saving records choices and review schema'
  Assert ($result.HasChosenUpdatePreference -eq $settings.HasChosenUpdatePreference) 'New-options review must not silently decide an unrelated update preference'
  Assert (!(Static SetupNewOptions NeedsReview @($result))) 'Reviewed options are not shown again'
  Assert ($serializer.Serialize($settings) -eq $before) 'Review must use an isolated draft'
 }finally{$wizard.Dispose()}
 $wizard=[Activator]::CreateInstance($a.GetType('AOLauncher.SetupWizard'),$f,$null,[object[]]@($settings,$false,$false,$true),$null)
 try{Show $wizard;$wizard.Controls.Find('setupSkip',$true)[0].PerformClick();Assert ($serializer.Serialize($settings) -eq $before) 'Later leaves choices and review marker unchanged'}finally{$wizard.Dispose()}
 $dialog=[Activator]::CreateInstance($a.GetType('AOLauncher.SettingsDialog'),$f,$null,[object[]]@($result),$null)
 try{Assert ($dialog.Controls.Find('monitorClientHealth',$true)[0].Checked -and $dialog.Controls.Find('collectClientExitReports',$true)[0].Checked) 'Settings shows persisted choices'}finally{$dialog.Dispose()}
}
$settings.OptionsReviewVersion=99;Static SetupNewOptions MarkReviewed @($settings)|Out-Null
Assert ($settings.OptionsReviewVersion -eq 99 -and !(Static SetupNewOptions NeedsReview @($settings))) 'Older binaries must not downgrade future review markers'
$settings.OptionsReviewVersion=0
$wizard=[Activator]::CreateInstance($a.GetType('AOLauncher.SetupWizard'),$f,$null,[object[]]@($settings,$false,$false,$true),$null)
try {
 $delegateType=[Func``3].MakeGenericType($settings.GetType(),[bool],[string])
 $reject=[scriptblock]{param($profile,$startup) return 'Synthetic save failure'} -as $delegateType
 $wizard.GetType().GetProperty('Commit',$f).SetValue($wizard,$reject)
 Show $wizard;$wizard.Controls.Find('setupNext',$true)[0].PerformClick()
 Assert ($wizard.Visible -and !$wizard.GetType().GetProperty('Result',$f).GetValue($wizard) -and $settings.OptionsReviewVersion -eq 0) 'Failed upgrade-review save remains open and does not mark saved profile reviewed'
}finally{$wizard.Dispose()}

$form=[Activator]::CreateInstance($a.GetType('AOLauncher.MainForm'),[object[]]@($true))
try {
 Show $form
 $label=$form.Controls.Find('clientHealth',$true)[0]
 Assert (!$label.Visible) 'No observed plugin hides header status'
 $row=New ClientObservation;SetField $row Name 'Fixture';SetField $row Alias 'Client-1';SetField $row Pid 70
 $rows=Field $form clientObservations;$rows.Add($row)
 Call $form RefreshClientStatus|Out-Null;Assert (!$label.Visible) 'Monitoring disabled with unmeasured clients stays hidden'
 (Field $form settings).MonitorClientHealth=$true
 Call $form RefreshClientStatus|Out-Null;Assert ($label.Visible -and $label.Text.Contains('not measured')) 'Enabled monitoring with no plugin explains setup instead of hiding'
 Call $form RefreshHealthTray|Out-Null
 Assert ((Field $form trayIcon).Visible -and (Field $form trayIcon).Text.Contains('not measured')) 'Tray explains enabled but unconfigured measurement'
 SetField $row Measured $true
 SetField $row Condition ([Enum]::Parse($a.GetType('AOLauncher.ClientCondition'),'Healthy'))
 Call $form RefreshClientStatus|Out-Null;Assert ($label.Visible -and $label.Text.Contains('1/1')) 'Header becomes visible on measured healthy client'
 SetField $row Condition ([Enum]::Parse($a.GetType('AOLauncher.ClientCondition'),'NoResponse'))
 Call $form RefreshClientStatus|Out-Null;Assert ($label.Visible -and $label.Text.Contains('Fixture')) 'Stalled client stays visible by name'
 SetField $row Condition ([Enum]::Parse($a.GetType('AOLauncher.ClientCondition'),'MeasurementUnavailable'))
 Call $form RefreshClientStatus|Out-Null;Assert ($label.Text.Contains('Measurement unavailable') -and $label.LinkColor -eq $a.GetType('AOLauncher.Theme').GetField('Warning',$f).GetValue($null)) 'Missing measurement stays visible and amber without accusing game of not responding'
 $output=Join-Path $PSScriptRoot '../logs/client-monitor-preview.png';$bitmap=[Drawing.Bitmap]::new($form.Width,$form.Height)
 try{$form.DrawToBitmap($bitmap,[Drawing.Rectangle]::new(0,0,$bitmap.Width,$bitmap.Height));$bitmap.Save($output)}finally{$bitmap.Dispose()}
}finally{$form.Dispose()}

$observationArray=[Array]::CreateInstance($a.GetType('AOLauncher.ClientObservation'),0)
$factoryType=[Func``1].MakeGenericType($observationArray.GetType())
$getRows=[scriptblock]{return ,$observationArray} -as $factoryType
foreach($language in @('en','da')) { foreach($scale in @(1,1.5)) {
 Static Localization.Texts SetLanguage @($language)|Out-Null
 $getText=[Func[string]]{return "Fixture A: responding | IPC 12 ms | 0 timeouts`r`nFixture B: no response | 3 timeouts"}
 $getSnapshot=[Func[string]]{return '{}'}
 $dialog=[Activator]::CreateInstance($a.GetType('AOLauncher.ClientStatusDialog'),$f,$null,[object[]]@($getText,$getSnapshot,$getRows),$null)
 try {
  if($scale -ne 1){$dialog.Font=[Drawing.Font]::new($dialog.Font.FontFamily,$dialog.Font.Size*$scale);$dialog.Scale([Drawing.SizeF]::new($scale,$scale))}
  Show $dialog
  Assert ($dialog.Controls.Find('clientMeasurements',$true)[0].Text.Contains('Fixture A')) 'Live measurements populate details dialog'
  $save=$dialog.GetType().BaseType.GetField('SaveButton',$f).GetValue($dialog)
  $bounds=$dialog.RectangleToClient($save.RectangleToScreen($save.ClientRectangle))
  Assert ($bounds.Bottom -le $dialog.ClientSize.Height) 'Diagnostic ZIP button remains accessible at both scales/languages'
  if($scale -eq 1){$bitmap=[Drawing.Bitmap]::new($dialog.Width,$dialog.Height);try{$dialog.DrawToBitmap($bitmap,[Drawing.Rectangle]::new(0,0,$bitmap.Width,$bitmap.Height));$bitmap.Save((Join-Path $PSScriptRoot ('../logs/client-details-'+$language+'.png')))}finally{$bitmap.Dispose()}}
 }finally{$dialog.Dispose()}
}}

$exit=New ClientObservation;SetField $exit Pid 70;SetField $exit Alias 'Client-1';SetField $exit StartTicks ([DateTime]::UtcNow.AddHours(-1).Ticks);SetField $exit ExitedUtc ([DateTime]::UtcNow)
$array=[Array]::CreateInstance($exit.GetType(),1);$array.SetValue($exit,0)
$xml='<Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event"><System><EventID>1000</EventID><TimeCreated SystemTime="'+[DateTime]::UtcNow.ToString('o')+'"/></System><EventData><Data Name="AppName">AnarchyOnline.exe</Data><Data Name="ProcessId">0x46</Data><Data Name="ModuleName">C:\Users\SECRET_PERSON\Gamecode.dll</Data><Data Name="AppPath">C:\SECRET_PATH</Data><Data Name="ExceptionCode">c0000005</Data></EventData></Event>'
$evidence=Static ClientReport MatchEvent @($xml,$array)
Assert ($evidence.Contains('Gamecode.dll') -and $evidence.Contains('c0000005') -and !$evidence.Contains('SECRET')) 'Windows evidence allowlists fields and removes paths'
Assert ($null -eq (Static ClientReport MatchEvent @($xml.Replace('0x46','0x47'),$array))) 'Unrelated process crash must not be attributed to this client'
Assert ($null -eq (Static ClientReport MatchEvent @($xml.Replace('AnarchyOnline.exe','Other.exe'),$array))) 'Unrelated executable must be ignored'
$wrongCreation=$xml.Replace('</EventData>','<Data Name="ProcessCreationTime">0x1</Data></EventData>')
Assert ($null -eq (Static ClientReport MatchEvent @($wrongCreation,$array))) 'A reused PID with different Windows process creation time must not match'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('NexusMonitorTest-'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($temp)|Out-Null
try {
 [IO.File]::WriteAllText((Join-Path $temp 'fixture.json'),'{"Client":"SECRET_NAME","ProcessId":70,"UnknownCredentials":"SECRET_PASSWORD","Samples":[]}')
 $zip=Join-Path $temp 'report.zip'
 Static ClientReport Export @($zip,'{"Clients":[]}',$evidence,$temp)|Out-Null
 $archive=[IO.Compression.ZipFile]::OpenRead($zip)
 try {
  Assert ($archive.Entries.Count -eq 4) 'ZIP has only README, snapshot, Windows evidence and allowlisted exit report'
  foreach($entry in $archive.Entries){$reader=[IO.StreamReader]::new($entry.Open());try{Assert (!$reader.ReadToEnd().Contains('SECRET')) 'Export must not include arbitrary saved-report fields or paths'}finally{$reader.Dispose()}}
 }finally{$archive.Dispose()}
}finally{
 $full=[IO.Path]::GetFullPath($temp);$base=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$full.StartsWith($base,[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($full).StartsWith('NexusMonitorTest-')){throw 'Unsafe cleanup path'}
 Remove-Item -LiteralPath $full -Recurse -Force
}
Write-Output "PASS: $checks client-status, timeout, recovery, privacy, export and new-options migration checks."
