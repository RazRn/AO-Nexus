# Pure policy checks shared by the release gate and synthetic regression tests.
function Test-DefenderResourcePath([string]$Resource, [string]$Path) {
    $candidate = ($Resource -replace '^(containerfile|file):_', '') -split '->', 2
    return [string]::Equals($candidate[0], $Path, [StringComparison]::OrdinalIgnoreCase)
}
function Assert-DefenderReady($Status, $Preference, [datetime]$Now = [datetime]::UtcNow) {
    foreach ($name in @('AMServiceEnabled','AntivirusEnabled','RealTimeProtectionEnabled','BehaviorMonitorEnabled')) {
        if ($Status.$name -ne $true) { throw "Defender release check requires $name." }
    }
    if ($Preference.DisableArchiveScanning -ne $false) { throw 'Archive scanning must be enabled for release verification.' }
    if (!$Status.AntivirusSignatureLastUpdated) { throw 'Defender signature age is unavailable.' }
    $age = ($Now - ([datetime]$Status.AntivirusSignatureLastUpdated).ToUniversalTime()).TotalHours
    if ($age -lt 0 -or $age -gt 24) { throw 'Release policy requires Defender signatures from the last 24 hours.' }
}
function Assert-DefenderScanResult([int]$ExitCode, [string]$BeforeHash, [string]$AfterHash, [object[]]$Detections) {
    if ($ExitCode -ne 0) { throw "Defender scan did not pass (exit $ExitCode)." }
    if (!$BeforeHash -or !$AfterHash -or $BeforeHash -ne $AfterHash) { throw 'The scanned artifact was removed or changed.' }
    if (@($Detections).Count -gt 0) { throw 'Defender recorded a matching detection. Release remains blocked.' }
}
function Assert-DefenderRuntimeReview([string]$Path, [string]$ExecutableHash) {
    if (!$Path) { throw 'A completed -DefenderRuntimeReview is required; see docs/development/log/Defender-Review.txt.' }
    $review = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    if ($review.Result -ne 'Passed' -or $review.ExecutableSha256 -ne $ExecutableHash -or $ExecutableHash -notmatch '^[a-fA-F0-9]{64}$') {
        throw 'Runtime review must pass for the exact executable being packaged.'
    }
    if ([string]::IsNullOrWhiteSpace($review.Reviewer) -or !$review.TestedUtc -or !$review.DefenderSignatureVersion) { throw 'Runtime review is incomplete.' }
    $age = ([datetime]::UtcNow - ([datetime]$review.TestedUtc).ToUniversalTime()).TotalDays
    if ($age -lt 0 -or $age -gt 7) { throw 'Runtime review must be from the last seven days.' }
    foreach ($scenario in @('LaunchAndLogin','AOSharpAndMonitoring','NexusRestartWithClients','OptionalWindowsStartup')) {
        if ($review.Scenarios.$scenario -ne 'Passed') { throw "Missing runtime scenario: $scenario." }
    }
    # The current Behavior:Win32/DefenseEvasion.A!ml incident is unresolved.
    # A clean static scan alone cannot close a behavioral detection.
    if (!$review.MicrosoftSubmissionId -or $review.MicrosoftVerdict -ne 'FalsePositiveResolved') {
        throw 'The known Defender incident needs a recorded Microsoft resolution before release.'
    }
}

# Deferral is explicit and recorded by Package.ps1; it is not a passed review.
function Assert-ReleaseRuntimeReview([string]$Path, [string]$ExecutableHash, [switch]$Deferred, [string]$Reason) {
    if ($Deferred) {
        if ([string]::IsNullOrWhiteSpace($Reason)) { throw 'Runtime-review deferral requires a recorded reason.' }
        Write-Warning ('Runtime review and Microsoft resolution deferred: ' + $Reason)
        return
    }
    Assert-DefenderRuntimeReview $Path $ExecutableHash
}
