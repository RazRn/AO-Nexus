param([string]$AOSharpRuntime, [string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(!$AOSharpRuntime){$AOSharpRuntime=(& "$PSScriptRoot\Restore-TelemetryDependency.ps1" | Select-Object -Last 1)}
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $root 'dist\Monitoring'}
New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach($name in @('AOSharp.Common.dll','Newtonsoft.Json.dll')) {Copy-Item -LiteralPath (Join-Path $AOSharpRuntime $name) -Destination $output -Force}
& $compiler /nologo /target:library /platform:x86 "/out:$output\NexusTelemetry.Protocol.dll" "/r:$output\AOSharp.Common.dll" "$root\src\Telemetry\TelemetryProtocol.cs"
if($LASTEXITCODE){throw 'Telemetry protocol build failed'}
$refs=@("/r:$output\AOSharp.Common.dll","/r:$output\NexusTelemetry.Protocol.dll",'/r:System.Web.Extensions.dll')
& $compiler /nologo /target:library /platform:x86 "/out:$output\NexusHealthPlugin.dll" @refs "/r:$AOSharpRuntime\AOSharp.Core.dll" "$root\src\Telemetry\NexusHealthPlugin.cs"
if($LASTEXITCODE){throw 'Telemetry plugin build failed'}
& $compiler /nologo /target:exe /platform:x86 "/out:$output\NexusHealthHost.exe" @refs "$root\src\Telemetry\HealthHost.cs"
if($LASTEXITCODE){throw 'Telemetry host build failed'}
Copy-Item -LiteralPath "$root\docs\Client-Monitoring.txt" -Destination "$output\README.txt" -Force
Write-Output "Optional monitoring companion ready: $output"
