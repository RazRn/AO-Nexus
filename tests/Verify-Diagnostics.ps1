param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Web.Extensions
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance';$checks=0
function Assert($ok,$message){if(!$ok){throw $message};$script:checks++}
function Type($name){$a.GetType('AOLauncher.'+$name,$true)}
function New($name){[Activator]::CreateInstance((Type $name),$true)}
function Call($type,$method,[object[]]$arguments){for($i=0;$i -lt $arguments.Count;$i++){if($null -ne $arguments[$i]){$arguments[$i]=$arguments[$i].psobject.BaseObject}};(Type $type).GetMethod($method,$flags).Invoke($null,$arguments)}
function Redact($text){$current=(Type 'DiagnosticRedactor').GetProperty('Current',$flags).GetValue($null);$current.GetType().GetMethod('Redact',$flags).Invoke($current,[object[]]@($text))}
$root=Join-Path ([IO.Path]::GetTempPath()) ('Nexus-diagnostics-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
try {
    Call DiagnosticJournal Configure @($true,$root)
    $settings=New LauncherSettings
    Assert ($settings.KeepDiagnostics) 'Diagnostics default to on'
    $json=New-Object Web.Script.Serialization.JavaScriptSerializer
    Assert ($json.Deserialize('{}',$settings.GetType()).KeepDiagnostics) 'Older profiles keep diagnostics on'
    $profile=New LoginServerProfile;$profile.Id='custom1';$profile.Name='Private Test Server';$profile.Server='10.20.30.40';$profile.Port=7500;$profile.MaxAccounts=0;$profile.MaxCharactersPerAccount=0;$profile.CharacterSlots=0
    $settings.LoginServers.Add($profile)
    $account=New AccountConfig;$account.Label='Main crew';$account.Username='fixturelogin';$account.GamePath='C:\Users\fixture-user\Games\AO Rebirth';$account.PrefsPath='C:\Users\fixture-user\Games\AO Rebirth\Prefs\fixturelogin';$account.ServerProfileId='rebirth';$account.Server='2.24.96.30';$account.Port=7500
    $first=New CharacterConfig;$first.Name='Fixturespawn';$first.CharacterId=123456789;$account.Characters.Add($first)
    $second=New CharacterConfig;$second.Name='Aster · Doctor';$second.CharacterId=987654321;$account.Characters.Add($second)
    $other=New AccountConfig;$other.Label='alt crew';$other.Username='AltLogin';$other.GamePath='D:\AO';$other.ServerProfileId='custom1';$other.Server='10.20.30.40';$other.Port=7500
    $settings.Accounts.Add($account);$settings.Accounts.Add($other)
    $preset=New LaunchPreset;$preset.Name='Raid team Fixturespawn';$settings.Presets.Add($preset)
    Call DiagnosticRedactor Update @($settings)
    $raw='Starting Fixturespawn (ID 123456789) from C:\Users\fixture-user\Games\AO Rebirth\AnarchyOnline.exe for fixturelogin on 10.20.30.40:7500. ID 987654321: detected as Aster. Prefs ...\Char987654321\prefs.xml. Mail someone@example.com. Team Raid team Fixturespawn. Private Test Server. ALTLOGIN. Public 2.24.96.30:7500. Exit code 2. maximum.'
    $redacted=Redact $raw
    foreach($secret in @('Fixturespawn','123456789','987654321','Aster','fixturelogin','fixture-user','10.20.30.40','someone@example.com','Raid team','Private Test','AltLogin')){Assert ($redacted.IndexOf($secret,[StringComparison]::OrdinalIgnoreCase) -lt 0) "Redaction hides $secret"}
    Assert ($redacted.Contains('2.24.96.30:7500') -and $redacted.Contains('Exit code 2') -and $redacted.Contains('maximum')) 'Public endpoints, small numbers and ordinary words are kept'
    $token=Call DiagnosticRedactor Token @('Char','123456789')
    Assert ($redacted.Contains("Starting $token (ID $token)")) 'Name and ID of one character share a stable pseudonym'
    $account.Characters.RemoveAt(0);Call DiagnosticRedactor Update @($settings)
    Assert ((Redact 'Fixturespawn') -eq $token) 'Removed characters stay hidden during the session'

    Call DiagnosticJournal Record @('login','Starting Fixturespawn for fixturelogin')
    Call DiagnosticJournal Exception @('gui','Applying layout',[IO.IOException]::new('Access denied: C:\Users\fixture-user\Games\AO Rebirth\Prefs\fixturelogin\Char987654321'))
    $journalPath=Join-Path $root 'diagnostics.log'
    $journal=[IO.File]::ReadAllText($journalPath)
    Assert ($journal.Contains('[login]') -and $journal.Contains('System.IO.IOException') -and $journal.IndexOf('fixture',[StringComparison]::OrdinalIgnoreCase) -lt 0 -and !$journal.Contains('987654321')) 'Journal entries are anonymised before reaching disk'

    $crashReporter=Type 'CrashReporter'
    try { throw [InvalidOperationException]::new('Boom for Fixturespawn',[UnauthorizedAccessException]::new('inner fixturelogin')) } catch { $thrown=$_.Exception }
    $saved=Call CrashReporter Save @($thrown,'interface')
    $reports=Call CrashReporter Reports @()
    Assert ($saved -and $reports.Count -eq 1 -and $crashReporter.GetProperty('HasPending',$flags).GetValue($null)) 'Crash report is saved and offered at next start'
    $crash=[IO.File]::ReadAllText($reports[0])
    Assert ($crash.Contains('System.UnauthorizedAccessException') -and $crash.IndexOf('fixture',[StringComparison]::OrdinalIgnoreCase) -lt 0) 'Crash report includes inner exceptions and is anonymised'
    Call CrashReporter AcknowledgePending @()
    Assert (!$crashReporter.GetProperty('HasPending',$flags).GetValue($null)) 'Acknowledging clears the pending prompt'
    for($i=0;$i -lt 25;$i++){Call CrashReporter Save @([Exception]::new("x$i"),'background')|Out-Null;Start-Sleep -Milliseconds 3}
    Assert ((Call CrashReporter Reports @()).Count -eq 20) 'Only the newest 20 crash reports are kept'

    $area=[Enum]::ToObject((Type 'ProblemArea'),0)
    $snapshot=Call DiagnosticSnapshot Build @($settings,$false)
    Assert ($snapshot.Contains('AO Nexus') -and $snapshot.Contains('Configuration (anonymised)') -and $snapshot.Contains('.NET Framework')) 'Environment snapshot covers version, runtime and setup'
    $report=Call DiagnosticReport Compose @($area,'Third char never logs in on fixturelogin','Aster stays at login',$snapshot,$true,$true)
    Assert ($report.Contains('### Diagnostics log') -and $report.Contains('### Environment') -and $report.IndexOf('fixture',[StringComparison]::OrdinalIgnoreCase) -lt 0 -and !$report.Contains('Aster')) 'Report has all sections and no account data'
    $arguments=[object[]]@($area,'Third char never logs in on fixturelogin',$report,$false)
    $url=(Type 'DiagnosticReport').GetMethod('BuildIssueUrl',$flags).Invoke($null,$arguments)
    Assert ($url.Length -le 7500 -and $url.StartsWith('https://github.com/RazRn/AO-Nexus/issues/new?template=bug_report.yml&title=') -and $url.IndexOf('fixture',[StringComparison]::OrdinalIgnoreCase) -lt 0) 'Issue link is bounded, targets the template and is anonymised'
    if ($arguments[3]) { Assert ($url.Contains([Uri]::EscapeDataString('full report is on your clipboard'))) 'A shortened link tells the user to paste the full report' }

    $main=[Activator]::CreateInstance((Type 'MainForm'),[object[]]@($true))
    try {
        $request=New LaunchRequest;$launch=New LaunchCharacter;$launch.AccountId=$other.Id;$launch.Account='AltLogin';$launch.Server='10.20.30.40';$launch.Port=7500;$launch.CharacterId=555666777;$request.Characters.Add($launch)
        $accounts=[Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType((Type 'AccountConfig')));$accounts.Add($other)
        $summary=Redact (Call MainForm DescribeLaunch @($request,$accounts,$false))
        Assert ($summary.Contains('Login run started: 1 character(s) on 1 login(s)') -and !$summary.Contains('AltLogin') -and !$summary.Contains('10.20.30.40')) 'Login summary is useful and anonymised'
        Assert ((Call MainForm DescribeExitCode @(-1073741819)).Contains('0xC0000005')) 'Unexpected backend exit codes are shown in hex'
    } finally { $main.Dispose() }

    foreach($language in @('en','da')){
        Call 'Localization.Texts' SetLanguage @($language)
        Call NexusStrings Register @()
        $dialog=[Activator]::CreateInstance((Type 'ReportProblemDialog'),$flags,$null,[object[]]@($settings,$area,$false),$null)
        try {
            foreach($name in @('problemArea','problemSummary','problemDescription','includeDiagnosticsLog','includeCrashReports','reportPreview','openGitHubIssue','copyReport','saveReport')){Assert ($dialog.Controls.Find($name,$true).Count -eq 1) "Report dialog has $name ($language)"}
            $expected=if($language -eq 'da'){'Rapportér et problem'}else{'Report a problem'}
            Assert ($dialog.Text -eq $expected) "Report dialog title is translated ($language)"
        } finally { $dialog.Dispose() }
        $settingsDialog=[Activator]::CreateInstance((Type 'SettingsDialog'),[object[]]@($settings))
        try { foreach($name in @('reportProblem','keepDiagnostics','clearDiagnostics')){Assert ($settingsDialog.Controls.Find($name,$true).Count -eq 1) "Settings has $name ($language)"} } finally { $settingsDialog.Dispose() }
    }
    $viewer=New LogViewer
    try { Assert ($viewer.Controls.Find('reportProblemFromLog',$true).Count -eq 1) 'Activity log offers Report a problem' } finally { $viewer.Dispose() }

    Call DiagnosticJournal Configure @($false,$root)
    Assert ((Call DiagnosticJournal ReadTail @(1000)) -eq '' -and (Call CrashReporter Reports @()).Count -eq 0) 'Turned-off diagnostics are not read into reports'
    Call DiagnosticJournal Record @('login','must not be written')
    Assert (![IO.File]::ReadAllText($journalPath).Contains('must not be written')) 'Turned-off diagnostics are not written'
    Call DiagnosticJournal Configure @($true,$root)
    Assert (Call DiagnosticJournal Clear @()) 'Delete diagnostics succeeds'
    Assert (!(Test-Path -LiteralPath $journalPath) -and !(Test-Path -LiteralPath (Join-Path $root 'anonymisation.key')) -and (Call CrashReporter Reports @()).Count -eq 0) 'Delete removes the journal, crash reports and pseudonym key'
    for($i=0;$i -lt 2600;$i++){Call DiagnosticJournal Record @('app',('x'*1000)+" entry $i")}
    Assert ((Get-Item -LiteralPath $journalPath).Length -le 2MB -and (Call DiagnosticJournal ReadTail @(4096)).Contains('entry 2599')) 'Journal is capped at 2 MB and keeps the newest entries'
    Write-Output "PASS: $checks diagnostics, anonymisation, crash-report and report-dialog checks. Isolated temporary files only; no network or GitHub access."
} finally {
    Call DiagnosticJournal Configure @($false,$null)
    $resolved=[IO.Path]::GetFullPath($root)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($resolved).StartsWith('Nexus-diagnostics-')) {throw 'Unexpected test cleanup root'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
