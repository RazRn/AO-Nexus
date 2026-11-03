param([string]$PackDirectory = (Join-Path $PSScriptRoot '../runtime/Local13Pack'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$pack = [IO.Path]::GetFullPath($PackDirectory)
$output = Join-Path $root 'logs/ShortcutsExceptions'
$core = Join-Path $output 'AOSharp'
$plugin = Join-Path $output 'Plugin'
[IO.Directory]::CreateDirectory($core) | Out-Null
[IO.Directory]::CreateDirectory($plugin) | Out-Null
# Keep AOSharp and the fixture in different folders, just like real plugin attribution.
Get-ChildItem -LiteralPath (Join-Path $pack 'AOSharp') -Filter '*.dll' | Copy-Item -Destination $core -Force
Copy-Item -LiteralPath (Join-Path $pack 'Plugins/Nexus/NexusShortcuts.dll') -Destination $plugin -Force
$compiler = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'Roslyn compiler not found.' }
$exe = Join-Path $output 'ShortcutsExceptions.Tests.exe'
& $compiler /nologo /target:exe /platform:x86 /langversion:7.3 /optimize+ "/out:$exe" "/r:$core/AOSharp.Core.dll" "/r:$core/AOSharp.Common.dll" "/r:$plugin/NexusShortcuts.dll" (Join-Path $PSScriptRoot 'ShortcutsExceptions.Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Exception fixture compilation failed.' }
[IO.File]::WriteAllText($exe+'.config', '<configuration><runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1"><probing privatePath="AOSharp;Plugin" /></assemblyBinding></runtime></configuration>')
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Exception fixture failed.' }
