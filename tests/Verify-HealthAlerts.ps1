param([string]$AssemblyPath = (Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4) { throw 'Run with x86 Windows PowerShell -STA' }
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions
[Windows.Forms.Application]::EnableVisualStyles()
$a = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$f = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$a.GetType('AOLauncher.Localization.Texts').GetMethod('SetLanguage',[Reflection.BindingFlags]'Public,NonPublic,Static').Invoke($null,@('en')) | Out-Null
$checks = 0
function Assert($ok,$message) { if (!$ok) { throw $message }; $script:checks++ }
function Model($name) { [Activator]::CreateInstance($a.GetType('AOLauncher.'+$name),$true) }
function Field($o,$name) { $o.GetType().GetField($name,$f).GetValue($o) }
function Set-Field($o,$name,$value) { $o.GetType().GetField($name,$f).SetValue($o,$value) }
function Prop($o,$name) { $o.GetType().GetProperty($name,$f).GetValue($o) }
function Call($o,$name,[object[]]$arguments=@()) {
 for ($i=0;$i -lt $arguments.Count;$i++) { if ($null -ne $arguments[$i]) { $arguments[$i]=$arguments[$i].psobject.BaseObject } }
 $o.GetType().GetMethod($name,$f).Invoke($o,$arguments)
}
function Show-Fixture($form) { $form.ShowInTaskbar=$false; $form.StartPosition='Manual'; $form.Location=[Drawing.Point]::new(-16000,-16000); $form.Show(); [Windows.Forms.Application]::DoEvents() }
$settings = Model 'LauncherSettings'
Assert (!$settings.BackgroundHealthChecks -and !$settings.HealthTrayNotifications) 'Background traffic and notifications are opt-in'
$tracker = Model 'HealthAlerts'
$snapshot = Model 'HealthSnapshot'
$server = $settings.LoginServers[0]
Set-Field $snapshot 'Server' $server
Set-Field $snapshot 'Scope' 'example:7500/config-a'
$sample = Model 'HealthResult'; $sample.Key='login'
$results = [Array]::CreateInstance($sample.GetType(),1); $results.SetValue($sample,0)
Set-Field $snapshot 'Results' $results
$now = [DateTime]::UtcNow
function Observe($state,$offset) {
 $sample.State = [Enum]::Parse($a.GetType('AOLauncher.HealthState'),$state)
 $sample.CheckedUtc=$now.AddSeconds($offset)
 @(Call $tracker 'Apply' @($snapshot,$sample.CheckedUtc))
}
Assert ((Observe 'Reachable' 0).Count -eq 0) 'Initial healthy baseline is silent'
Assert ((Observe 'Unreachable' 60).Count -eq 0) 'One failed check does not alert'
Assert ((Observe 'Unreachable' 60).Count -eq 0 -and (Prop $tracker 'IssueCount') -eq 0) 'Replaying a sample cannot confirm failure'
$changes=Observe 'Unreachable' 120
Assert ($changes.Count -eq 1 -and !(Field $changes[0] 'Recovered') -and (Prop $tracker 'IssueCount') -eq 1) 'Two distinct failures raise one issue'
Assert ((Observe 'Unreachable' 180).Count -eq 0) 'Continuing outage does not spam alerts'
Assert ((Observe 'Unknown' 240).Count -eq 0 -and (Prop $tracker 'IssueCount') -eq 1) 'Unknown observation keeps issue unresolved'
$entry=@(Prop $tracker 'Observations')[0]
Assert ((Call $entry 'Status' @($now.AddSeconds(241))).Contains('unresolved')) 'Tray explains unresolved unknown status'
$changes=Observe 'Reachable' 300
Assert ($changes.Count -eq 1 -and (Field $changes[0] 'Recovered') -and (Prop $tracker 'IssueCount') -eq 0) 'Confirmed recovery clears issue and notifies once'
Assert ((Observe 'Reachable' 360).Count -eq 0) 'Repeated health remains silent'
Observe 'Unreachable' 420 | Out-Null
Observe 'Unknown' 480 | Out-Null
Assert ((Observe 'Unreachable' 540).Count -eq 0) 'Unknown breaks consecutive-failure confirmation'
Assert ((Observe 'Unreachable' 900).Count -eq 0) 'Long sleep breaks consecutive-failure confirmation'
$sample.CheckedUtc=$now.AddSeconds(1000);$sample.State=[Enum]::Parse($sample.State.GetType(),'Reachable')
Assert (@(Call $tracker 'Apply' @($snapshot,$now.AddSeconds(1200))).Count -eq 0) 'Stale sample is ignored'
Assert (@(Call $tracker 'Apply' @($snapshot,$now)).Count -eq 0) 'Future sample is ignored'
Set-Field $snapshot 'Scope' 'example:7500/config-b'; $sample.Key='report'
Assert ((Observe 'ReportedIssue' 1020).Count -eq 0) 'Changed endpoint starts a separate baseline'
Assert ((Observe 'ReportedIssue' 1080).Count -eq 1) 'Official report issues are tracked'
Call $tracker 'Retain' (,([string[]]@('example:7500/config-a')))
Assert ((Prop $tracker 'IssueCount') -eq 0) 'Removing a monitored endpoint clears its retained issue without false recovery'

foreach ($language in @('en','da')) {
 $texts=$a.GetType('AOLauncher.Localization.Texts');$texts.GetMethod('SetLanguage',[Reflection.BindingFlags]'Public,NonPublic,Static').Invoke($null,@($language))|Out-Null
 $dialog=[Activator]::CreateInstance($a.GetType('AOLauncher.SettingsDialog'),[object[]]@($settings))
 try {
  Show-Fixture $dialog
  $background=$dialog.Controls.Find('backgroundHealthChecks',$true)[0];$notify=$dialog.Controls.Find('healthTrayNotifications',$true)[0]
  $notify.Checked=$true
  Assert ($background.Checked -and (Prop $dialog 'BackgroundHealthChecks') -and (Prop $dialog 'HealthTrayNotifications')) 'Notifications enable background checks in draft'
  Assert (!$settings.BackgroundHealthChecks -and !$settings.HealthTrayNotifications) 'Settings do not change until Save'
  $background.Checked=$false
  Assert (!$notify.Checked) 'Stopping background work disables notifications'
  $background.Checked=$true
  Assert (!$notify.Checked) 'Background work can run without notifications'
 } finally { $dialog.Dispose() }
}
$settings.BackgroundHealthChecks=$true;$settings.HealthTrayNotifications=$true
$json=New-Object Web.Script.Serialization.JavaScriptSerializer
$copy=$json.Deserialize($json.Serialize($settings),$settings.GetType())
Assert ($copy.BackgroundHealthChecks -and $copy.HealthTrayNotifications) 'Both settings survive JSON reload'

Add-Type @'
using System;
using System.Threading;
using System.Threading.Tasks;
public static class BackgroundHealthFixture {
 public static int Calls;
 public static object Results, Pending;
 public static bool Hang;
 public static CancellationToken Token;
 public static Task<T[]> Probe<T>(object server,CancellationToken token) {
  Calls++;Token=token;
  if(Hang) { var pending=new TaskCompletionSource<T[]>();Pending=pending;return pending.Task; }
  return Task.FromResult((T[])Results);
 }
}
'@
$panel=[Activator]::CreateInstance($a.GetType('AOLauncher.HealthPanel'),$f,$null,[object[]]@($false),$null)
$hostForm=[Windows.Forms.Form]::new()
try {
 $panel.Visible=$false;$hostForm.Controls.Add($panel)
 $check=$panel.GetType().GetField('Check',$f)
 $method=[BackgroundHealthFixture].GetMethod('Probe').MakeGenericMethod($sample.GetType())
 $check.SetValue($panel,[Delegate]::CreateDelegate($check.FieldType,$method))
 $sample.Key='login';$sample.State=[Enum]::Parse($sample.State.GetType(),'Reachable');$sample.CheckedUtc=[DateTime]::UtcNow
 [BackgroundHealthFixture]::Results=$results
 Call $panel 'Configure' @($settings.LoginServers,[string[]]@('rebirth','prk'))
 Show-Fixture $hostForm
 Call $panel 'SetBackgroundChecks' @($true)
 $task=Call $panel 'RefreshAsync' @($false);$task.GetAwaiter().GetResult()|Out-Null
 Assert ([BackgroundHealthFixture]::Calls -eq 2) 'Hidden background mode checks all selected servers'
 Assert ((Field $panel 'timer').Enabled) 'Hidden background timer continues running'
 $task=Call $panel 'RefreshAsync' @($true);$task.GetAwaiter().GetResult()|Out-Null
 Assert ([BackgroundHealthFixture]::Calls -eq 2) 'Background batches retain throttle'
 $hostForm.Hide();[Windows.Forms.Application]::DoEvents()
 Assert ((Field $panel 'timer').Enabled) 'Hiding entire host keeps background timer running'
 $panel.Visible=$true;Show-Fixture $hostForm
 $picker=Field $panel 'picker';$picker.DroppedDown=$true;[Windows.Forms.Application]::DoEvents()
 $card=(Field $panel 'cards').Controls[0]
 Set-Field $panel 'lastAttempt' ([DateTime]::MinValue)
 $task=Call $panel 'RefreshAsync' @($true);$task.GetAwaiter().GetResult()|Out-Null
 Assert ([BackgroundHealthFixture]::Calls -eq 4 -and $picker.DroppedDown -and !$card.IsDisposed) 'Open dropdown does not stop probes or get replaced'
 $picker.DroppedDown=$false
 Set-Field $panel 'lastAttempt' ([DateTime]::MinValue);[BackgroundHealthFixture]::Hang=$true
 $task=Call $panel 'RefreshAsync' @($true)
 $again=Call $panel 'RefreshAsync' @($true)
 Assert ([BackgroundHealthFixture]::Calls -eq 5) 'Pending batches cannot overlap'
 $panel.Visible=$false;[Windows.Forms.Application]::DoEvents()
 Assert (![BackgroundHealthFixture]::Token.IsCancellationRequested) 'Hiding does not cancel background probe'
 Call $panel 'SetBackgroundChecks' @($false)
 Assert ([BackgroundHealthFixture]::Token.IsCancellationRequested -and !(Field $panel 'timer').Enabled) 'Opting out cancels active work and stops hidden polling'
 [BackgroundHealthFixture]::Pending.SetCanceled();[Windows.Forms.Application]::DoEvents()
 Assert (!(Field $panel 'busy')) 'Cancellation releases busy guard'
} finally { $hostForm.Dispose() }
$main=[Activator]::CreateInstance($a.GetType('AOLauncher.MainForm'),[object[]]@($true))
try {
 Show-Fixture $main;$profile=Field $main 'settings';$profile.HealthTrayNotifications=$true;$profile.BackgroundHealthChecks=$true
 Call $main 'ConfigureSidebar';Call $main 'RefreshHealthTray'
 $icon=Field $main 'trayIcon'
 Assert ($icon.Visible) 'Tray status stays available with window open when notifications enabled'
 Call $main 'MinimizeToTray';Call $main 'RestoreFromTray'
 Assert ($icon.Visible -and $main.Visible) 'Restoring window retains opted-in tray status'
 $profile.HealthTrayNotifications=$false;Call $main 'ConfigureSidebar'
 Assert (!$icon.Visible) 'Opting out restores normal visible-window tray behaviour'
 Call $main 'PopulateHealthMenu'
 Assert ((Field $main 'trayHealthItem').DropDownItems.Count -gt 0) 'Tray menu offers status details without popups'
 $main.Dispose()
 Assert (!$icon.Visible) 'Disposal removes health tray icon'
} finally { $main.Dispose() }
Write-Output "PASS: $checks health transition, background scheduling, settings and tray checks. Synthetic probes only; no game connections or real notifications."
