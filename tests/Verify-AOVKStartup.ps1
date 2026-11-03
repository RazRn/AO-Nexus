param([string]$StockGame, [string]$AOVKGame)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $root 'logs\aovk-startup-tests'
[IO.Directory]::CreateDirectory($output) | Out-Null
$install = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -property installationPath
$dev = Join-Path $install 'Common7\Tools\VsDevCmd.bat'
$source = Join-Path $PSScriptRoot 'AOVKStartup.Tests.cpp'
$exe = Join-Path $output 'AOVKStartup.Tests.exe'
$obj = Join-Path $output 'AOVKStartup.Tests.obj'
$command = '"'+$dev+'" -arch=x86 -host_arch=x64 >nul && cl.exe /nologo /O2 /MT /EHsc /std:c++17 /W4 /Fe:"'+$exe+'" /Fo:"'+$obj+'" "'+$source+'"'
& $env:ComSpec /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw 'Native startup test compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Native startup tests failed.' }
$project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
  <ItemGroup><Compile Include="../../tests/AOVKStartup.Tests.cs"/><Compile Include="../../src/Backend/NexusEngine/FastStartup.cs"/></ItemGroup>
</Project>
'@
$projectPath = Join-Path $output 'StartupTests.csproj'
[IO.File]::WriteAllText($projectPath, $project)
$localSdk = Join-Path $env:LOCALAPPDATA 'AOSharpBuildTools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet).Source }
if ($StockGame -and $AOVKGame) { & $dotnet run --project $projectPath -- $StockGame $AOVKGame }
else { & $dotnet run --project $projectPath }
if ($LASTEXITCODE -ne 0) { throw 'Managed startup tests failed.' }
