param(
    [string]$BackendDirectory = (Join-Path $PSScriptRoot '..\runtime\AOQuickLauncher'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\Launcher\obj\EmbeddedBackend'),
    [switch]$ValidateOnly,
    [string]$VerifyExecutable,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$backendRoot = [IO.Path]::GetFullPath($BackendDirectory).TrimEnd('\')
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
$licensePattern = '^(LICENSE|LICENCE|COPYING|NOTICE|THIRD-PARTY-NOTICES)(\.(txt|md|rst))?$|^FASM-LICENSE\.TXT$'

function Assert-OrdinaryPath([string]$Path, [switch]$File) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($File -and ($item.PSIsContainer -or $item.Length -eq 0)) { throw "Missing or empty payload input: $Path" }
    # OneDrive cloud placeholders are allowed, but links and junctions are not.
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        if (!('AOLauncherPayload.NativeTags' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace AOLauncherPayload {
    public static class NativeTags {
        [StructLayout(LayoutKind.Sequential)] private struct Info { public uint Attributes; public uint Tag; }
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out Info info, uint size);
        public static bool IsCloud(string path) {
            using (var handle = CreateFileW(path, 0x80, 7, IntPtr.Zero, 3, 0x00200000 | 0x02000000, IntPtr.Zero)) {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                Info info;
                if (!GetFileInformationByHandleEx(handle, 9, out info, 8)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return (info.Tag & ~0x0000F000u) == 0x9000001Au;
            }
        }
    }
}
'@
        }
        if (![AOLauncherPayload.NativeTags]::IsCloud($item.FullName)) { throw "Payload paths must not use links or junctions: $Path" }
    }
}

function Assert-Ancestors([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($null -ne $current) {
        if (Test-Path -LiteralPath $current) { Assert-OrdinaryPath $current }
        $parent = [IO.Directory]::GetParent($current)
        $current = if ($null -eq $parent) { $null } else { $parent.FullName }
    }
}

function Get-StreamHash([IO.Stream]$Stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($Stream)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

Assert-Ancestors $backendRoot
foreach ($name in @('AOQuickLauncher.deps.json', 'AOQuickLauncher.runtimeconfig.json')) { Assert-OrdinaryPath (Join-Path $backendRoot $name) -File }
$deps = Get-Content -LiteralPath (Join-Path $backendRoot 'AOQuickLauncher.deps.json') -Raw | ConvertFrom-Json
$runtimeConfig = Get-Content -LiteralPath (Join-Path $backendRoot 'AOQuickLauncher.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.tfm -ne 'net10.0' -or
    !($runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'includedFrameworks')) {
    throw 'Publish the win-x86 backend with --self-contained true before bundling it.'
}
if ($deps.runtimeTarget.name -notmatch '/win-x86$') { throw 'The embedded backend must target win-x86.' }

# The dependency manifest is the allowlist. Add the apphost, our native login
# handler and Reloaded.Assembler native content, which are not runtime entries.
# Do not enumerate arbitrary binaries or JSON from a used installation.
$relativePaths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($name in @('AOQuickLauncher.exe', 'AOQuickLauncher.deps.json', 'AOQuickLauncher.runtimeconfig.json',
    'LoginHandler.dll', 'FASM.DLL', 'FASMX64.DLL', 'FASM-LICENSE.TXT')) { [void]$relativePaths.Add($name) }
foreach ($target in $deps.targets.PSObject.Properties) {
    foreach ($library in $target.Value.PSObject.Properties) {
        foreach ($kind in @('runtime', 'native', 'runtimeTargets')) {
            $group = $library.Value.PSObject.Properties[$kind]
            if ($null -eq $group) { continue }
            foreach ($asset in $group.Value.PSObject.Properties) {
                $relative = $asset.Name.Replace('\', '/')
                $name = [IO.Path]::GetFileName($relative)
                if ($name -eq '_._') { continue }
                if ($relative -match '(^/|:|(^|/)\.\.(/|$))' -or [IO.Path]::GetExtension($name) -notin @('.dll', '.exe')) {
                    throw "Unexpected runtime asset path: $relative"
                }
                # SDK publish flattens package lib/runtimes paths. Preserve a
                # relative subdirectory only when it is present in the output.
                if (!(Test-Path -LiteralPath (Join-Path $backendRoot $relative) -PathType Leaf)) { $relative = $name }
                [void]$relativePaths.Add($relative)
            }
        }
    }
}
foreach ($license in @(Get-ChildItem -LiteralPath $backendRoot -File | Where-Object Name -match $licensePattern)) {
    [void]$relativePaths.Add($license.Name)
}
$orderedPaths = [string[]]@($relativePaths)
[Array]::Sort($orderedPaths, [StringComparer]::Ordinal)
$expected = @{}
$files = @($orderedPaths | ForEach-Object {
    $path = Join-Path $backendRoot $_
    Assert-Ancestors ([IO.Path]::GetDirectoryName($path))
    Assert-OrdinaryPath $path -File
    $entry = [pscustomobject][ordered]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(); Length = (Get-Item -LiteralPath $path).Length }
    $expected[$_] = $entry
    $entry
})
foreach ($required in @('AOQuickLauncher.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'CommandLine.dll', 'Reloaded.Injector.dll')) {
    if (!$expected.ContainsKey($required)) { throw "Required backend runtime asset is missing: $required" }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-Bundle([IO.Stream]$ZipStream, [string]$ManifestText) {
    $manifest = $ManifestText | ConvertFrom-Json
    if ((Get-StreamHash $ZipStream) -ne $manifest.PayloadSha256) { throw 'Embedded ZIP checksum differs from its manifest.' }
    $ZipStream.Position = 0
    if (@($manifest.Files).Count -ne $files.Count) { throw 'Embedded backend file count is stale. Regenerate the payload and rebuild AOLauncher.' }
    $manifestEntries = @{}
    foreach ($entry in $manifest.Files) {
        if (!$expected.ContainsKey($entry.Path) -or $manifestEntries.ContainsKey($entry.Path)) { throw "Unexpected or duplicate embedded backend file: $($entry.Path)" }
        $wanted = $expected[$entry.Path]
        if ($entry.Sha256 -ne $wanted.Sha256 -or $entry.Length -ne $wanted.Length) { throw "Embedded backend is stale: $($entry.Path). Regenerate the payload and rebuild AOLauncher." }
        $manifestEntries[$entry.Path] = $entry
    }
    $archive = New-Object IO.Compression.ZipArchive($ZipStream, [IO.Compression.ZipArchiveMode]::Read, $true)
    try {
        if ($archive.Entries.Count -ne $expected.Count) { throw 'Embedded ZIP file count differs from its allowlist.' }
        $seen = @{}
        foreach ($entry in $archive.Entries) {
            if (!$expected.ContainsKey($entry.FullName) -or $seen.ContainsKey($entry.FullName)) { throw "Unexpected or duplicate ZIP entry: $($entry.FullName)" }
            $seen[$entry.FullName] = $true
            $stream = $entry.Open()
            try {
                $wanted = $expected[$entry.FullName]
                if ($entry.Length -ne $wanted.Length -or (Get-StreamHash $stream) -ne $wanted.Sha256) { throw "Embedded ZIP data differs from the published backend: $($entry.FullName)" }
            } finally { $stream.Dispose() }
        }
    } finally { $archive.Dispose() }
}

function Read-EmbeddedResource([string]$Executable, [string]$Name) {
    # Metadata/resource inspection only: never execute GUI code or load settings.
    if ($PSVersionTable.PSEdition -eq 'Core') {
        $stream = [IO.File]::OpenRead($Executable)
        $pe = New-Object System.Reflection.PortableExecutable.PEReader($stream)
        try {
            $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            foreach ($handle in $metadata.ManifestResources) {
                $resource = $metadata.GetManifestResource($handle)
                if ($metadata.GetString($resource.Name) -ne $Name) { continue }
                if (!$resource.Implementation.IsNil) { throw "Expected an embedded resource: $Name" }
                $section = $pe.GetSectionData($pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress)
                $reader = $section.GetReader([int]$resource.Offset, $section.Length - [int]$resource.Offset)
                $length = $reader.ReadInt32()
                return ,($reader.ReadBytes($length))
            }
        } finally { $pe.Dispose(); $stream.Dispose() }
    } else {
        $assembly = [Reflection.Assembly]::ReflectionOnlyLoad([IO.File]::ReadAllBytes($Executable))
        $stream = $assembly.GetManifestResourceStream($Name)
        if ($null -ne $stream) {
            $memory = New-Object IO.MemoryStream
            try { $stream.CopyTo($memory); return ,($memory.ToArray()) }
            finally { $stream.Dispose(); $memory.Dispose() }
        }
    }
    throw "Missing $Name in AOLauncher.exe. Build the portable launcher before packaging."
}

if ($VerifyExecutable) {
    Assert-Ancestors ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($VerifyExecutable)))
    Assert-OrdinaryPath $VerifyExecutable -File
    $zipBytes = Read-EmbeddedResource $VerifyExecutable 'AOLauncher.Backend.zip'
    $manifestBytes = Read-EmbeddedResource $VerifyExecutable 'AOLauncher.Backend.manifest.json'
    $zipStream = New-Object IO.MemoryStream(,$zipBytes)
    try { Assert-Bundle $zipStream ([Text.Encoding]::UTF8.GetString($manifestBytes)) }
    finally { $zipStream.Dispose() }
    Write-Host "Portable launcher verified against $($files.Count) published backend files: $VerifyExecutable"
} elseif ($ValidateOnly) {
    $zipPath = Join-Path $outputRoot 'Backend.zip'
    $manifestPath = Join-Path $outputRoot 'Backend.manifest.json'
    Assert-Ancestors $outputRoot
    Assert-OrdinaryPath $zipPath -File
    Assert-OrdinaryPath $manifestPath -File
    $zipStream = [IO.File]::OpenRead($zipPath)
    try { Assert-Bundle $zipStream (Get-Content -LiteralPath $manifestPath -Raw) }
    finally { $zipStream.Dispose() }
    Write-Host "Embedded payload verified: $($files.Count) runtime files."
} else {
    Assert-Ancestors $outputRoot
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    $zipPath = Join-Path $outputRoot 'Backend.zip'
    $manifestPath = Join-Path $outputRoot 'Backend.manifest.json'
    foreach ($path in @($zipPath, $manifestPath)) { if (Test-Path -LiteralPath $path) { Assert-OrdinaryPath $path -File } }
    $pendingZip = Join-Path $outputRoot ('Backend-' + [Guid]::NewGuid().ToString('N') + '.zip')
    $archive = [IO.Compression.ZipFile]::Open($pendingZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $files) {
            $zipEntry = $archive.CreateEntry($entry.Path, [IO.Compression.CompressionLevel]::Optimal)
            $zipEntry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $input = [IO.File]::OpenRead((Join-Path $backendRoot $entry.Path))
            $output = $zipEntry.Open()
            try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
        }
    } finally { $archive.Dispose() }
    $manifest = [ordered]@{ PayloadSha256 = (Get-FileHash -LiteralPath $pendingZip -Algorithm SHA256).Hash.ToLowerInvariant(); Files = $files }
    $manifestText = $manifest | ConvertTo-Json -Depth 4
    $zipStream = [IO.File]::OpenRead($pendingZip)
    try { Assert-Bundle $zipStream $manifestText } finally { $zipStream.Dispose() }
    Move-Item -LiteralPath $pendingZip -Destination $zipPath -Force
    [IO.File]::WriteAllText($manifestPath, $manifestText, (New-Object Text.UTF8Encoding($false)))
    $originalBytes = ($files | Measure-Object -Property Length -Sum).Sum
    Write-Host ('Backend payload: {0} files; {1:N1} MiB compressed from {2:N1} MiB.' -f $files.Count, ((Get-Item -LiteralPath $zipPath).Length / 1MB), ($originalBytes / 1MB))
}

if ($PassThru) {
    [pscustomobject]@{ BackendDirectory = $backendRoot; OutputDirectory = $outputRoot; Files = $files }
}
