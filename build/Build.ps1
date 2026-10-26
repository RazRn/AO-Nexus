param([switch]$RebuildBackend, [switch]$RebuildTelemetry, [switch]$Package, [string]$PlatformToolset = 'v145')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($RebuildTelemetry -or !(Test-Path -LiteralPath (Join-Path $projectRoot 'dist\Monitoring\NexusHealthPlugin.dll'))) { & (Join-Path $PSScriptRoot 'Build-Telemetry.ps1') }
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\Client-Monitoring.txt') -Destination (Join-Path $projectRoot 'dist\Monitoring\README.txt') -Force
& (Join-Path $PSScriptRoot 'Build-MonitoringPayload.ps1')
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'Install Visual Studio Build Tools with .NET Framework 4.8 and desktop C++ support.' }
if ($RebuildBackend) {
    & (Join-Path $PSScriptRoot 'Restore-NativeDependency.ps1')
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    $localSdk = Join-Path $env:LOCALAPPDATA 'AOSharpBuildTools\dotnet\dotnet.exe'
    $dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } elseif ($dotnetCommand) { $dotnetCommand.Source } else { $localSdk }
    if (!(Test-Path -LiteralPath $dotnet)) { throw 'Install the .NET 10 SDK.' }
    & $msbuild (Join-Path $projectRoot 'src\Backend\LoginHandler\LoginHandler.vcxproj') /p:Configuration=Release /p:Platform=Win32 "/p:PlatformToolset=$PlatformToolset" /m:1 /nologo /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Native backend build failed.' }
    & $dotnet publish (Join-Path $projectRoot 'src\Backend\NexusEngine\NexusEngine.csproj') -c Release -r win-x86 --self-contained true -o (Join-Path $projectRoot 'runtime\NexusEngine') --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Managed backend build failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\Backend\Build\Release\net5.0\LoginHandler.dll') -Destination (Join-Path $projectRoot 'runtime\NexusEngine\LoginHandler.dll') -Force
}
& (Join-Path $PSScriptRoot 'Build-Payload.ps1')
& $msbuild (Join-Path $projectRoot 'src\Launcher\AONexus.csproj') /p:Configuration=Release /p:PlatformTarget=x86 /m:1 /nologo /verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw 'AO Nexus build failed.' }
$compiled = Join-Path $projectRoot 'src\Launcher\bin\Release\AONexus.exe'
[IO.Directory]::CreateDirectory((Join-Path $projectRoot 'dist')) | Out-Null
Copy-Item -LiteralPath $compiled -Destination (Join-Path $projectRoot 'dist\AONexus.exe') -Force
& (Join-Path $PSScriptRoot 'Build-Payload.ps1') -ValidateOnly -VerifyExecutable (Join-Path $projectRoot 'dist\AONexus.exe')
if ($Package) { & (Join-Path $PSScriptRoot 'Package.ps1') }
Write-Host ('Ready: ' + (Join-Path $projectRoot 'dist\AONexus.exe'))
