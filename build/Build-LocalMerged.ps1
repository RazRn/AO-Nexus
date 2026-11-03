param([switch]$SkipBuild, [switch]$RebuildBackend)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$package = Split-Path $root
$output = Join-Path $package 'merged-test'
$pack = Join-Path $root 'runtime\Local13Pack'
if (!$SkipBuild) {
    if (!(Test-Path -LiteralPath (Join-Path $pack 'AOSharp\AOSharp.Core.dll'))) { throw 'Restore the local Local13Pack dependencies before building.' }
    & (Join-Path $PSScriptRoot 'Build.ps1') -AOSharpPack $pack -RebuildBackend:$RebuildBackend
}
$exe = Join-Path $root 'dist\AONexus.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Build the merged launcher first.' }
[IO.Directory]::CreateDirectory($output) | Out-Null
Copy-Item -LiteralPath $exe -Destination (Join-Path $output 'AONexus-Test.exe') -Force
Copy-Item -LiteralPath (Join-Path $root 'config\servers.json') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root 'dist\CREDITS.txt') -Destination $output -Force
[IO.Directory]::CreateDirectory((Join-Path $output 'Licenses')) | Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'dist\Licenses') | Copy-Item -Destination (Join-Path $output 'Licenses') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'docs\development\log\Local-1.3-AOVK-Merge.txt') -Destination (Join-Path $package 'README-MERGED.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Start-Merged-Test.cmd') -Destination $package -Force
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath (Join-Path $output 'AONexus-Test.exe') -Algorithm SHA256).Hash -ne $hash) { throw 'Packaged launcher does not match the build.' }
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), $hash + '  AONexus-Test.exe' + [Environment]::NewLine)
Write-Host ('Local test ready: ' + (Join-Path $package 'Start-Merged-Test.cmd'))
Write-Host ('SHA256: ' + $hash)
