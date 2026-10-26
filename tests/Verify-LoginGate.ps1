$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $root 'logs\login-gate'
[IO.Directory]::CreateDirectory($output)|Out-Null
$install=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -property installationPath
$dev=Join-Path $install 'Common7\Tools\VsDevCmd.bat'
$source=Join-Path $PSScriptRoot 'LoginGate.Tests.cpp'
$exe=Join-Path $output 'LoginGate.Tests.exe'
$obj=Join-Path $output 'LoginGate.Tests.obj'
$command='"'+$dev+'" -arch=x86 -host_arch=x64 >nul && cl.exe /nologo /O2 /MT /EHsc /std:c++17 /W4 /Fe:"'+$exe+'" /Fo:"'+$obj+'" "'+$source+'" user32.lib'
& $env:ComSpec /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw 'Login gate test compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Login gate validation failed.' }
