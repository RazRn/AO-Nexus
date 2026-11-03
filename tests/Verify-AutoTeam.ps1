$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
if(!$compiler){throw 'Roslyn not found'}
$output=Join-Path $root 'logs\AutoTeam.Tests.exe'
[IO.Directory]::CreateDirectory((Split-Path $output)) | Out-Null
& $compiler /nologo /target:exe /langversion:7.3 "/out:$output" /r:System.Core.dll (Join-Path $root 'src\Shared\AutoTeamPlan.cs') (Join-Path $PSScriptRoot 'AutoTeamPlan.Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Auto team test build failed'}
& $output
if($LASTEXITCODE -ne 0){throw 'Auto team tests failed'}
$recovery=Join-Path $root 'logs\TeamRecovery.Tests.exe'
& $compiler /nologo /target:exe /langversion:7.3 "/out:$recovery" /r:System.Core.dll (Join-Path $root 'src\Shared\AutoTeamPlan.cs') (Join-Path $root 'src\Shared\TeamRecoveryPlan.cs') (Join-Path $root 'src\Shared\TeamRecoveryTracker.cs') (Join-Path $PSScriptRoot 'TeamRecovery.Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Recovery test build failed'}
& $recovery
if($LASTEXITCODE -ne 0){throw 'Recovery tests failed'}
