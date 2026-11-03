param(
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [switch]$ValidateOnly,
    [switch]$Restore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
# Local repair only. Never downloads a client or disables launcher integrity checks.
$stockHash = '370C0670CC9CB46626EF24692376AAF492BB1787BAD8A1125365A6BE4F663862'
$largeHash = '60647245852D50F6EEA322F2CB49971C73646E2B6EFA0AAE9F7385CE85AC8424'
$folder = [IO.Path]::GetFullPath($GameDirectory)
$path = Join-Path $folder 'AnarchyOnline.exe'
$current = Get-Item -LiteralPath $path
while ($null -ne $current) {
    if ($current.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked game paths are not supported.' }
    $current = if ($current -is [IO.FileInfo]) { $current.Directory } else { $current.Parent }
}
if (![Environment]::Is64BitOperatingSystem) { throw 'This repair requires 64-bit Windows.' }
function Get-BytesHash([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '') }
    finally { $sha.Dispose() }
}
$access = if ($ValidateOnly) { [IO.FileAccess]::Read } else { [IO.FileAccess]::ReadWrite }
$share = if ($ValidateOnly) { [IO.FileShare]::ReadWrite } else { [IO.FileShare]::None }
try { $stream = [IO.File]::Open($path, [IO.FileMode]::Open, $access, $share) }
catch { throw "Cannot open the game executable for this repair. Close all clients from this folder first. $($_.Exception.Message)" }
try {
    if ($stream.Length -gt 32MB) { throw 'Unexpected executable size.' }
    $reader = New-Object IO.BinaryReader($stream)
    $original = $reader.ReadBytes([int]$stream.Length)
    $before = Get-BytesHash $original
    if ($before -ne $stockHash -and $before -ne $largeHash) { throw 'Unsupported client: the complete executable fingerprint must match the verified classic build.' }
    $pe = [BitConverter]::ToInt32($original, 0x3c)
    if ([BitConverter]::ToUInt16($original, 0) -ne 0x5a4d -or
        [BitConverter]::ToUInt32($original, $pe) -ne 0x4550 -or
        [BitConverter]::ToUInt16($original, $pe + 4) -ne 0x14c) { throw 'Expected a classic x86 PE executable.' }
    $offset = $pe + 22
    $desired = if ($Restore) { $stockHash } else { $largeHash }
    $patched = [byte[]]$original.Clone()
    $patched[$offset] = if ($Restore) { $patched[$offset] -band 0xdf } else { $patched[$offset] -bor 0x20 }
    if ((Get-BytesHash $patched) -ne $desired) { throw 'Repair would change more than the verified large-address flag.' }
    if ($ValidateOnly -or $before -eq $desired) {
        [pscustomobject]@{ Path=$path; CurrentHash=$before; TargetHash=$desired; ChangeNeeded=($before -ne $desired); ValidateOnly=[bool]$ValidateOnly }
        return
    }
    $backup = $path + '.nexus-memory-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N') + '.bak'
    [IO.File]::WriteAllBytes($backup, $original)
    if ((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne $before) { throw 'Backup verification failed; game unchanged.' }
    try {
        $stream.Position = $offset
        $stream.WriteByte($patched[$offset])
        $stream.Flush($true)
        $stream.Position = 0
        if ((Get-BytesHash ($reader.ReadBytes([int]$stream.Length))) -ne $desired) { throw 'Post-write verification failed.' }
    } catch {
        $stream.Position = $offset
        $stream.WriteByte($original[$offset])
        $stream.Flush($true)
        throw
    }
    [pscustomobject]@{ Path=$path; Hash=$desired; Backup=$backup; LargeAddressAware=(!$Restore) }
} finally { $stream.Dispose() }
