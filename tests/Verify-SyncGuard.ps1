param([string]$AOSharpRuntime)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $root 'logs/SyncGuardTests'
[IO.Directory]::CreateDirectory($output)|Out-Null
if (!$AOSharpRuntime) { $AOSharpRuntime = (& "$root/build/Restore-TelemetryDependency.ps1" | Select-Object -Last 1) }
Copy-Item -LiteralPath "$AOSharpRuntime/AOSharp.Common.dll" -Destination $output -Force
$compiler=& "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products '*' -find 'MSBuild/Current/Bin/Roslyn/csc.exe' | Select-Object -First 1
& $compiler /nologo /target:library /platform:x86 "/out:$output/NexusTelemetry.Protocol.dll" /r:System.Web.Extensions.dll "$root/src/Telemetry/TelemetryProtocol.cs" "$root/src/Telemetry/LocalHealthChannel.cs" "$root/src/Common/CombatMeterSnapshot.cs" "$root/src/Common/MovementSnapshot.cs"
if($LASTEXITCODE){throw 'Telemetry protocol compilation failed'}
& $compiler /nologo /nowarn:0436 /define:SYNC_GUARD_TEST /target:exe /platform:x86 /main:SyncGuardTests "/out:$output/SyncGuard.Tests.exe" "/r:$output/AOSharp.Common.dll" "/r:$output/NexusTelemetry.Protocol.dll" /r:System.Web.Extensions.dll "$root/src/Shared/AutoRegroup.cs" "$PSScriptRoot/TelemetryGameFixture.cs" "$PSScriptRoot/SyncGuard.Tests.cs" "$root/src/Shared/SyncSeparationPolicy.cs" "$root/src/Telemetry/SyncMovementGuard.cs" "$root/src/Telemetry/AutoRegroupController.cs" "$root/src/Shortcuts/FormationMovementLease.cs" "$root/src/Telemetry/NexusHealthPlugin.cs" "$root/src/Telemetry/NativeCombatMeter.cs" "$root/src/Telemetry/SyncMovementProbe.cs"
if($LASTEXITCODE){throw 'Sync guard fixture compilation failed'}
& "$output/SyncGuard.Tests.exe"
if($LASTEXITCODE){throw 'Sync guard tests failed'}
