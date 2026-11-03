param([ValidateSet('baseline','serial-fast','pipeline-2','pipeline-6')][string]$Mode='baseline',[ValidateSet(1,6)][int]$Count=6,[string]$BackendDirectory='runtime\NexusEngine',[switch]$Run,[switch]$CompileOnly)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$launcher=Join-Path $root 'src\Launcher'
$output=Join-Path $root 'logs\StartupBenchmark.exe'
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
[xml]$project=Get-Content -LiteralPath (Join-Path $launcher 'AONexus.csproj')
$arguments=@('/nologo','/target:exe','/platform:x86','/langversion:7.3','/main:AOLauncher.StartupBenchmark',('/out:"'+$output+'"'))
foreach($reference in $project.Project.ItemGroup.Reference){if($reference.Include){$arguments+='/r:'+$reference.Include+'.dll'}}
foreach($source in $project.Project.ItemGroup.Compile){if($source.Include){$arguments+='"'+[IO.Path]::GetFullPath((Join-Path $launcher $source.Include))+'"'}}
$arguments+='"'+(Join-Path $PSScriptRoot 'Measure-Startup.cs')+'"'
$arguments+='/resource:"'+(Join-Path $root 'config\servers.json')+'",AOLauncher.servers.json'
$response=Join-Path $root 'logs\StartupBenchmark.rsp'
[IO.File]::WriteAllLines($response,$arguments,[Text.UTF8Encoding]::new($false))
& $compiler "@$response"
if($LASTEXITCODE){throw 'Benchmark compilation failed'}
if($CompileOnly){return}
$log=Join-Path $root ('logs\startup-'+$Mode+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
& $output $Mode $Count ([IO.Path]::GetFullPath($BackendDirectory)) $log $(if($Run){'run'}else{'validate'})
if($LASTEXITCODE){throw 'Benchmark failed; inspect its redacted timing log'}
