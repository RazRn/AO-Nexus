param([switch]$Live)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = & 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe' -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (-not $compiler) { throw 'Visual Studio C# compiler was not found.' }
$output = Join-Path $root 'logs\ServerNews.Tests.exe'
& $compiler /nologo /target:exe /langversion:7.3 /platform:x86 "/out:$output" /r:System.Net.Http.dll /r:System.Xml.Linq.dll /r:System.Web.Extensions.dll (Join-Path $root 'src\Launcher\ServerNews.cs') (Join-Path $PSScriptRoot 'ServerNews.Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'News test compilation failed.' }
$arguments = @((Join-Path $root 'logs'))
if ($Live) { $arguments += '--live' }
& $output @arguments
if ($LASTEXITCODE -ne 0) { throw 'News checks failed.' }
