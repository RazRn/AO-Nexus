$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../build/DefenderRelease.Policy.ps1')
$script:checks = 0
function Assert($Condition, [string]$Message) { if (!$Condition) { throw $Message }; $script:checks++ }
function Reject([scriptblock]$Action, [string]$Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert $rejected $Message
}
$artifact = 'C:\Fixture\AONexus.exe'
Assert (Test-DefenderResourcePath ('containerfile:_' + $artifact) $artifact) 'Container detection matches'
Assert (Test-DefenderResourcePath ('file:_' + $artifact + '->[MSILRES:AOLauncher.NexusPluginHost.exe]') $artifact) 'Embedded helper detection matches'
Assert (Test-DefenderResourcePath 'file:_c:\fixture\aonexus.exe' $artifact) 'Path comparison is case insensitive'
Assert (!(Test-DefenderResourcePath 'file:_C:\Fixture\AONexus.exe.old' $artifact)) 'Adjacent filename is not this artifact'
Assert (!(Test-DefenderResourcePath 'file:_C:\Other\AONexus.exe' $artifact)) 'Unrelated artifact is not this release'
$now = [datetime]::UtcNow
$status = [pscustomobject]@{ AMServiceEnabled=$true; AntivirusEnabled=$true; RealTimeProtectionEnabled=$true; BehaviorMonitorEnabled=$true; AntivirusSignatureLastUpdated=$now.AddHours(-1) }
$pref = [pscustomobject]@{ DisableArchiveScanning=$false }
Assert-DefenderReady $status $pref $now
$script:checks++
foreach ($name in @('AMServiceEnabled','AntivirusEnabled','RealTimeProtectionEnabled','BehaviorMonitorEnabled')) {
    $status.$name=$false
    Reject { Assert-DefenderReady $status $pref $now } "Disabled $name must block release"
    $status.$name=$true
}
$status.AntivirusSignatureLastUpdated=$now.AddHours(-25)
Reject { Assert-DefenderReady $status $pref $now } 'Stale signatures block release'
$status.AntivirusSignatureLastUpdated=$now.AddHours(1)
Reject { Assert-DefenderReady $status $pref $now } 'Future signature timestamps block release'
$status.AntivirusSignatureLastUpdated=$now.AddHours(-1);$pref.DisableArchiveScanning=$true
Reject { Assert-DefenderReady $status $pref $now } 'Disabled archive scanning blocks release'
Assert-DefenderScanResult 0 'abc' 'abc' @()
$script:checks++
Reject { Assert-DefenderScanResult 2 'abc' 'abc' @() } 'Scanner failure blocks release'
Reject { Assert-DefenderScanResult 0 'abc' '' @() } 'Removed artifact blocks even when scanner returns success'
Reject { Assert-DefenderScanResult 0 'abc' 'def' @() } 'Changed artifact blocks even when scanner returns success'
Reject { Assert-DefenderScanResult 0 'abc' 'abc' @([pscustomobject]@{ThreatID=1}) } 'Remediated detection blocks even when scanner returns success'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('NexusDefenderReview-' + [guid]::NewGuid().ToString('N') + '.json')
try {
    $hash = 'a' * 64
    $review = [ordered]@{ Result='Passed'; ExecutableSha256=$hash; Reviewer='Synthetic fixture'; TestedUtc=$now.ToString('o'); DefenderSignatureVersion='fixture';
        MicrosoftSubmissionId='fixture'; MicrosoftVerdict='FalsePositiveResolved'; Scenarios=[ordered]@{
            LaunchAndLogin='Passed'; AOSharpAndMonitoring='Passed'; NexusRestartWithClients='Passed'; OptionalWindowsStartup='Passed' } }
    $review | ConvertTo-Json | Set-Content -LiteralPath $temp
    Assert-DefenderRuntimeReview $temp $hash
    $script:checks++
    Reject { Assert-DefenderRuntimeReview '' $hash } 'Missing runtime review blocks release'
    Reject { Assert-DefenderRuntimeReview $temp ('b' * 64) } 'Runtime review for other bytes blocks release'
    $review.MicrosoftVerdict='Pending';$review | ConvertTo-Json | Set-Content -LiteralPath $temp
    Reject { Assert-DefenderRuntimeReview $temp $hash } 'Unresolved known incident blocks release'
    $review.MicrosoftVerdict='FalsePositiveResolved';$review.Scenarios.AOSharpAndMonitoring='NotTested'
    $review | ConvertTo-Json | Set-Content -LiteralPath $temp
    Reject { Assert-DefenderRuntimeReview $temp $hash } 'Untested AOSharp runtime blocks release'
    $review.Scenarios.AOSharpAndMonitoring='Passed';$review.TestedUtc=$now.AddDays(-8).ToString('o')
    $review | ConvertTo-Json | Set-Content -LiteralPath $temp
    Reject { Assert-DefenderRuntimeReview $temp $hash } 'Old runtime review blocks release'
    # The evidence writer must never overwrite its input, even on a failed scan.
    $before = Get-Content -LiteralPath $temp -Raw
    Reject { & (Join-Path $PSScriptRoot '../build/Verify-DefenderRelease.ps1') -ArtifactPath $temp -ReportPath $temp } 'Report/input collision is rejected'
    Assert ((Get-Content -LiteralPath $temp -Raw) -eq $before) 'Collision rejection preserved input bytes'
} finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
Write-Host "PASS: $script:checks synthetic Defender release-policy checks. No malware sample, game launch or protection changes."
