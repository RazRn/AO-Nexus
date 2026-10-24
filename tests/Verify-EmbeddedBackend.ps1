param(
    [string]$Compiler = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe',
    [string]$LauncherPath
)
$ErrorActionPreference = 'Stop'
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('AOLauncher-EmbeddedBackendTest-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
$exePath = Join-Path $testDirectory 'EmbeddedBackendTests.exe'
$framework = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$references = @('mscorlib', 'System', 'System.Core', 'System.IO.Compression', 'System.IO.Compression.FileSystem', 'System.Web.Extensions') | ForEach-Object { "/reference:$framework\$_.dll" }
& $Compiler /nologo /langversion:7.3 /target:exe /platform:x86 /nostdlib+ "/out:$exePath" @references (Join-Path $PSScriptRoot '..\src\Launcher\EmbeddedBackend.cs') (Join-Path $PSScriptRoot 'EmbeddedBackend.Tests.cs') (Join-Path $PSScriptRoot '..\src\Common\Texts.cs')
if ($LASTEXITCODE -ne 0) { throw 'Embedded backend harness compilation failed.' }
& $exePath (Join-Path $testDirectory 'synthetic')
if ($LASTEXITCODE -ne 0) { throw 'Embedded backend regression failed.' }

# A Windows junction works without elevation; both its target and link are isolated test fixtures.
$junctionTarget = Join-Path $testDirectory 'junction-target'
$junctionPath = Join-Path $testDirectory 'junction-cache'
[IO.Directory]::CreateDirectory($junctionTarget) | Out-Null
New-Item -ItemType Junction -Path $junctionPath -Target $junctionTarget | Out-Null
& $exePath --reparse (Join-Path $junctionPath 'runtime')
if ($LASTEXITCODE -ne 0) { throw 'Linked cache ancestor was not rejected.' }
if (@(Get-ChildItem -LiteralPath $junctionTarget -Force).Count -ne 0) { throw 'Linked cache target was unexpectedly modified.' }

if ($LauncherPath) {
    $absoluteLauncher = (Resolve-Path -LiteralPath $LauncherPath).Path
    $portableDirectory = Join-Path $testDirectory 'Portable launcher - æøå'
    [IO.Directory]::CreateDirectory($portableDirectory) | Out-Null
    $portableLauncher = Join-Path $portableDirectory 'AOLauncher.exe'
    Copy-Item -LiteralPath $absoluteLauncher -Destination $portableLauncher
    & $exePath --embedded $portableLauncher (Join-Path $testDirectory 'real-payload-cache')
    if ($LASTEXITCODE -ne 0) { throw 'Real embedded launcher payload validation failed.' }
}
"Test artifacts: $testDirectory"
