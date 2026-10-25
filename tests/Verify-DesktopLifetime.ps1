param([string]$Compiler)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Compiler) {
    $Compiler = & (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe') -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
$output = Join-Path $root 'logs\DesktopLifetime.Tests.exe'
& $Compiler /nologo /target:exe /platform:x86 /langversion:7.3 "/out:$output" /reference:System.Windows.Forms.dll (Join-Path $root 'src\Launcher\DesktopLifetime.cs') (Join-Path $PSScriptRoot 'DesktopLifetime.Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Desktop lifetime test compilation failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Desktop lifetime tests failed.' }
