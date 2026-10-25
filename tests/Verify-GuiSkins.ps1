param([string]$AssemblyPath, [string]$ReviewedArchives)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compiler=& (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe') -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
if(!$compiler){throw 'Visual Studio C# compiler not found'}
$output=Join-Path $root 'logs/GuiSkins.Tests.exe'
[IO.Directory]::CreateDirectory((Split-Path $output))|Out-Null
& $compiler /nologo /target:exe /platform:x86 /langversion:7.3 "/out:$output" /r:System.Net.Http.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll (Join-Path $root 'src/Launcher/GuiSkins.cs') (Join-Path $PSScriptRoot 'GuiSkins.Tests.cs')
if($LASTEXITCODE -ne 0){throw 'GUI skin test build failed'}
if($ReviewedArchives){& $output $ReviewedArchives}else{& $output}
if($LASTEXITCODE -ne 0){throw 'GUI skin checks failed'}
