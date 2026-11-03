param([Parameter(Mandatory=$true)][string]$PackDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$launcher=Join-Path $root 'src\Launcher'
$output=Join-Path $root 'logs\PluginTheme.Tests.exe'
[IO.Directory]::CreateDirectory((Split-Path $output)) | Out-Null
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Roslyn compiler not found.' }
[xml]$project=Get-Content -LiteralPath (Join-Path $launcher 'AONexus.csproj')
$arguments=@('/nologo','/target:exe','/platform:x86','/langversion:7.3','/main:AOLauncher.PluginThemeTests',('/out:"'+$output+'"'))
foreach ($reference in $project.Project.ItemGroup.Reference) {
    if ($reference.Include) { $arguments+='/r:'+$reference.Include+'.dll' }
}
foreach ($source in $project.Project.ItemGroup.Compile) {
    if ($source.Include) { $arguments+='"'+[IO.Path]::GetFullPath((Join-Path $launcher $source.Include))+'"' }
}
$arguments+='"'+(Join-Path $PSScriptRoot 'PluginTheme.Tests.cs')+'"'
$arguments+='/resource:"'+(Join-Path $root 'config\servers.json')+'",AOLauncher.servers.json'
foreach ($resource in $project.Project.ItemGroup.EmbeddedResource) {
    if ($resource.Include -like 'Assets\*') { $arguments+='/resource:"'+(Join-Path $launcher $resource.Include)+'",'+$resource.LogicalName }
}
$response=Join-Path $root 'logs\PluginTheme.rsp'
[IO.File]::WriteAllLines($response,$arguments,[Text.UTF8Encoding]::new($false))
. (Join-Path $PSScriptRoot 'CompilerInputs.ps1')
$nexusCompilerInputs = @(Get-NexusCompilerInputs)
& $compiler @nexusCompilerInputs "@$response"
if ($LASTEXITCODE -ne 0) { throw 'Plugin theme test compilation failed.' }
$run=@()
if ($PackDirectory) { $run+='--pack'; $run+=[IO.Path]::GetFullPath($PackDirectory) }
& $output @run
if ($LASTEXITCODE -ne 0) { throw 'Plugin theme tests failed.' }
