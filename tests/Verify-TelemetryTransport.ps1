param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../src/Launcher/bin/Release/AONexus.exe'), [string]$MonitoringDirectory)
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
foreach($name in @('AOSharp.Common.dll','Newtonsoft.Json.dll','NexusTelemetry.Protocol.dll')){Copy-Item -LiteralPath (Join-Path $helper $name) -Destination $output -Force}
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
& $compiler /nologo /target:exe /platform:x86 "/out:$output\TelemetryFixture.exe" "/r:$output\AOSharp.Common.dll" "/r:$output\NexusTelemetry.Protocol.dll" /r:System.Web.Extensions.dll "$PSScriptRoot\TelemetryGameFixture.cs" "$root\src\Telemetry\NexusHealthPlugin.cs"
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
 Command 'falling';$row=Sample 2
 Assert ((Field $row CastWhileFalling) -and !(Field $row CastIssueConfirmed)) 'Cast hook publishes observed falling without claiming a confirmed rejection'
 Command 'falling-network';$row=Sample 2
 Assert ((Field $row CastIssueCount) -gt 1 -and (Field $row Condition).ToString() -eq 'Healthy') 'Plugin cast packets increase the grouped alert while responsiveness remains healthy'
 Command 'falling-feedback';$row=Sample 2
 Assert ((Field $row CastIssueConfirmed)) 'Recognized game feedback reaches Nexus as a confirmed recent failure'
 Assert ($monitor.Changes.Count -eq 0) 'Repeated action reports do not spam the launcher log'
 Command 'normal'
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
 Assert (!(Get-Content -LiteralPath $saved[0].FullName -Raw).Contains('Synthetic test client')) 'Persisted exit report must not retain character name'
 Write-Output "PASS: $checks production-plugin/real-IPC checks: normal, frozen loop, late reply, recovery, stalled update, blocked IPC, unload, process exit and retained report."
}finally{
 if(!$fixture.HasExited){Command 'quit';if(!$fixture.WaitForExit(3000)){$fixture.Kill()}}
 $monitor.Dispose();$fixture.Dispose()
 if($pulsePath){foreach($path in @($pulsePath,$pulsePath+'.tmp')){if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}}}
}
