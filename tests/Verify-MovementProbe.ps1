param([string]$AOSharpRuntime)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $root 'logs/MovementProbe'
[IO.Directory]::CreateDirectory($output)|Out-Null
if (!$AOSharpRuntime) { $AOSharpRuntime = (& "$root/build/Restore-TelemetryDependency.ps1" | Select-Object -Last 1) }
$runtime=[IO.Path]::GetFullPath($AOSharpRuntime)
Copy-Item -LiteralPath (Join-Path $runtime 'AOSharp.Common.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root 'dist/Monitoring/NexusTelemetry.Protocol.dll') -Destination $output -Force
$compiler=& "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products '*' -find 'MSBuild/Current/Bin/Roslyn/csc.exe' | Select-Object -First 1
& $compiler /nologo /target:exe /platform:x86 /main:MovementProbeTests "/out:$output/MovementProbe.Tests.exe" "/r:$output/AOSharp.Common.dll" "/r:$output/NexusTelemetry.Protocol.dll" /r:System.Web.Extensions.dll "$root/src/Shared/AutoRegroup.cs" "$PSScriptRoot/TelemetryGameFixture.cs" "$PSScriptRoot/MovementProbe.Tests.cs" "$root/src/Telemetry/NexusHealthPlugin.cs" "$root/src/Telemetry/NativeCombatMeter.cs" "$root/src/Telemetry/SyncMovementProbe.cs"
if($LASTEXITCODE){throw 'Movement probe fixture compilation failed'}
& "$output/MovementProbe.Tests.exe"
if($LASTEXITCODE){throw 'Movement probe tests failed'}
