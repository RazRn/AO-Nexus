param([string]$AssemblyPath = (Join-Path $PSScriptRoot '../src/Launcher/bin/Release/AONexus.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions
[Windows.Forms.Application]::EnableVisualStyles()
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$flags=[Reflection.BindingFlags]'Instance,Public,NonPublic'; $static=[Reflection.BindingFlags]'Static,Public,NonPublic'
$checks=0
function Assert($ok,$message){if(!$ok){throw $message};$script:checks++}
function Model($name){[Activator]::CreateInstance($assembly.GetType('AOLauncher.'+$name),$true)}
function Field($obj,$name){$type=$obj.GetType();while($type){$field=$type.GetField($name,$flags);if($field){return $field.GetValue($obj)};$type=$type.BaseType};throw "No field $name"}
function Click($wizard,$name){$wizard.Controls.Find($name,$true)[0].PerformClick();[Windows.Forms.Application]::DoEvents()}
function Show($wizard){$wizard.ShowInTaskbar=$false;$wizard.StartPosition='Manual';$wizard.Location=[Drawing.Point]::new(-18000,-18000);$wizard.Show();[Windows.Forms.Application]::DoEvents()}
function Create($settings,$first){[Activator]::CreateInstance($assembly.GetType('AOLauncher.SetupWizard'),$flags,$null,[object[]]@($settings,$false,$first),$null)}
$store=$assembly.GetType('AOLauncher.SettingsStore');$prefs=$assembly.GetType('AOLauncher.SetupPreferences')
$json=[Web.Script.Serialization.JavaScriptSerializer]::new()
$fresh=Model LauncherSettings
Assert (!$fresh.HasCompletedSetup) 'Fresh install should enter setup'
$legacy=$store.GetMethod('Deserialize',$static).Invoke($null,@('{"Version":2,"Accounts":[],"ShowServerNews":false,"BackgroundHealthChecks":true,"HasChosenUpdatePreference":true}'))
Assert ($legacy.HasCompletedSetup -and !$legacy.ShowServerNews -and $legacy.BackgroundHealthChecks) 'Old profiles must retain choices without being forced through setup'
$pending=$store.GetMethod('Deserialize',$static).Invoke($null,@($json.Serialize($fresh)))
Assert (!$pending.HasCompletedSetup) 'Explicit incomplete setup must survive restart'
$mapped=$prefs.GetMethod('NewsForServers',$static).Invoke($null,(,[string[]]@('live','live-2019','rebirth','prk','rebirth-local','custom')))
Assert (($mapped -join ',') -eq 'live,rebirth,prk') 'Map retail dimensions to one news source; no invented custom feed'
$output=Join-Path $PSScriptRoot '../logs/setup-previews';[IO.Directory]::CreateDirectory($output)|Out-Null
foreach($language in @('en','da')){
 $assembly.GetType('AOLauncher.Localization.Texts').GetMethod('SetLanguage',$static).Invoke($null,@($language))|Out-Null
 foreach($scale in @(1,1.5)){
  $settings=Model LauncherSettings;$settings.Language=$language;$before=$json.Serialize($settings)
  $wizard=Create $settings $true
  try{
   if($scale -ne 1){$wizard.Font=[Drawing.Font]::new($wizard.Font.FontFamily,$wizard.Font.Size*$scale);$wizard.Scale([Drawing.SizeF]::new($scale,$scale))}
   Show $wizard
   for($step=0;$step -lt 4;$step++){
    $fields=Field $wizard Fields
    $preferred=$fields.GetPreferredSize([Drawing.Size]::new($fields.ClientSize.Width,0)).Height
    $work=[Windows.Forms.Screen]::FromControl($wizard).WorkingArea
    Assert ($preferred -le $fields.ClientSize.Height -or $wizard.Height -ge $work.Height-25) "Step $step $language $scale has avoidable scrolling ($preferred/$($fields.ClientSize.Height))"
    foreach($control in @($wizard.Controls.Find('setupNext',$true))+@($wizard.Controls.Find('setupSkip',$true))){
     $rect=$wizard.RectangleToClient($control.RectangleToScreen($control.ClientRectangle))
     Assert ($rect.Bottom -le $wizard.ClientSize.Height) "Footer inaccessible at step $step $language $scale"
    }
    if($scale -eq 1){$bmp=[Drawing.Bitmap]::new($wizard.Width,$wizard.Height);try{$wizard.DrawToBitmap($bmp,[Drawing.Rectangle]::new(0,0,$bmp.Width,$bmp.Height));$bmp.Save((Join-Path $output "$language-$step.png"))}finally{$bmp.Dispose()}}
    if($step -lt 3){Click $wizard setupNext}
   }
   Assert ($json.Serialize($settings) -eq $before) 'Navigation must not mutate the saved profile'
   Click $wizard setupBack;Click $wizard setupBack
   $wizard.Controls.Find('setupServer-rebirth',$true)[0].Checked=$true
   Click $wizard setupNext
   $wizard.Controls.Find('setupNotifications',$true)[0].Checked=$true
   Assert $wizard.Controls.Find('setupBackground',$true)[0].Checked 'Notifications must enable background checks'
   $wizard.Controls.Find('setupBackground',$true)[0].Checked=$false
   Assert (!$wizard.Controls.Find('setupNotifications',$true)[0].Checked) 'Disabling background checks must disable notifications'
   $wizard.Controls.Find('setupUpdates',$true)[0].Checked=$true
   Click $wizard setupNext;Click $wizard setupNext
   $result=$wizard.GetType().GetProperty('Result',$flags).GetValue($wizard)
   Assert ($result.HasCompletedSetup -and $result.HasChosenUpdatePreference -and $result.CheckForUpdatesOnStartup) 'Finish saves onboarding and update choice'
   Assert (($result.HealthServerIds -join ',') -eq 'rebirth' -and ($result.NewsSourceIds -join ',') -eq 'rebirth') 'Only selected server used for health and news'
   Assert ($json.Serialize($settings) -eq $before) 'Finishing returns a new profile rather than mutating input'
  }finally{$wizard.Dispose()}
 }
}
$wizard=Create $fresh $true
try{Show $wizard;Click $wizard setupSkip;$result=$wizard.GetType().GetProperty('Result',$flags).GetValue($wizard)
 Assert ($result.HasCompletedSetup -and $result.HasChosenUpdatePreference) 'Skip is remembered'
 Assert (!$result.CheckForUpdatesOnStartup -and !$result.ShowServerNews -and !$result.ShowServerHealth -and !$result.BackgroundHealthChecks -and !$result.HealthTrayNotifications) 'Skip must leave optional connections off'
}finally{$wizard.Dispose()}
$existing=Model LauncherSettings;$existing.HasCompletedSetup=$true
$account=Model AccountConfig;$account.Label='Existing';$account.Username='example';$account.GamePath='C:\Games\Example';$account.Server='127.0.0.1';$account.Port=7500;$existing.Accounts.Add($account)
$before=$json.Serialize($existing);$wizard=Create $existing $false
try{Show $wizard;(Field $wizard draft).Accounts.Clear();Click $wizard setupSkip;Assert ($json.Serialize($existing) -eq $before) 'Cancelling rerun preserves original accounts and settings'}finally{$wizard.Dispose()}
$wizard=Create $existing $false
try{
 Show $wizard
 $language=$wizard.Controls.Find('setupLanguage',$true)[0];$language.SelectedIndex=1;[Windows.Forms.Application]::DoEvents()
 Assert ($wizard.Text.StartsWith('AO Nexus-') -and (Field $wizard draft).Language -eq 'da') 'Language selection should translate the wizard immediately'
 Click $wizard setupSkip
}finally{$wizard.Dispose()}
$wizard=Create $fresh $true
try{
 Show $wizard
 $delegateType=[Func``3].MakeGenericType($assembly.GetType('AOLauncher.LauncherSettings'),[bool],[string])
 $reject=[scriptblock]{param($profile,$startup) return 'Synthetic save failure'} -as $delegateType
 $wizard.GetType().GetProperty('Commit',$flags).SetValue($wizard,$reject)
 Click $wizard setupNext;Click $wizard setupNext;Click $wizard setupNext;Click $wizard setupNext
 Assert ($wizard.Visible -and !$wizard.GetType().GetProperty('Result',$flags).GetValue($wizard)) 'Save failure must not complete setup'
 Assert ((Field $wizard ErrorLabel).Text -eq 'Synthetic save failure') 'Save failure is actionable in the wizard'
}finally{$wizard.Dispose()}
$tempRoot=Join-Path ([IO.Path]::GetTempPath()) ('AONexus-Setup-'+[Guid]::NewGuid().ToString('N'))
try{
 $skipped=$prefs.GetMethod('Skip',$static).Invoke($null,@($fresh))
 $store.GetMethod('SaveToDirectory',$static).Invoke($null,@($skipped,[string]$tempRoot))|Out-Null
 $loaded=$store.GetMethod('ReadFromDirectory',$static).Invoke($null,@([string]$tempRoot))
 Assert ($loaded.HasCompletedSetup -and $loaded.HasChosenUpdatePreference -and !$loaded.ShowServerNews) 'Skipped setup must stay skipped after disk reload'
}finally{
 $full=[IO.Path]::GetFullPath($tempRoot);$base=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$full.StartsWith($base,[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($full).StartsWith('AONexus-Setup-')){throw 'Unexpected fixture cleanup path'}
 if(Test-Path -LiteralPath $full){Remove-Item -LiteralPath $full -Recurse -Force}
}
Write-Output "PASS: $checks setup migration, draft isolation, completion, skip, language, notification and sizing checks."
