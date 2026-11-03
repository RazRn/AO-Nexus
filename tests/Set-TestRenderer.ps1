param([ValidateSet('Stock','AOVK')][string]$Mode = 'AOVK', [string]$GameDirectory = 'D:\Games\AORebirth-AOVK-Test')
$ErrorActionPreference = 'Stop'
$game = [IO.Path]::GetFullPath($GameDirectory).TrimEnd('\')
if (!(Test-Path -LiteralPath (Join-Path $game 'NEXUS-AOVK-TEST.txt'))) { throw 'This is not a prepared Nexus test installation.' }
# Both modes keep Rebirth's login and crash repairs. The optional AO-VK
# AOReloaded version.dll must never be installed here.
$stockPatchHash = 'F392CB4EE8810D933C2A13D0DFEADE79981F39A42CAE65302D312726D0A58301'
$vkPatchHash = 'F156D1C2F7FCFFA6BFEF2DFCE7A1EF50664F7ED15E67625F0E156A40EDEFC857'
$rebirthPatch = Join-Path $game 'version.dll'
if (!(Test-Path -LiteralPath $rebirthPatch) -or
    (Get-FileHash -LiteralPath $rebirthPatch).Hash -notin @($stockPatchHash, $vkPatchHash)) {
    throw 'The required Rebirth version.dll is missing or unknown. Leaving the installation unchanged.'
}
foreach ($process in @(Get-Process -Name AnarchyOnline -ErrorAction SilentlyContinue)) {
    try { $path = $process.Path } catch { throw 'Cannot verify a running game. Close game clients before changing renderer.' }
    if (!$path -or [IO.Path]::GetDirectoryName($path).TrimEnd('\') -eq $game) { throw 'Close the test game clients before changing renderer.' }
}
$stockHash = '9D9E0DA25AC6F8C7ECDCE7A47A2409575945966A29E3B77850F7C62033A04BE3'
$vkHash = '60759C61C7383EFA39AE4F09981ED3E42D7A25CE7C9D093E6791A5C4A98040C6'
$current = Join-Path $game 'randy31.dll'
$currentHash = (Get-FileHash -LiteralPath $current).Hash
if ($currentHash -notin @($stockHash, $vkHash)) { throw 'The current renderer was changed outside this test; leaving it untouched.' }
$source = Join-Path $game $(if ($Mode -eq 'Stock') { 'randy31_orig.dll' } else { 'randy31-aovk-test.dll' })
$expected = if ($Mode -eq 'Stock') { $stockHash } else { $vkHash }
if ((Get-FileHash -LiteralPath $source).Hash -ne $expected) { throw 'Saved renderer checksum mismatch.' }
$patchSource = Join-Path $game $(if ($Mode -eq 'Stock') { 'version-stock-rebirth.dll' } else { 'version-aovk-test.dll' })
$expectedPatch = if ($Mode -eq 'Stock') { $stockPatchHash } else { $vkPatchHash }
if ((Get-FileHash -LiteralPath $patchSource).Hash -ne $expectedPatch) { throw 'Saved Rebirth patch checksum mismatch.' }
$previousRenderer = [IO.File]::ReadAllBytes($current)
$previousPatch = [IO.File]::ReadAllBytes($rebirthPatch)
try {
    Copy-Item -LiteralPath $patchSource -Destination $rebirthPatch -Force
    Copy-Item -LiteralPath $source -Destination $current -Force
    if ((Get-FileHash -LiteralPath $current).Hash -ne $expected -or
        (Get-FileHash -LiteralPath $rebirthPatch).Hash -ne $expectedPatch) { throw 'Renderer/patch copy verification failed.' }
} catch {
    [IO.File]::WriteAllBytes($rebirthPatch, $previousPatch)
    [IO.File]::WriteAllBytes($current, $previousRenderer)
    throw
}
Write-Host "Test installation now uses: $Mode with its matching Rebirth patch. Normal Rebirth installation is unchanged."
