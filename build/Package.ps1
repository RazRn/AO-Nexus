param([string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$distRoot = Join-Path $projectRoot 'dist'
# A tested staged build can be packaged while Windows locks the installed EXE.
$executable = if ($ExecutablePath) { [IO.Path]::GetFullPath($ExecutablePath) } else { Join-Path $distRoot 'AONexus.exe' }
& (Join-Path $PSScriptRoot 'Build-Payload.ps1') -ValidateOnly -VerifyExecutable $executable
Add-Type -AssemblyName System.IO.Compression.FileSystem
$inputs = @{}
foreach ($name in @('AONexus.exe', 'README.txt', 'CREDITS.txt')) {
    $path = if ($name -eq 'AONexus.exe') { $executable } else { Join-Path $distRoot $name }
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing release input: $name" }
    $inputs[$name] = $path
}
# Only the executable, public documentation and licenses are distributable.
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $distRoot 'Licenses') -File -Recurse) {
    $relative = $file.FullName.Substring($distRoot.Length + 1).Replace('\', '/')
    $inputs[$relative] = $file.FullName
}
$hashes = @{}
$lines = foreach ($name in @($inputs.Keys | Sort-Object)) {
    $hashes[$name] = (Get-FileHash -LiteralPath $inputs[$name] -Algorithm SHA256).Hash.ToLowerInvariant()
    $hashes[$name] + '  ' + $name
}
$manifest = Join-Path (Split-Path -Parent $executable) 'SHA256SUMS.txt'
$lines | Set-Content -LiteralPath $manifest -Encoding ASCII
$inputs['SHA256SUMS.txt'] = $manifest
$hashes['SHA256SUMS.txt'] = (Get-FileHash -LiteralPath $manifest).Hash.ToLowerInvariant()
$temporary = Join-Path $projectRoot ('AO-Nexus-' + [Guid]::NewGuid().ToString('N') + '.zip')
$archive = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in @($inputs.Keys | Sort-Object)) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $inputs[$name], ('AO-Nexus/' + $name), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
$reader = [IO.Compression.ZipFile]::OpenRead($temporary)
try {
    if ($reader.Entries.Count -ne $inputs.Count) { throw 'Unexpected release archive contents.' }
    foreach ($entry in $reader.Entries) {
        $name = $entry.FullName.Substring('AO-Nexus/'.Length)
        if (!$inputs.ContainsKey($name)) { throw 'The package includes an unapproved file.' }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($actual -ne $hashes[$name]) { throw "Release verification failed: $name" }
    }
} finally { $reader.Dispose() }
$destination = Join-Path $projectRoot 'AO-Nexus-Luckyspawn.zip'
Move-Item -LiteralPath $temporary -Destination $destination -Force
Write-Host "Verified release: $destination ($($inputs.Count) public files; no personal settings or passwords)."
