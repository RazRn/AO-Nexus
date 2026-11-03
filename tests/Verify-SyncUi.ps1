$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$out=Join-Path $root 'logs/SyncUi'
[IO.Directory]::CreateDirectory($out)|Out-Null
$runtime=Join-Path $root 'runtime/Local13Pack/AOSharp'
Get-ChildItem -LiteralPath $runtime -Filter '*.dll' | Copy-Item -Destination $out -Force
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
& $compiler /nologo /target:exe /platform:x86 /langversion:7.3 "/out:$out/SyncUi.Tests.exe" "/r:$runtime/AOSharp.Core.dll" "/r:$runtime/AOSharp.Common.dll" (Join-Path $root 'src/Shortcuts/NexusPluginUi.cs') (Join-Path $PSScriptRoot 'SyncUi.Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Sync UI fixture compilation failed'}
& (Join-Path $out 'SyncUi.Tests.exe')
if($LASTEXITCODE -ne 0){throw 'Sync UI fixture failed'}
