param([string]$AOSharpRuntime, [string]$OutputDirectory = (Join-Path $PSScriptRoot '../dist/Shortcuts'))
$ErrorActionPreference = 'Stop'
if (!$AOSharpRuntime) { $AOSharpRuntime = (& "$PSScriptRoot/Restore-TelemetryDependency.ps1" | Select-Object -Last 1) }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtime = [IO.Path]::GetFullPath($AOSharpRuntime)
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$compiler = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Roslyn compiler not found.' }
& $compiler /nologo /target:library /platform:x86 /langversion:7.3 /optimize+ /deterministic+ "/out:$output\NexusShortcuts.dll" /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Xml.Linq.dll /r:System.Xml.dll "/r:$runtime\AOSharp.Core.dll" "/r:$runtime\AOSharp.Common.dll" (Join-Path $root 'src/Shortcuts/NexusShortcutsPlugin.cs') (Join-Path $root 'src/Shortcuts/NexusPluginUi.cs') (Join-Path $root 'src/Shortcuts/NexusTools.cs') (Join-Path $root 'src/Shortcuts/SyncFormationControls.cs') (Join-Path $root 'src/Shortcuts/NexusFormation.cs') (Join-Path $root 'src/Shortcuts/PanelFormationController.cs') (Join-Path $root 'src/Shortcuts/FormationMovementLease.cs') (Join-Path $root 'src/Shared/PanelFormation.cs') (Join-Path $root 'src/Shared/SyncRefresh.cs') (Join-Path $root 'src/Shortcuts/SyncRefreshController.cs') (Join-Path $root 'src/Shared/PlayTools.cs') (Join-Path $root 'src/Shared/AutoTeamPlan.cs') (Join-Path $root 'src/Shared/TeamRecoveryPlan.cs') (Join-Path $root 'src/Shortcuts/NexusAutoTeam.cs') (Join-Path $root 'src/Shortcuts/ClientTeamRoster.cs') (Join-Path $root 'src/Shortcuts/CastPostureGuard.cs') (Join-Path $root 'src/Shared/CastPostureRecovery.cs') (Join-Path $root 'src/Shared/GuiTeamRoster.cs') (Join-Path $root 'src/Telemetry/SyncMovementProbe.cs') (Join-Path $root 'src/Common/MovementSnapshot.cs')
if ($LASTEXITCODE -ne 0) { throw 'Nexus Shortcuts compilation failed.' }
Write-Host "Built $output\NexusShortcuts.dll"
