param([Parameter(Mandatory=$true)][string]$AOSharpRuntime)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $root 'logs/NativeMeterFixture'
[IO.Directory]::CreateDirectory($output)|Out-Null
Copy-Item -LiteralPath (Join-Path $AOSharpRuntime 'AOSharp.Common.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $AOSharpRuntime 'Newtonsoft.Json.dll') -Destination $output -Force
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
& $compiler /nologo /target:exe /platform:x86 "/out:$output/Native.Tests.exe" "/r:$output/AOSharp.Common.dll" "$PSScriptRoot/NativeCombatMeter.Tests.cs" "$root/src/Telemetry/NativeCombatMeter.cs" "$root/src/Common/CombatMeterSnapshot.cs"
if($LASTEXITCODE){throw 'Native meter compilation failed'}
& "$output/Native.Tests.exe"
if($LASTEXITCODE){throw 'Native meter checks failed'}
