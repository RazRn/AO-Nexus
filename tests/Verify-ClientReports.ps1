param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Web.Extensions,System.IO.Compression,System.IO.Compression.FileSystem
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$f=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$checks=0
function Assert($ok,$why){if(!$ok){throw $why};$script:checks++}
function New($name){[Activator]::CreateInstance($a.GetType('AOLauncher.'+$name),$true)}
function Static($name,$method,$values=@()){
 $argsForCall=New-Object object[] $values.Count
 for($i=0;$i -lt $values.Count;$i++){if($null -ne $values[$i]){$argsForCall[$i]=$values[$i].PSObject.BaseObject}}
 $a.GetType('AOLauncher.'+$name).GetMethod($method,$f).Invoke($null,$argsForCall)
}
$json=[Web.Script.Serialization.JavaScriptSerializer]::new();$json.MaxJsonLength=16777216
$summary=New ClientObservationSummary
$start=[DateTime]::UtcNow.AddMinutes(-5)
for($i=0;$i -lt 180;$i++){
 $s=New ClientSample;$s.Utc=$start.AddSeconds($i);$s.State='Unmeasured';$s.WorkingSetBytes=500
 if($i -eq 0){$s.WorkingSetBytes=9000;$s.PulseAgeMs=10;$s.ObservedUpdateGapMs=8000;$s.MaxGapMs=8500;$s.ReplyMs=600;$s.State='NoResponse'}
 $summary.GetType().GetMethod('Observe',$f).Invoke($summary,@($s))|Out-Null
}
Assert ($summary.TotalSamples -eq 180 -and $summary.PeakObservedWorkingSetBytes -eq 9000) 'Lifetime summary preserves early peak after recent window expires'
Assert ($summary.LongestObservedUpdateGapMs -eq 8000 -and $summary.LargestPluginReportedGapMs -eq 8500 -and $summary.LargestObservedReplyMs -eq 600) 'Early monitoring outliers survive'
Assert ($summary.StateSamples['Unmeasured'] -eq 179 -and $summary.SamplesWithPluginData -eq 1 -and $summary.FreshPluginSamples -eq 1) 'Summary separates process samples from fresh plugin observations'
Assert ($summary.LongestObserverSampleIntervalMs -eq 1000 -and $summary.FirstObservedUtc -eq $start) 'Observer interval and coverage are explicit'
Assert ($null -eq $s.Zoning -and $null -eq $s.ReplyMs) 'Missing measurements remain unknown'
$now=[Diagnostics.Stopwatch]::GetTimestamp()
$pulse=New ClientPulse;$pulse.Version=1;$pulse.Pid=123;$pulse.StartTicks=$start.Ticks;$pulse.Instance='fixture';$pulse.PublishedQpc=$now;$pulse.LastUpdateQpc=$now
function Validate{Static ClientMonitor ValidatePulse @($pulse,123,$start.Ticks,$now)}
Assert ((Validate) -eq 'SnapshotAccepted') 'Valid process-specific snapshot accepted'
$pulse.Pid=124;Assert ((Validate) -eq 'SnapshotIdentityMismatch') 'Wrong process rejected with reason';$pulse.Pid=123
$pulse.Version=2;Assert ((Validate) -eq 'SnapshotVersionUnsupported') 'Unsupported version diagnosed';$pulse.Version=1
$pulse.Instance='';Assert ((Validate) -eq 'SnapshotInvalid') 'Incomplete snapshot diagnosed';$pulse.Instance='fixture'
$pulse.PublishedQpc=$now+1;Assert ((Validate) -eq 'SnapshotInvalid') 'Future snapshot rejected';$pulse.PublishedQpc=$now
$pulse.MaxMs=[double]::NaN;Assert ((Validate) -eq 'SnapshotInvalid') 'Invalid metrics rejected'
$os=Static DiagnosticSnapshot WindowsVersion
Assert ($os.Contains('build ') -or $os.Contains('unavailable')) 'OS version uses registry build or explicit unavailable status'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('NexusReportTest-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp)|Out-Null
function ReadEntry($zip,$name){$reader=[IO.StreamReader]::new($zip.GetEntry($name).Open());try{$reader.ReadToEnd()}finally{$reader.Dispose()}}
try {
 # Exercise the production reader using this test process and an isolated snapshot folder.
 $monitor=[Activator]::CreateInstance($a.GetType('AOLauncher.ClientMonitor'),$f,$null,[object[]]@([string]$temp,[string]$temp,[string]$temp),$null)
 try {
  $process=[Diagnostics.Process]::GetCurrentProcess()
  $client=New 'PresetCamping+Client';$client.ProcessId=$process.Id;$client.StartTimeUtcTicks=$process.StartTime.ToUniversalTime().Ticks
  $tracked=New 'ClientMonitor+Tracked'
  $tracked.GetType().GetField('Client',$f).SetValue($tracked,$client)
  $tracked.GetType().GetField('Process',$f).SetValue($tracked,$process)
  $tracked.GetType().GetField('Alias',$f).SetValue($tracked,'Client-test')
  $key=$client.ProcessId.ToString()+'-'+$client.StartTimeUtcTicks
  $monitor.GetType().GetField('clients',$f).GetValue($monitor).Add($key,$tracked)
  $settings=New LauncherSettings;$settings.MonitorClientHealth=$true;$settings.CollectClientExitReports=$true
  function PollStatus {
   $monitor.GetType().GetMethod('Poll',$f).Invoke($monitor,@($settings))|Out-Null
   $tracked.GetType().GetField('SnapshotStatus',$f).GetValue($tracked)
  }
  Assert ((PollStatus) -eq 'SnapshotMissing') 'Missing expected file diagnosed by real poll'
  $pulseFile=Join-Path $temp ($key+'.json')
  [IO.File]::WriteAllText($pulseFile,'null')
  Assert ((PollStatus) -eq 'SnapshotInvalid') 'Invalid snapshot diagnosed by real poll'
  [IO.File]::WriteAllText($pulseFile,'{}')
  $locked=[IO.File]::Open($pulseFile,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  try{Assert ((PollStatus) -eq 'SnapshotUnreadable') 'Locked file diagnosed without losing process observation'}finally{$locked.Dispose()}
  $settings.MonitorClientHealth=$false
  Assert ((PollStatus) -eq 'MonitoringDisabled') 'Disabled monitoring distinguishable from missing plugin'
  $capture=$monitor.GetType().GetMethod('Capture',$f).Invoke($monitor,@())|ConvertFrom-Json
  Assert ($capture.Format -eq 2 -and !$capture.MonitorClientHealth -and $capture.CollectClientExitReports) 'Export records monitor configuration'
  Assert ($capture.Clients[0].Summary.TotalSamples -eq 4 -and $capture.Clients[0].Summary.SamplesWithPluginData -eq 0) 'Unmeasured polling creates summary without inventing evidence'
  Assert ($null -eq $capture.Clients[0].Samples[0].Zoning) 'Missing zoning remains null in serialized report'
  Remove-Item -LiteralPath $pulseFile
 }finally{$monitor.Dispose()}
 $report=New 'ClientReport+SavedExit';$report.Client='SECRET_CHARACTER';$report.ProcessId=123;$report.StartedUtc=$start;$report.StartedUtcTicks=$start.Ticks;$report.ExitedUtc=$start.AddMinutes(2);$report.ExitCode=0
 $report.Samples=@($s);$report.SnapshotStatus='SnapshotMissing'
 $session=New 'ClientReport+SessionReport';$session.Format=2;$session.CreatedUtc=[DateTime]::UtcNow;$session.MonitorClientHealth=$true;$session.Clients=@($report)
 [IO.File]::WriteAllText((Join-Path $temp 'duplicate.json'),$json.Serialize($report))
 $report2=New 'ClientReport+SavedExit';$report2.Client='SECRET_OLD_ALIAS';$report2.ProcessId=123;$report2.StartedUtc=$start.AddMinutes(-10);$report2.StartedUtcTicks=$report2.StartedUtc.Ticks;$report2.Samples=@()
 [IO.File]::WriteAllText((Join-Path $temp 'reused-pid.json'),$json.Serialize($report2))
 $zipPath=Join-Path $temp 'report.zip'
 Static ClientReport Export @($zipPath,$json.Serialize($session),'Fixture evidence',$temp)|Out-Null
 $zip=[IO.Compression.ZipFile]::OpenRead($zipPath)
 try {
  Assert ($zip.Entries.Count -eq 5) 'Duplicate exit omitted; different lifetime of reused PID retained'
  $text=ReadEntry $zip 'report-summary.txt'
  Assert ($text.Contains('NO RESPONSIVENESS EVIDENCE') -and $text.Contains('duplicates: 1') -and $text.Contains('enabled')) 'Export states missing evidence, settings and duplicate count'
  $saved=@($zip.Entries|Where-Object FullName -like 'saved-exits/*')[0]
  $history=(ReadEntry $zip $saved.FullName)|ConvertFrom-Json
  Assert ($history.Client -eq 'Client-2' -and $saved.FullName.StartsWith('saved-exits/Client-2-')) 'Historical report and filename share one export alias'
  $current=(ReadEntry $zip 'current-session.json')|ConvertFrom-Json
  Assert ($current.Clients[0].Client -eq 'Client-1' -and $current.Clients[0].StartedUtcTicks -eq $start.Ticks) 'Canonical current alias and exact identity ticks survive serialization'
  foreach($entry in $zip.Entries){Assert (!(ReadEntry $zip $entry.FullName).Contains('SECRET')) 'Character names never escape into export'}
 }finally{$zip.Dispose()}
 # Distinct evidence for the same process must be preserved, with the same alias.
 $report.Summary=$summary
 [IO.File]::WriteAllText((Join-Path $temp 'distinct.json'),$json.Serialize($report))
 $session.Clients[0].Summary=$null
 $second=Join-Path $temp 'distinct.zip'
 Static ClientReport Export @($second,$json.Serialize($session),'Fixture evidence',$temp)|Out-Null
 $zip=[IO.Compression.ZipFile]::OpenRead($second)
 try{
  $entries=@($zip.Entries|Where-Object FullName -like 'saved-exits/Client-1-*')
  Assert ($entries.Count -eq 1) 'Additional lifetime summary retained using current client alias'
  Assert (!(ReadEntry $zip 'report-summary.txt').Contains('NO RESPONSIVENESS EVIDENCE')) 'Historical plugin evidence is included in coverage assessment'
 }finally{$zip.Dispose()}
 # Legacy reports store DateTime at millisecond precision, so compare at that precision.
 $legacy=New 'ClientReport+SavedExit';$legacy.ProcessId=123;$legacy.StartedUtc=[DateTime]::new($start.Ticks-($start.Ticks%10000),[DateTimeKind]::Utc)
 Assert (Static ClientReport SameProcess @($report,$legacy)) 'Legacy dates pair with exact new identity at millisecond precision'
 $legacy.StartedUtcTicks=$start.Ticks+1
 Assert (!(Static ClientReport SameProcess @($report,$legacy))) 'Two exact process identities never merge on rounded milliseconds'
}finally{
 $full=[IO.Path]::GetFullPath($temp);$base=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$full.StartsWith($base,[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($full).StartsWith('NexusReportTest-')){throw 'Unsafe cleanup path'}
 Remove-Item -LiteralPath $full -Recurse -Force
}
Write-Output "PASS: $checks report coverage, identity, deduplication, privacy and lifetime-summary checks."
