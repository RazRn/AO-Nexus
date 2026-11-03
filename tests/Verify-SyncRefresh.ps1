$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $root 'logs/SyncRefreshBuild/SyncRefresh.Tests.exe'
[IO.Directory]::CreateDirectory((Split-Path $output))|Out-Null
$compiler=& "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products '*' -find 'MSBuild/Current/Bin/Roslyn/csc.exe'|Select-Object -First 1
& $compiler /nologo /target:exe /platform:x86 /langversion:7.3 "/out:$output" (Join-Path $root 'src/Shared/SyncRefresh.cs') (Join-Path $PSScriptRoot 'SyncRefresh.Tests.cs')
if($LASTEXITCODE){throw 'Sync refresh tests failed to compile'}
& $output
if($LASTEXITCODE){throw 'Sync refresh tests failed'}
Copy-Item -LiteralPath "$root/runtime/Local13Pack/AOSharp/AOSharp.Common.dll" -Destination (Split-Path $output) -Force
$adapter=Join-Path (Split-Path $output) 'SyncRefreshAdapter.Tests.exe'
& $compiler /nologo /nowarn:0436 /target:exe /platform:x86 /langversion:7.3 "/out:$adapter" /r:System.Web.Extensions.dll /r:System.Xml.Linq.dll "/r:$root/runtime/Local13Pack/AOSharp/AOSharp.Common.dll" "$root/src/Shared/SyncRefresh.cs" "$root/src/Shared/PlayTools.cs" "$root/src/Shortcuts/SyncRefreshController.cs" "$PSScriptRoot/SyncRefreshAdapter.Tests.cs"
if($LASTEXITCODE){throw 'Sync refresh adapter tests failed to compile'}
& $adapter
if($LASTEXITCODE){throw 'Sync refresh adapter tests failed'}
