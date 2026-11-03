param([Parameter(Mandatory=$true)][string]$AOSharpRuntime,[Parameter(Mandatory=$true)][string]$MonitoringDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $root 'logs/NpcTransportFixture'
[IO.Directory]::CreateDirectory($output)|Out-Null
foreach($name in @('AOSharp.Common.dll','Newtonsoft.Json.dll')){Copy-Item -LiteralPath (Join-Path $AOSharpRuntime $name) -Destination $output -Force}
Copy-Item -LiteralPath (Join-Path $MonitoringDirectory 'NexusTelemetry.Protocol.dll') -Destination $output -Force
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
. (Join-Path $PSScriptRoot 'CompilerInputs.ps1')
$nexusCompilerInputs = @(Get-NexusCompilerInputs)
& $compiler @nexusCompilerInputs /nologo /target:exe /platform:x86 "/out:$output/TelemetryFixture.exe" "/r:$output/AOSharp.Common.dll" "/r:$output/NexusTelemetry.Protocol.dll" /r:System.Web.Extensions.dll "$root/src/Shared/AutoRegroup.cs" "$PSScriptRoot/TelemetryGameFixture.cs" "$root/src/Telemetry/NexusHealthPlugin.cs" "$root/src/Telemetry/NativeCombatMeter.cs" "$root/src/Telemetry/SyncMovementProbe.cs"
if($LASTEXITCODE){throw 'Telemetry fixture compilation failed'}
. (Join-Path $PSScriptRoot 'CompilerInputs.ps1')
$nexusCompilerInputs = @(Get-NexusCompilerInputs)
& $compiler @nexusCompilerInputs /nologo /target:exe /platform:x86 "/out:$output/NpcTransport.Tests.exe" /r:System.Web.Extensions.dll "$PSScriptRoot/NpcTransport.Tests.cs"
if($LASTEXITCODE){throw 'NPC transport test compilation failed'}
& "$output/NpcTransport.Tests.exe" "$root/src/Launcher/bin/Release/AONexus.exe" "$output/TelemetryFixture.exe"
if($LASTEXITCODE){throw 'NPC transport test failed'}
