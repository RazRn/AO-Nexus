param([Parameter(Mandatory=$true)][string]$GameDirectory)
$ErrorActionPreference='Stop'
$game=(Resolve-Path -LiteralPath $GameDirectory).Path
$expected='9D9E0DA25AC6F8C7ECDCE7A47A2409575945966A29E3B77850F7C62033A04BE3'
if ((Get-FileHash -LiteralPath (Join-Path $game 'randy31.dll') -Algorithm SHA256).Hash -ne $expected) {
    throw 'This equivalence test requires the verified classic randy31.dll.'
}
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $root 'logs\pixel-equivalence'
[IO.Directory]::CreateDirectory($output)|Out-Null
$install=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -property installationPath
$dev=Join-Path $install 'Common7\Tools\VsDevCmd.bat'
$source=Join-Path $PSScriptRoot 'FastBilinear.Tests.cpp'
$exe=Join-Path $output 'FastBilinear.Tests.exe'
$obj=Join-Path $output 'FastBilinear.Tests.obj'
$command='"'+$dev+'" -arch=x86 -host_arch=x64 >nul && cl.exe /nologo /O2 /MT /EHsc /std:c++17 /W4 /Fe:"'+$exe+'" /Fo:"'+$obj+'" "'+$source+'"'
& $env:ComSpec /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw 'Pixel test compilation failed.' }
# Loads the original local DLL but does not start AO or connect to a server.
$result=& $exe $game
if ($LASTEXITCODE -ne 0) { throw 'Pixel equivalence failed.' }
$result | Set-Content -LiteralPath (Join-Path $output 'result.json')
$result
