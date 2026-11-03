param([string]$PackDirectory = (Join-Path $PSScriptRoot '../runtime/Local13Pack'))
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$launcher=Join-Path $root 'src\Launcher'
$output=Join-Path $root 'logs\PluginStartupFlow.Tests.exe'
[IO.Directory]::CreateDirectory((Split-Path $output)) | Out-Null
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Roslyn compiler not found.' }
[xml]$project=Get-Content -LiteralPath (Join-Path $launcher 'AONexus.csproj')
$arguments=@('/nologo','/target:exe','/platform:x86','/langversion:7.3','/main:AOLauncher.PluginStartupFlowTests',('/out:"'+$output+'"'))
foreach ($reference in $project.Project.ItemGroup.Reference) {
    if ($reference.Include) { $arguments+='/r:'+$reference.Include+'.dll' }
}
foreach ($source in $project.Project.ItemGroup.Compile) {
    if ($source.Include) { $arguments+='"'+[IO.Path]::GetFullPath((Join-Path $launcher $source.Include))+'"' }
}
$arguments+='"'+(Join-Path $PSScriptRoot 'PluginStartupFlow.Tests.cs')+'"'
$arguments+='/resource:"'+(Join-Path $root 'config\servers.json')+'",AOLauncher.servers.json'
foreach ($resource in $project.Project.ItemGroup.EmbeddedResource) {
    if ($resource.Include -like 'Assets\*') { $arguments+='/resource:"'+(Join-Path $launcher $resource.Include)+'",'+$resource.LogicalName }
}
$response=Join-Path $root 'logs\PluginStartupFlow.rsp'
[IO.File]::WriteAllLines($response,$arguments,[Text.UTF8Encoding]::new($false))
. (Join-Path $PSScriptRoot 'CompilerInputs.ps1')
$nexusCompilerInputs = @(Get-NexusCompilerInputs)
& $compiler @nexusCompilerInputs "@$response"
if ($LASTEXITCODE -ne 0) { throw 'Plugin startup flow test compilation failed.' }
& $output $PackDirectory
if ($LASTEXITCODE -ne 0) { throw 'Plugin startup flow tests failed.' }
