param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../src/Launcher/bin/Release/AONexus.exe'))
$ErrorActionPreference='Stop'
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$f=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$root=Join-Path ([IO.Path]::GetTempPath()) ('NexusActionIssueFixture-'+[Guid]::NewGuid().ToString('N'))
$helper=$a.GetType('AOLauncher.EmbeddedMonitoring').GetMethod('Extract',$f).Invoke($null,@([IO.Path]::GetFullPath($root)))
$protocol=[Reflection.Assembly]::LoadFrom((Join-Path $helper 'NexusTelemetry.Protocol.dll'))
$pulse=[Activator]::CreateInstance($protocol.GetType('NexusTelemetry.Pulse'))
$tracker=[Activator]::CreateInstance($protocol.GetType('NexusTelemetry.ActionIssueTracker'),[object[]]@($pulse))
$checks=0
function Assert($ok,$why){if(!$ok){throw $why};$script:checks++}
$frequency=[Diagnostics.Stopwatch]::Frequency;$now=[Diagnostics.Stopwatch]::GetTimestamp()
$tracker.ObserveCast($false,$false,$now)
$tracker.ObserveCast($true,$true,$now)
$tracker.ObserveFeedback('A player said: You can''t execute nanoprograms while falling!',$now)
$tracker.ObserveFeedback('Arbitrary private text',$now)
Assert ($pulse.ActionIssue -eq 0) 'Grounded casts, zoning and unrecognized text do not create alerts'
$tracker.ObserveCast($true,$false,$now)
Assert ($pulse.ActionIssue -eq 1 -and $pulse.ActionIssueCount -eq 1 -and !$pulse.ActionIssueConfirmed) 'Falling cast is an observation, not a confirmed rejection'
$tracker.ObserveFeedback('You can''t execute nanoprograms while falling!',$now)
Assert ($pulse.ActionIssueCount -eq 1 -and $pulse.ActionIssueConfirmed) 'Feedback confirms the issue without counting the same attempt twice'
for($i=1;$i -le 100;$i++){$tracker.ObserveCast($true,$false,($now+[long]($frequency*$i/100)))}
Assert ($pulse.ActionIssueCount -ge 4 -and $pulse.ActionIssueCount -le 5) 'Rapid reports are grouped but continuous retries still increase the bounded counter'
$later=$now+32*$frequency;$tracker.ObserveCast($true,$false,$later)
Assert ($pulse.ActionIssueCount -eq 1 -and !$pulse.ActionIssueConfirmed) 'A new burst resets the count and confirmation evidence'
$clientPulse=[Activator]::CreateInstance($a.GetType('AOLauncher.ClientPulse'),$true)
$clientPulse.Active=$true;$clientPulse.ActionIssue=1;$clientPulse.ActionIssueCount=1;$clientPulse.PublishedQpc=$now;$clientPulse.ActionIssueQpc=$now
$recent=$a.GetType('AOLauncher.ClientMonitor').GetMethod('HasRecentCastIssue',$f)
function Recent($time){$recent.Invoke($null,[object[]]@($clientPulse,[long]$time))}
Assert (Recent $now) 'Fresh recognized action issue is visible'
Assert (!(Recent ($now+4*$frequency))) 'Stale telemetry cannot report a current action alert'
$clientPulse.PublishedQpc=$now+31*$frequency
Assert (!(Recent ($now+31*$frequency))) 'An alert expires after 30 seconds even when telemetry remains fresh'
$clientPulse.PublishedQpc=$now;$clientPulse.ActionIssueQpc=$now+1
Assert (!(Recent $now)) 'Future action timestamps are rejected'
$clientPulse.ActionIssueQpc=$now;$clientPulse.Active=$false
Assert (!(Recent $now)) 'Unloaded monitoring cannot keep an action alert active'
$clientPulse.Active=$true;$clientPulse.Zoning=$true
Assert (!(Recent $now)) 'Zoning suppresses the recent movement-related alert'
$clientPulse.Zoning=$false;$clientPulse.ActionIssue=99
Assert (!(Recent $now)) 'Unknown issue codes are ignored'
$clientPulse.ActionIssue=1;$clientPulse.ActionIssueCount=10000
Assert (!(Recent $now)) 'Unbounded counters are rejected'
$legacy=[Activator]::CreateInstance($a.GetType('AOLauncher.ClientPulse'),$true)
Assert (!$recent.Invoke($null,[object[]]@($legacy,[long]$now))) 'Older monitoring snapshots remain compatible without invented action alerts'
Write-Output "PASS: $checks action-event classification, duplicate grouping, evidence and expiry checks. No game or profile accessed."
