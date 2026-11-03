param([Parameter(Mandatory=$true)][string]$AOSharpSource,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
[IO.Directory]::CreateDirectory($OutputDirectory)|Out-Null
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
$exe=Join-Path $OutputDirectory 'SecureIpc.Tests.exe'
& $compiler /nologo /target:exe /platform:x86 /langversion:7.3 /r:System.Security.dll "/out:$exe" (Join-Path $AOSharpSource 'AOSharp.Common\IPC\AuthenticatedPacket.cs') (Join-Path $PSScriptRoot 'SecureIpc.Tests.cs')
if($LASTEXITCODE){throw 'Secure IPC tests failed to compile.'}
& $exe
if($LASTEXITCODE){throw 'Secure IPC tests failed.'}
