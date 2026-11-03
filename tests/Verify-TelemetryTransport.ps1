param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../src/Launcher/bin/Release/AONexus.exe'), [string]$MonitoringDirectory, [string]$AOSharpRuntime=(Join-Path $PSScriptRoot '../runtime/Local13Pack/AOSharp'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Web.Extensions
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$helper=if($MonitoringDirectory){[IO.Path]::GetFullPath($MonitoringDirectory)}else{
 $cache=Join-Path ([IO.Path]::GetTempPath()) ('NexusTransportFixture-'+[Guid]::NewGuid().ToString('N'))
 $a.GetType('AOLauncher.EmbeddedMonitoring').GetMethod('Extract',$flags).Invoke($null,@([IO.Path]::GetFullPath($cache)))
}
$output=Join-Path $root 'logs\TelemetryFixture'
New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach($name in @('AOSharp.Common.dll','Newtonsoft.Json.dll')){Copy-Item -LiteralPath (Join-Path $AOSharpRuntime $name) -Destination $output -Force}
Copy-Item -LiteralPath (Join-Path $helper 'NexusTelemetry.Protocol.dll') -Destination $output -Force
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
. (Join-Path $PSScriptRoot 'CompilerInputs.ps1')
$nexusCompilerInputs = @(Get-NexusCompilerInputs)
& $compiler @nexusCompilerInputs /nologo /target:exe /platform:x86 "/out:$output\TelemetryFixture.exe" "/r:$output\AOSharp.Common.dll" "/r:$output\NexusTelemetry.Protocol.dll" /r:System.Web.Extensions.dll "$root/src/Shared/AutoRegroup.cs" "$PSScriptRoot\TelemetryGameFixture.cs" "$root\src\Telemetry\NexusHealthPlugin.cs" "$root\src\Telemetry\NativeCombatMeter.cs" "$root\src\Telemetry\SyncMovementProbe.cs"
if($LASTEXITCODE){throw 'Fixture build failed'}
$checks=0
function Assert($ok,$why){if(!$ok){throw $why};$script:checks++}
function Field($o,$name){,$o.GetType().GetField($name,$flags).GetValue($o)}
function SetField($o,$name,$value){$o.GetType().GetField($name,$flags).SetValue($o,$value)}
function Invoke($o,$name,$values=@()){$o.GetType().GetMethod($name,$flags).Invoke($o,[object[]]$values)}
$settings=[Activator]::CreateInstance($a.GetType('AOLauncher.LauncherSettings'),$true)
$settings.MonitorClientHealth=$true;$settings.CollectClientExitReports=$true
$pulseRoot=Join-Path $env:LOCALAPPDATA 'AONexus\Telemetry'
$reports=Join-Path $output ('reports-'+[Guid]::NewGuid().ToString('N'))
$monitor=[Activator]::CreateInstance($a.GetType('AOLauncher.ClientMonitor'),$flags,$null,[object[]]@([string]$pulseRoot,[string]$reports,[string]$helper),$null)
$fixture=[Diagnostics.Process]::new()
$fixture.StartInfo=[Diagnostics.ProcessStartInfo]::new((Join-Path $output 'TelemetryFixture.exe'))
$fixture.StartInfo.UseShellExecute=$false;$fixture.StartInfo.CreateNoWindow=$true
$fixture.StartInfo.RedirectStandardInput=$true;$fixture.StartInfo.RedirectStandardOutput=$true;$fixture.StartInfo.RedirectStandardError=$true
$pulsePath=$null
function Command($text){$fixture.StandardInput.WriteLine($text);$fixture.StandardInput.Flush()}
function Sample($seconds){
 $end=[DateTime]::UtcNow.AddSeconds($seconds)
 do{Invoke $monitor Poll @($settings)|Out-Null;Start-Sleep -Milliseconds 250}while([DateTime]::UtcNow -lt $end)
 $monitor.GetType().GetProperty('Current',$flags).GetValue($monitor)[0]
}
try {
 $fixture.Start()|Out-Null
 $line=$fixture.StandardOutput.ReadLine();if(!$line){throw ('Fixture failed: '+$fixture.StandardError.ReadToEnd())}
 $identity=$line.Split(' ');$gamePid=[int]$identity[0];$startTicks=[long]$identity[1]
 $pulsePath=Join-Path $pulseRoot ($gamePid.ToString()+'-'+$startTicks+'.json')
 $client=[Activator]::CreateInstance($a.GetType('AOLauncher.PresetCamping+Client'),$true)
 $client.ProcessId=$gamePid;$client.StartTimeUtcTicks=$startTicks;$client.CharacterName='Synthetic test client'
 $tracked=[Activator]::CreateInstance($a.GetType('AOLauncher.ClientMonitor+Tracked'),$true)
 $retained=[Diagnostics.Process]::GetProcessById($gamePid);$null=$retained.Handle
 SetField $tracked Client $client;SetField $tracked Process $retained;SetField $tracked Alias 'Client-fixture';SetField $tracked Version 'fixture'
 $dictionary=Field $monitor clients;$dictionary.Add($gamePid.ToString()+'-'+$startTicks,$tracked)
 $row=Sample 5
 Assert ((Field $row Condition).ToString() -eq 'Healthy') ('Production plugin + real IPC should respond: '+(Field $row Condition)+' / '+$monitor.GetType().GetProperty('Error',$flags).GetValue($monitor))
 $s=Field $row Sample;Assert ($s.Replies -gt 0 -and $s.Timeouts -eq 0) 'Real IPC replies must arrive without timeouts in normal fixture'
 $movement=(Field $row Pulse).Movement
 Assert ($movement.CharacterId -eq 1 -and $movement.Playfield -eq 42 -and $movement.Qpc -gt 0 -and $movement.Adapter -eq 'Unavailable') 'Movement snapshot survives production event collection and JSON without inventing an absent SyncManager'
 $helperProcess=Field $monitor host
 $measurementPids=@($helperProcess.Id,$gamePid)
 Assert (@(Get-NetUDPEndpoint | Where-Object {$_.OwningProcess -in $measurementPids}).Count -eq 0) 'Helper and production-plugin fixture open no UDP sockets'
 Assert (@(Get-NetTCPConnection | Where-Object {$_.OwningProcess -in $measurementPids}).Count -eq 0) 'Helper and production-plugin fixture open no TCP sockets'
 Command 'leader';$row=Sample 2
 Assert ((Field $row Pulse).IsTeamLeader -eq $true -and (Field $row Pulse).TeamStateQpc -gt 0) 'In-game leadership reaches Nexus through production telemetry'
 Command 'member';$row=Sample 1
 Assert ((Field $row Pulse).IsTeamLeader -eq $false) 'Leadership transfer clears the previous leader'
 Command 'solo-leader';$row=Sample 1
 Assert ((Field $row Pulse).IsTeamLeader -eq $false) 'An unteamed character cannot be marked leader'
 Command 'falling';$row=Sample 2
 Assert ((Field $row CastWhileFalling) -and !(Field $row CastIssueConfirmed)) 'Cast hook publishes observed falling without claiming a confirmed rejection'
 Assert ((Field $row Pulse).CastNanoId -eq 1 -and (Field $row Pulse).CastNanoName -eq 'Synthetic healing nano') 'Known nano name travels from production plugin through JSON to the launcher'
 Command 'falling-network';$row=Sample 2
 Assert ((Field $row CastIssueCount) -gt 1 -and (Field $row Condition).ToString() -eq 'Healthy') 'Plugin cast packets increase the grouped alert while responsiveness remains healthy'
 Assert ((Field $row Pulse).CastNanoId -eq 2 -and !(Field $row Pulse).CastNanoName) 'Unknown nano clears the previous name and keeps its own ID for fallback'
 Command 'falling-feedback';$row=Sample 2
 Assert ((Field $row CastIssueConfirmed)) 'Recognized game feedback reaches Nexus as a confirmed recent failure'
 Assert ($monitor.Changes.Count -eq 0) 'Repeated action reports do not spam the launcher log'
 Command 'normal'
 Command 'combat';$row=Sample 2
 $meter=(Field $row Pulse).CombatMeter
 Assert ($meter.Source -eq 'Nexus' -and $meter.Status -eq 'Ready' -and $meter.Actors.Count -eq 1) 'Built-in meter publishes without Mali loaded'
 Assert ($meter.Actors[0].Damage -eq 100 -and $meter.Actors[0].Xp -eq 123 -and $meter.Actors[0].Axp -eq 45) 'Damage and XP/AXP awards pass through production events, JSON and launcher deserialization'
 Command 'pet-combat';$row=Sample 2
 $meter=(Field $row Pulse).CombatMeter
 Assert ($meter.Actors[0].PetDamage -eq 50 -and $meter.Actors[0].Damage -eq 150 -and $meter.Actors[0].NanoDamage -eq 10) 'Production ownership resolver counts roster-only pets without dynels or owner metadata; foreign and released pets are excluded'
 Command 'nearby-combat';$row=Sample 2
 $observed=(Field $row Pulse).ObservedCombatMeter
 Assert ($observed.Source -eq 'NexusObserved' -and $observed.Actors.Count -eq 5) 'Separate nearby snapshots survive production event collection, JSON and launcher deserialization'
 $visitor=@($observed.Actors | Where-Object {$_.CharacterId -eq 20})[0]
 Assert ($visitor.Name -eq 'Nearby 20' -and $visitor.Damage -eq 356 -and $visitor.PetDamage -eq 111 -and $visitor.NanoDamage -eq 85 -and $visitor.Xp -eq 0) 'Production resolver assigns a remote pet to its observed player owner without claiming that players XP'
 Assert (@($observed.Actors | Where-Object {$_.CharacterId -eq 22 -and $_.UnassignedPet -and $_.PetDamage -eq 7}).Count -eq 1) 'Unresolved pet owners stay visible and ordinary NPCs are excluded'
 Assert ((Field $row Pulse).CombatMeter.Actors[0].Damage -eq 273) 'Only the additional local-player hit changes the local combat total'
 $npc=(Field $row Pulse).NpcCombatMeter
 Assert ($npc.Source -eq 'NexusNPC' -and $npc.Actors.Count -eq 1 -and $npc.Actors[0].CharacterId -eq 23 -and $npc.Actors[0].Damage -eq 1004 -and $npc.Actors[0].DamageTaken -eq 168) 'NPC dealt/taken counters survive production classification, event endpoints, JSON transport and launcher deserialization'
 Command 'pause';$row=Sample 5
 Assert ((Field $row Condition).ToString() -eq 'NoResponse') 'Stopped loop must be visible despite running telemetry writer'
 Assert ((Field $row Sample).Timeouts -gt 0) 'Independent host must count timeouts while game loop is stopped'
 Command 'normal';$row=Sample 3
 Assert ((Field $row Sample).Late -gt 0) 'Late replies must be counted separately after recovery'
 Assert ((Field $row Condition).ToString() -eq 'Delayed') 'Recovery retains the recent long pause as an amber warning'
 $row=Sample 11;Assert ((Field $row Condition).ToString() -eq 'Healthy') 'Warning clears after healthy recent window'
 Command 'ipc-only';$row=Sample 4
 Assert ((Field $row Condition).ToString() -eq 'NoResponse' -and (Field $row Sample).ReplyMs -lt 100) 'Good IPC must not hide stalled OnUpdate subscribers'
 Command 'normal';Sample 2|Out-Null
 Command 'ticks-only';$row=Sample 5
 Assert ((Field $row Condition).ToString() -eq 'NoResponse' -and (Field $row Sample).UpdateAgeMs -lt 1500) 'Good ticks must not hide blocked IPC'
 Command 'normal';Sample 2|Out-Null
 Command 'unload';$row=Sample 2
 Assert ((Field $row Condition).ToString() -eq 'Unloaded') 'Explicit teardown is recognized without a false crash'
 Assert (!(Field $row CastWhileFalling)) 'Unloading clears the live action alert'
 Command 'crash';$fixture.WaitForExit(5000)|Out-Null;$row=Sample 1
 Assert ((Field $row Condition).ToString() -eq 'Exited' -and (Field $row ExitCode) -eq 37) 'OS process exit and code must be retained'
 $saved=@(Get-ChildItem -LiteralPath $reports -Filter '*.json')
 Assert ($saved.Count -eq 1) 'Observed exit saves one local report'
 $json=Get-Content -LiteralPath $saved[0].FullName -Raw|ConvertFrom-Json
 Assert ($json.Samples.Count -gt 0 -and $json.ExitCode -eq 37) 'Exit report preserves the pre-exit measurement history'
 Assert ($json.Summary.TotalSamples -gt $json.Samples.Count -and $json.Samples.Count -eq 120) 'Observed lifetime summary survives actual recent-sample eviction'
 Assert ($json.Summary.SamplesWithPluginData -gt 0 -and $json.Summary.LargestPluginReportedGapMs -ge 2000) 'Saved summary retains plugin coverage and the induced long game-loop pause'
 Assert ($json.StartedUtcTicks -eq $startTicks -and $json.SnapshotStatus -eq 'SnapshotAccepted') 'Saved exit retains exact process identity and snapshot acceptance status'
 Assert (!(Get-Content -LiteralPath $saved[0].FullName -Raw).Contains('Synthetic test client')) 'Persisted exit report must not retain character name'
 Write-Output "PASS: $checks production-plugin/real-IPC checks: normal, frozen loop, late reply, recovery, stalled update, blocked IPC, unload, process exit and retained report."
}finally{
 if(!$fixture.HasExited){Command 'quit';if(!$fixture.WaitForExit(3000)){$fixture.Kill()}}
 $monitor.Dispose();$fixture.Dispose()
 if($pulsePath){foreach($path in @($pulsePath,$pulsePath+'.tmp')){if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}}}
}
