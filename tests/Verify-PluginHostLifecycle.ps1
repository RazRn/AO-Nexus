$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compiler=& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
$output=Join-Path $root 'logs\PluginHostLifecycle.Tests.exe'
[IO.Directory]::CreateDirectory((Split-Path $output)) | Out-Null
& $compiler /nologo /target:exe /platform:x86 /langversion:7.3 /main:PluginHostLifecycleTests "/out:$output" /r:System.Web.Extensions.dll (Join-Path $root 'src\PluginHost\NexusPluginHost.cs') (Join-Path $PSScriptRoot 'PluginHostLifecycle.Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Plugin host test compilation failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Plugin host lifecycle checks failed.' }
