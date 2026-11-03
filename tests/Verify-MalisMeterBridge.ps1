$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $root 'logs/MalisBridgeFixture'
[IO.Directory]::CreateDirectory($output)|Out-Null
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
& $compiler /nologo /target:library "/out:$output/Malis Damage Meter.dll" "$PSScriptRoot/MalisMeterFixture.cs"
if($LASTEXITCODE){throw 'Meter interface fixture compilation failed'}
& $compiler /nologo /target:exe "/out:$output/Bridge.Tests.exe" "/r:$output/Malis Damage Meter.dll" "$PSScriptRoot/MalisMeterBridge.Tests.cs" "$root/src/Telemetry/MalisMeterBridge.cs" "$root/src/Common/CombatMeterSnapshot.cs"
if($LASTEXITCODE){throw 'Meter bridge test compilation failed'}
& "$output/Bridge.Tests.exe"
if($LASTEXITCODE){throw 'Meter bridge tests failed'}
