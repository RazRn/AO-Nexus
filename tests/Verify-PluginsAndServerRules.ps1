param([string]$PackDirectory, [string]$PayloadDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$launcher=Join-Path $root 'src\Launcher'
$output=Join-Path $root 'logs\PluginsAndServerRules.Tests.exe'
[IO.Directory]::CreateDirectory((Split-Path $output)) | Out-Null
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Roslyn compiler not found.' }
[xml]$project=Get-Content -LiteralPath (Join-Path $launcher 'AONexus.csproj')
$arguments=@('/nologo','/target:exe','/platform:x86','/langversion:7.3','/main:AOLauncher.PluginsAndServerRulesTests',('/out:"'+$output+'"'))
foreach ($reference in $project.Project.ItemGroup.Reference) {
    if ($reference.Include) { $arguments+='/r:'+$reference.Include+'.dll' }
}
foreach ($source in $project.Project.ItemGroup.Compile) {
    if ($source.Include) { $arguments+='"'+[IO.Path]::GetFullPath((Join-Path $launcher $source.Include))+'"' }
}
$arguments+='"'+(Join-Path $PSScriptRoot 'PluginsAndServerRules.Tests.cs')+'"'
$arguments+='/resource:"'+(Join-Path $root 'config\servers.json')+'",AOLauncher.servers.json'
foreach ($resource in $project.Project.ItemGroup.EmbeddedResource) {
    if ($resource.Include -like 'Assets\*') { $arguments+='/resource:"'+(Join-Path $launcher $resource.Include)+'",'+$resource.LogicalName }
}
$response=Join-Path $root 'logs\PluginsAndServerRules.rsp'
[IO.File]::WriteAllLines($response,$arguments,[Text.UTF8Encoding]::new($false))
. (Join-Path $PSScriptRoot 'CompilerInputs.ps1')
$nexusCompilerInputs = @(Get-NexusCompilerInputs)
& $compiler @nexusCompilerInputs "@$response"
if ($LASTEXITCODE -ne 0) { throw 'Plugin/server-rule test compilation failed.' }
$run=@()
if ($PackDirectory) { $run+='--pack'; $run+=[IO.Path]::GetFullPath($PackDirectory) }
if ($PayloadDirectory) { $run+='--payload'; $run+=[IO.Path]::GetFullPath($PayloadDirectory) }
& $output @run
if ($LASTEXITCODE -ne 0) { throw 'Plugin/server-rule tests failed.' }
