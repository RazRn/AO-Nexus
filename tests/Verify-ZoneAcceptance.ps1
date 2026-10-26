$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $root 'logs\zone-acceptance'
[IO.Directory]::CreateDirectory($output)|Out-Null
$install=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -property installationPath
$dev=Join-Path $install 'Common7\Tools\VsDevCmd.bat'
$source=Join-Path $PSScriptRoot 'ZoneAcceptance.Tests.cpp'
$exe=Join-Path $output 'ZoneAcceptance.Tests.exe'
$obj=Join-Path $output 'ZoneAcceptance.Tests.obj'
$command='"'+$dev+'" -arch=x86 -host_arch=x64 >nul && cl.exe /nologo /O2 /MT /EHsc /std:c++17 /W4 /Fe:"'+$exe+'" /Fo:"'+$obj+'" "'+$source+'"'
& $env:ComSpec /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw 'Acceptance parser test compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Acceptance parser validation failed.' }
