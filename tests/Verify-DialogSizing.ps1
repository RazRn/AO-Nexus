param([string]$AssemblyPath = (Join-Path $PSScriptRoot '../src/Launcher/bin/Release/AONexus.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
foreach ($name in @('GuiStrings','NexusStrings')) { $assembly.GetType('AOLauncher.'+$name).GetMethod('Register',$static).Invoke($null,@()) | Out-Null }
function Model($name) { [Activator]::CreateInstance($assembly.GetType('AOLauncher.'+$name),$true) }
function Children($control) { foreach ($child in $control.Controls) { $child; Children $child } }
function Check($ok,$message) { if (!$ok) { throw $message }; $script:checks++ }
function Pump { [Windows.Forms.Application]::DoEvents() }
$settings = Model 'LauncherSettings'
$account = Model 'AccountConfig'; $account.Label='Example'; $account.Username='Example'; $account.RememberPassword=$false
$choices = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($assembly.GetType('AOLauncher.GuiCharacterChoice')))
$candidates = [Array]::CreateInstance($assembly.GetType('AOLauncher.PrefsCandidate'),0)
$checks=0
$preview=Join-Path $PSScriptRoot '../logs/dialog-sizing-previews'
[IO.Directory]::CreateDirectory($preview) | Out-Null
foreach ($language in @('en','da')) {
 $assembly.GetType('AOLauncher.Localization.Texts').GetMethod('SetLanguage',$static).Invoke($null,@($language)) | Out-Null
 foreach ($scale in @(1,1.5)) {
  foreach ($kind in @('PresetNameDialog','AccountDialog','CharacterDialog','PasswordDialog','SavedPasswordDialog','WindowActionDialog','GuiCaptureDialog','GuiTemplateNameDialog','SettingsDialog','PrivacyDialog','WhatsNewDialog','UpdateDialog','LoginServersDialog','PrefsPickerDialog','SelectedTargetsDialog','LaunchConfirmationDialog','HealthEndpointsDialog','ReportProblemDialog')) {
   $ctorArgs = switch ($kind) {
    'PresetNameDialog' { [object[]]@('Create team preset','Example',[string[]]@('Reserved'),$null,$null,6,$false) }
    'AccountDialog' { [object[]]@($null,$settings) }
    'CharacterDialog' { [object[]]@('Example',$null) }
    'PasswordDialog' { [object[]]@($account) }
    'SavedPasswordDialog' { [object[]]@($account) }
    'WindowActionDialog' { [object[]]@($true) }
    'GuiCaptureDialog' { [object[]]@($choices,$false) }
    'GuiTemplateNameDialog' { [object[]]@('Example') }
    'SettingsDialog' { [object[]]@($settings) }
    'LoginServersDialog' { ,$settings.LoginServers }
    'HealthEndpointsDialog' { [object[]]@($settings.LoginServers,$settings.HealthCheckEndpoints) }
    'PrefsPickerDialog' { [object[]]@($candidates,'Example') }
    'SelectedTargetsDialog' { [object[]]@('Example account / example server') }
    'LaunchConfirmationDialog' { [object[]]@('6 characters across 2 accounts on 2 servers.') }
    'ReportProblemDialog' { [object[]]@($settings,[Enum]::ToObject($assembly.GetType('AOLauncher.ProblemArea'),0),$false) }
    'UpdateDialog' {
     $release=Model 'UpdateRelease'; $release.GetType().GetField('Version',$flags).SetValue($release,[version]'9.0.0'); $notes="# Example release`n`n- First change.`n- Second change."
     $release.GetType().GetField('Notes',$flags).SetValue($release,$notes)
     [object[]]@([Func[bool]]{return $false},$release)
    }
    default { [object[]]@() }
   }
   if ($kind -eq 'LoginServersDialog') { $ctorArgs=New-Object object[] 1; $ctorArgs[0]=$settings.LoginServers }
   $dialog=[Activator]::CreateInstance($assembly.GetType('AOLauncher.'+$kind),$flags,$null,[object[]]$ctorArgs,$null)
   try {
    if ($scale -ne 1) { $dialog.Font=[Drawing.Font]::new($dialog.Font.FontFamily,$dialog.Font.Size*$scale); $dialog.Scale([Drawing.SizeF]::new($scale,$scale)) }
    $dialog.ShowInTaskbar=$false; $dialog.StartPosition='Manual'; $dialog.Location=[Drawing.Point]::new(-18000,-18000); $dialog.Show(); Pump
    $tabs=@(Children $dialog | Where-Object {$_ -is [Windows.Forms.TabControl]})
    $pages=if ($tabs.Count) { $tabs[0].TabCount } else { 1 }
    for ($page=0; $page -lt $pages; $page++) {
     if ($tabs.Count) { $tabs[0].SelectedIndex=$page; Pump }
     $context="$kind $language $scale page $page"
     if ($kind -eq 'SettingsDialog' -and $page -eq 1) {
      $loginAction=$dialog.Controls.Find('manageLoginServers',$true)[0]
      $healthAction=$dialog.Controls.Find('manageHealthChecks',$true)[0]
      Check ($healthAction.Top - $loginAction.Bottom -ge 8*$scale-1) "$context stacked actions need a visible gap"
      Check ($healthAction.Left -eq $loginAction.Left -and $healthAction.Width -eq $loginAction.Width) "$context related actions should align and have equal widths"
     }
     $work=[Windows.Forms.Screen]::FromControl($dialog).WorkingArea
     Check ($dialog.Height -le $work.Height) "$context exceeds screen height"
     foreach ($table in @(Children $dialog | Where-Object { $_ -is [Windows.Forms.TableLayoutPanel] -and $_.Visible -and $_.AutoScroll })) {
      if ($table.ColumnCount -ne 1 -or @($table.RowStyles | Where-Object {$_.SizeType -eq 'Percent'}).Count) { continue }
      $preferred=$table.GetPreferredSize([Drawing.Size]::new($table.ClientSize.Width,0)).Height
      if ($kind -ne 'PrivacyDialog') {
       Check ($preferred -le $table.ClientSize.Height -or $dialog.Height -ge $work.Height-25) "$context is cramped despite room on screen ($preferred / $($table.ClientSize.Height))"
      }
      if ($preferred -le $table.ClientSize.Height) { Check (!$table.VerticalScroll.Visible) "$context has a needless scrollbar" }
      if ($kind -eq 'PrivacyDialog') { Check $table.VerticalScroll.Visible "$context must retain scrolling for the long document" }
     }
     foreach ($button in @(Children $dialog | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Visible -and ($_.DialogResult -eq 'Cancel' -or $_.Text -in @('Save','Gem')) })) {
      $rect=$dialog.RectangleToClient($button.RectangleToScreen($button.ClientRectangle))
      Check ($rect.Bottom -le $dialog.ClientSize.Height) "$context footer button is offscreen"
     }
     if ($scale -eq 1 -or $kind -eq 'PresetNameDialog') {
      $image=[Drawing.Bitmap]::new($dialog.Width,$dialog.Height)
      try { $dialog.DrawToBitmap($image,[Drawing.Rectangle]::new(0,0,$image.Width,$image.Height)); $image.Save((Join-Path $preview "$kind-$language-$scale-$page.png")) } finally { $image.Dispose() }
     }
    }
    Write-Host "PASS: $kind $language $scale ($pages pages)"
   } finally { $dialog.Dispose() }
  }
 }
}
Write-Host "PASS: $checks dialog-sizing checks. Synthetic data only; no game, profile, password or update writes."
