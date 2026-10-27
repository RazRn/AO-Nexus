param([string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'))
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Use 32-bit Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions
[Windows.Forms.Application]::EnableVisualStyles()
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$statics = [Reflection.BindingFlags]'Public,NonPublic,Static'
$checks = 0
function Assert($value, $message) { if (!$value) { throw $message }; $script:checks++ }
function Field($o, $name) { $o.GetType().GetField($name, $flags).GetValue($o) }
function Call($o, $name, [object[]]$arguments = @()) { $o.GetType().GetMethod($name, $flags).Invoke($o, $arguments) }
function Show-Fixture($form) {
    $form.ShowInTaskbar = $false; $form.StartPosition = 'Manual'; $form.Location = [Drawing.Point]::new(-16000,-16000)
    $form.Show(); [Windows.Forms.Application]::DoEvents()
}
$serializer = New-Object Web.Script.Serialization.JavaScriptSerializer
$oldProfile = $assembly.GetType('AOLauncher.SettingsStore').GetMethod('Deserialize', $statics).Invoke($null, @('{"Version":2,"SkipTrayConfirmation":true}'))
Assert (!$oldProfile.SkipLoginConfirmation -and !$oldProfile.SkipTeamLogoutConfirmation -and $oldProfile.SkipTrayConfirmation) 'Old profiles retain independent window preferences and default both game confirmations on'
$main = [Activator]::CreateInstance($assembly.GetType('AOLauncher.MainForm'), [object[]]@($true))
$script:promptCalls = 0; $script:accept = $false; $script:remember = $true; $script:lastLogout = $null; $script:lastSummary = $null
$callback = [Func[bool,string,Tuple[bool,bool]]]{ param($logout,$summary)
    $script:promptCalls++; $script:lastLogout = $logout; $script:lastSummary = $summary
    return [Tuple]::Create([bool]$script:accept,[bool]$script:remember)
}
try {
    Show-Fixture $main
    $main.GetType().GetField('GameActionPrompt', $flags).SetValue($main, $callback)
    $settings = Field $main 'settings'
    $before = $serializer.Serialize($settings)
    Assert (!(Call $main 'ConfirmSelectedLaunch')) 'Cancel must stop a login confirmation'
    Assert ($serializer.Serialize($settings) -eq $before) 'Cancel with remember checked must preserve the whole profile'
    Assert (!$script:lastLogout -and $script:lastSummary.Length -gt 0) 'Login confirmation supplies the actual selection summary'
    $script:accept = $true; $script:remember = $false
    Assert (Call $main 'ConfirmSelectedLaunch') 'An accepted login confirmation proceeds'
    Assert (!$settings.SkipLoginConfirmation) 'Accepting without remember keeps future login prompts'
    $script:remember = $true
    Assert (Call $main 'ConfirmSelectedLaunch') 'An accepted remembered login proceeds'
    Assert ($settings.SkipLoginConfirmation -and !$settings.SkipTeamLogoutConfirmation) 'Remember login changes only the login preference'
    $calls = $script:promptCalls; $script:accept = $false
    Assert (Call $main 'ConfirmSelectedLaunch') 'A remembered login skips its prompt'
    Assert ($script:promptCalls -eq $calls) 'Skipped login must not invoke the prompt'
    Assert (!(Call $main 'ConfirmGameAction' @($true,'Synthetic team logout'))) 'Logout still prompts and can be cancelled independently'
    Assert (!$settings.SkipTeamLogoutConfirmation -and $script:lastLogout -and $script:lastSummary -eq 'Synthetic team logout') 'Cancelled logout must not remember and receives its exact matched-client summary'
    $script:accept = $true
    Assert (Call $main 'ConfirmGameAction' @($true,'Synthetic team logout')) 'Confirmed logout proceeds'
    Assert ($settings.SkipLoginConfirmation -and $settings.SkipTeamLogoutConfirmation) 'Both preferences can be saved independently'
    $calls = $script:promptCalls; $script:accept = $false
    Assert (Call $main 'ConfirmGameAction' @($true,'Synthetic team logout')) 'A remembered logout skips its prompt'
    Assert ($calls -eq $script:promptCalls) 'Skipped logout must not invoke the prompt'
    $copy = $serializer.Deserialize($serializer.Serialize($settings), $settings.GetType())
    Assert ($copy.SkipLoginConfirmation -and $copy.SkipTeamLogoutConfirmation) 'Both choices survive profile serialization'
    $settings.SkipLoginConfirmation = $false
    Assert (!(Call $main 'ConfirmSelectedLaunch')) 'Re-enabling login confirmation restores its prompt'
    Assert ($settings.SkipTeamLogoutConfirmation) 'Re-enabling login leaves logout unchanged'
    $settings.SkipTeamLogoutConfirmation = $false
    Assert (!(Call $main 'ConfirmGameAction' @($true,'Synthetic team logout'))) 'Re-enabling logout confirmation restores its prompt'
    Assert (!(Field $main 'running')) 'Confirmation tests never start games'
} finally { $main.Dispose() }

$preview = Join-Path $PSScriptRoot '../logs/game-confirmations'
[IO.Directory]::CreateDirectory($preview) | Out-Null
foreach ($language in @('en','da')) {
    $assembly.GetType('AOLauncher.Localization.Texts').GetMethod('SetLanguage', $statics).Invoke($null, @($language)) | Out-Null
    foreach ($scale in @(1.0,1.5)) {
        foreach ($kind in @('LaunchConfirmationDialog','LogoutConfirmationDialog')) {
            $summary = if ($kind -eq 'LaunchConfirmationDialog') { "Log in 2 accounts on 1 login server?`r`n`r`n6 characters selected." } else { "Send /quit to 6 clients from team 'Evening team'?`r`n`r`nDoctor`r`nEnforcer`r`nSoldier`r`nKeeper`r`nFixer`r`nTrader`r`n`r`nThe game handles its normal logout countdown." }
            $dialog = [Activator]::CreateInstance($assembly.GetType('AOLauncher.' + $kind), [object[]]@($summary))
            try {
                if ($scale -gt 1) { $dialog.Font = [Drawing.Font]::new($dialog.Font.FontFamily,$dialog.Font.Size*$scale); $dialog.Scale([Drawing.SizeF]::new($scale,$scale)) }
                Show-Fixture $dialog
                $remember = $dialog.Controls.Find('dontShowAgain',$true)[0]
                Assert (!$remember.Checked -and $remember.Visible) 'Remember is visible and unchecked on every new prompt'
                Assert ($remember.Parent.ClientRectangle.Contains($remember.Bounds)) "$kind $language $scale checkbox fits its viewport"
                $expected = if ($language -eq 'da') { 'Vis ikke denne bekræftelse igen' } else { "Don't show this confirmation again" }
                Assert ($remember.Text -eq $expected) 'Remember checkbox uses the current language'
                $guide = $dialog.Controls.Find('confirmationSettingsGuide',$true)[0]
                Assert ($guide.Text.Contains($(if ($language -eq 'da') { 'Indstillinger' } else { 'Settings' }))) 'Prompt explains where to restore confirmations'
                Assert ([object]::ReferenceEquals($dialog.AcceptButton,$dialog.CancelButton)) 'Enter and Escape both default to Cancel'
                if ($scale -eq 1) {
                    $bitmap = [Drawing.Bitmap]::new($dialog.Width,$dialog.Height)
                    try { $dialog.DrawToBitmap($bitmap,[Drawing.Rectangle]::new(0,0,$dialog.Width,$dialog.Height)); $bitmap.Save((Join-Path $preview "$kind-$language.png"),[Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
                }
                $remember.Checked = $true
                $dialog.CancelButton.PerformClick(); [Windows.Forms.Application]::DoEvents()
                Assert ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::Cancel) 'Cancel remains effective after selecting remember'
            } finally { $dialog.Dispose() }
        }
    }
    $config = [Activator]::CreateInstance($assembly.GetType('AOLauncher.LauncherSettings'), $true)
    $config.SkipLoginConfirmation = $true; $config.SkipTeamLogoutConfirmation = $false
    $settingsDialog = [Activator]::CreateInstance($assembly.GetType('AOLauncher.SettingsDialog'), [object[]]@($config))
    try {
        Show-Fixture $settingsDialog
        $login = $settingsDialog.Controls.Find('confirmLogin',$true)[0]; $logout = $settingsDialog.Controls.Find('confirmTeamLogout',$true)[0]
        Assert (!$login.Checked -and $logout.Checked) 'Settings shows the two choices independently'
        $login.Checked = $true; $logout.Checked = $false
        Assert (!$settingsDialog.SkipLoginConfirmation -and $settingsDialog.SkipTeamLogoutConfirmation) 'Settings can re-enable login and disable logout separately'
        $settingsDialog.CancelButton.PerformClick(); [Windows.Forms.Application]::DoEvents()
        Assert ($config.SkipLoginConfirmation -and !$config.SkipTeamLogoutConfirmation) 'Cancelling Settings does not change either preference'
    } finally { $settingsDialog.Dispose() }
}
Write-Output "PASS: $checks game confirmation, independent persistence, cancellation, Settings and EN/DA layout checks. Synthetic profiles only; no game launched or logged out."
