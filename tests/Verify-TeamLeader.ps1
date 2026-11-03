$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $root 'logs/TeamLeaderTests'
[IO.Directory]::CreateDirectory($output)|Out-Null
$runtime=Join-Path $root 'runtime/Local13Pack/AOSharp'
Copy-Item -LiteralPath (Join-Path $runtime 'AOSharp.Common.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root 'dist/Monitoring/NexusTelemetry.Protocol.dll') -Destination $output -Force
$compiler=& "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products '*' -find 'MSBuild/Current/Bin/Roslyn/csc.exe' | Select-Object -First 1
# The fixture intentionally replaces the native GUI import with an inert test double.
& $compiler /nologo /nowarn:0436 /target:exe /platform:x86 /main:TeamLeaderTests "/out:$output/TeamLeader.Tests.exe" "/r:$output/AOSharp.Common.dll" "/r:$output/NexusTelemetry.Protocol.dll" /r:System.Web.Extensions.dll "$root/src/Shared/AutoRegroup.cs" "$PSScriptRoot/TelemetryGameFixture.cs" "$PSScriptRoot/TeamLeader.Tests.cs" "$root/src/Telemetry/NexusHealthPlugin.cs" "$root/src/Telemetry/NativeCombatMeter.cs" "$root/src/Telemetry/SyncMovementProbe.cs"
if($LASTEXITCODE){throw 'Team leader fixture compilation failed'}
& "$output/TeamLeader.Tests.exe"
if($LASTEXITCODE){throw 'Team leader tests failed'}
