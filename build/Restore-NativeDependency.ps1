# LoginHandler uses the same Detours NuGet package as the original backend.
# Restore its pinned package instead of keeping third-party binaries in Git.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packagesRoot = Join-Path $projectRoot 'src\Backend\packages'
$packageRoot = Join-Path $packagesRoot 'Detours.4.0.1'
$required = @('build\native\Detours.targets', 'lib\native\include\detours.h', 'lib\native\libs\x86\detours.lib')

if (Test-Path -LiteralPath $packageRoot) {
    foreach ($relative in $required) {
        if (!(Test-Path -LiteralPath (Join-Path $packageRoot $relative) -PathType Leaf)) {
            throw "The Detours package cache is incomplete. Remove '$packageRoot' and run the build again."
        }
    }
    Write-Host 'Detours 4.0.1 is already restored.'
    return
}

[IO.Directory]::CreateDirectory($packagesRoot) | Out-Null
$archive = Join-Path $packagesRoot ('.detours-restore-' + [Guid]::NewGuid().ToString('N') + '.zip')
$previousProtocol = [Net.ServicePointManager]::SecurityProtocol
try {
    [Net.ServicePointManager]::SecurityProtocol = $previousProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/detours/4.0.1/detours.4.0.1.nupkg' -UseBasicParsing -OutFile $archive
    $expectedHash = '178848D6D13AD1A83CED5A3AA24E73D76A5F113644E151B9C43BA132999F9118'
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'Detours package checksum mismatch. The downloaded package was not extracted.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $packageRoot)
    foreach ($relative in $required) {
        if (!(Test-Path -LiteralPath (Join-Path $packageRoot $relative) -PathType Leaf)) {
            throw "The restored Detours package is missing '$relative'."
        }
    }
    Write-Host 'Restored Detours 4.0.1 from NuGet (SHA-256 verified).'
}
finally {
    [Net.ServicePointManager]::SecurityProtocol = $previousProtocol
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
}
