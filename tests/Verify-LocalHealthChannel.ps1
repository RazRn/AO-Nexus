param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
$output=Join-Path $root 'logs/LocalHealthChannel.Tests.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$output" "$PSScriptRoot/LocalHealthChannel.Tests.cs" "$root/src/Telemetry/LocalHealthChannel.cs" "$root/src/Telemetry/TelemetryProtocol.cs" "$root/src/Common/CombatMeterSnapshot.cs" "$root/src/Common/MovementSnapshot.cs"
if($LASTEXITCODE){throw 'Local channel test build failed'}
& $output
if($LASTEXITCODE){throw 'Local channel tests failed'}
