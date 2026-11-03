$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $root 'logs/FormationBuild/FormationLease.Tests.exe'
[IO.Directory]::CreateDirectory((Split-Path $output))|Out-Null
$compiler=& "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products '*' -find 'MSBuild/Current/Bin/Roslyn/csc.exe'|Select-Object -First 1
$common=Join-Path $root 'runtime/Local13Pack/AOSharp/AOSharp.Common.dll'
Copy-Item -LiteralPath $common -Destination (Join-Path (Split-Path $output) 'AOSharp.Common.dll') -Force
& $compiler /nologo /target:exe /platform:x86 /langversion:7.3 "/r:RealAOCommon=$common" "/out:$output" (Join-Path $root 'src/Shared/PanelFormation.cs') (Join-Path $root 'src/Shared/SyncRefresh.cs') (Join-Path $root 'src/Shortcuts/FormationMovementLease.cs') (Join-Path $PSScriptRoot 'FormationLease.Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Formation fixture compilation failed'}
& $output
if($LASTEXITCODE -ne 0){throw 'Formation fixture failed'}
