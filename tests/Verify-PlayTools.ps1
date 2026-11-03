param([string]$AOSharpRuntime)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$out=Join-Path $root 'logs/PlayTools'
[IO.Directory]::CreateDirectory($out)|Out-Null
if (!$AOSharpRuntime) { $AOSharpRuntime = (& "$root/build/Restore-TelemetryDependency.ps1" | Select-Object -Last 1) }
$runtime=[IO.Path]::GetFullPath($AOSharpRuntime)
Get-ChildItem -LiteralPath $runtime -Filter '*.dll' | Copy-Item -Destination $out -Force
$compiler=& "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products '*' -find 'MSBuild/Current/Bin/Roslyn/csc.exe' | Select-Object -First 1
& $compiler /nologo /target:exe /platform:x86 /langversion:7.3 /r:System.Web.Extensions.dll /r:System.Xml.Linq.dll /r:System.Xml.dll "/out:$out/PlayTools.Tests.exe" "/r:$runtime/AOSharp.Core.dll" "/r:$runtime/AOSharp.Common.dll" (Join-Path $root 'src/Shared/PlayTools.cs') (Join-Path $root 'src/Shortcuts/NexusTools.cs') (Join-Path $root 'src/Shortcuts/SyncFormationControls.cs') (Join-Path $root 'src/Shortcuts/NexusFormation.cs') (Join-Path $root 'src/Shortcuts/ClientTeamRoster.cs') (Join-Path $root 'src/Shared/GuiTeamRoster.cs') (Join-Path $root 'src/Shortcuts/PanelFormationController.cs') (Join-Path $root 'src/Shortcuts/FormationMovementLease.cs') (Join-Path $root 'src/Shared/PanelFormation.cs') (Join-Path $root 'src/Shared/SyncRefresh.cs') (Join-Path $root 'src/Shortcuts/SyncRefreshController.cs') (Join-Path $root 'src/Shortcuts/NexusPluginUi.cs') (Join-Path $root 'src/Telemetry/SyncMovementProbe.cs') (Join-Path $root 'src/Common/MovementSnapshot.cs') (Join-Path $PSScriptRoot 'PlayTools.Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Play tools fixture compilation failed'}
& (Join-Path $out 'PlayTools.Tests.exe')
if($LASTEXITCODE -ne 0){throw 'Play tools fixture failed'}
