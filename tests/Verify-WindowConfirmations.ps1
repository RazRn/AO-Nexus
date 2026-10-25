param([string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'))
$ErrorActionPreference='Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Use 32-bit Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Instance'
$statics=[Reflection.BindingFlags]'Public,NonPublic,Static'
$checks=0
function Assert($value,$message) { if (!$value) { throw $message }; $script:checks++ }
function Field($o,$name) { $o.GetType().GetField($name,$flags).GetValue($o) }
function Call($o,$name,[object[]]$arguments=@()) { ($o.GetType().GetMethods($flags) | Where-Object { $_.Name -eq $name -and $_.GetParameters().Count -eq $arguments.Count } | Select-Object -First 1).Invoke($o,$arguments) }
function Show-Fixture($form) { $form.ShowInTaskbar=$false; $form.StartPosition='Manual'; $form.Location=[Drawing.Point]::new(-16000,-16000); $form.Show(); [Windows.Forms.Application]::DoEvents() }
$main=[Activator]::CreateInstance($assembly.GetType('AOLauncher.MainForm'),[object[]]@($true))
$script:promptCalls=0; $script:accept=$false; $script:remember=$true
$callback=[Func[bool,Tuple[bool,bool]]]{ param($minimize) $script:promptCalls++; return [Tuple]::Create([bool]$script:accept,[bool]$script:remember) }
try {
    Show-Fixture $main
    $main.GetType().GetField('WindowActionPrompt',$flags).SetValue($main,$callback)
    $settings=Field $main 'settings'
    Assert (!$settings.SkipTrayConfirmation -and !$settings.SkipQuitConfirmation) 'Both confirmations default on'
    $main.WindowState='Minimized'; [Windows.Forms.Application]::DoEvents()
    Assert ($main.Visible -and !(Field $main 'hiddenInTray') -and $main.WindowState -eq 'Normal') 'Cancel minimize restores the visible window'
    Assert (!$settings.SkipTrayConfirmation) 'Cancel does not save the checkbox'
    $script:accept=$true
    $main.WindowState='Minimized'; [Windows.Forms.Application]::DoEvents()
    Assert ((Field $main 'hiddenInTray') -and !$main.Visible -and $settings.SkipTrayConfirmation -and !$settings.SkipQuitConfirmation) 'Accepted minimize remembers only its own preference'
    Call $main 'ShowLauncher'; $previous=$script:promptCalls
    $main.WindowState='Minimized'; [Windows.Forms.Application]::DoEvents()
    Assert ($script:promptCalls -eq $previous) 'Remembered minimize no longer prompts'
    Call $main 'ShowLauncher'
    $script:accept=$false
    $main.Close(); [Windows.Forms.Application]::DoEvents()
    Assert (!$main.IsDisposed -and !(Field $main 'closing') -and !(Field $main 'stopRequested')) 'Cancel quit leaves launcher activity untouched'
    Assert (!$settings.SkipQuitConfirmation) 'Cancel quit does not save the checkbox'
    $settings.SkipTrayConfirmation=$false; $previous=$script:promptCalls
    Call $main 'MinimizeToTray'
    Assert ($script:promptCalls -eq $previous -and (Field $main 'hiddenInTray')) 'Windows startup can enter the tray without a user-action prompt'
    Call $main 'ShowLauncher'
    $script:accept=$true
    $main.Close(); [Windows.Forms.Application]::DoEvents()
    Assert ($main.IsDisposed -and $settings.SkipQuitConfirmation) 'Confirmed quit closes and saves its separate preference'
    $serializer=New-Object Web.Script.Serialization.JavaScriptSerializer
    $copy=$serializer.Deserialize($serializer.Serialize($settings),$settings.GetType())
    Assert ($copy.SkipQuitConfirmation -and !$copy.SkipTrayConfirmation) 'Both confirmation settings survive serialization independently'
} finally { $main.Dispose() }
$main=[Activator]::CreateInstance($assembly.GetType('AOLauncher.MainForm'),[object[]]@($true))
try {
    Show-Fixture $main
    $main.GetType().GetField('WindowActionPrompt',$flags).SetValue($main,$callback)
    $before=$script:promptCalls
    $event=[Windows.Forms.FormClosingEventArgs]::new([Windows.Forms.CloseReason]::ApplicationExitCall,$false)
    Call $main 'OnFormClosing' @($main,$event)
    Assert (!$event.Cancel -and $script:promptCalls -eq $before) 'An already-confirmed updater restart must not show another quit prompt'
} finally { $main.Dispose() }
foreach ($language in @('en','da')) {
    $assembly.GetType('AOLauncher.Localization.Texts').GetMethod('SetLanguage',$statics).Invoke($null,@($language))|Out-Null
    foreach ($scale in @(1.0,1.5)) {
        $whatsNew=[Activator]::CreateInstance($assembly.GetType('AOLauncher.WhatsNewDialog'),$true)
        try {
            Show-Fixture $whatsNew
            if($scale -gt 1){$whatsNew.Scale([Drawing.SizeF]::new($scale,$scale));[Windows.Forms.Application]::DoEvents()}
            $notes=$whatsNew.Controls.Find('installedReleaseNotes',$true)[0]
            Assert ($notes.ReadOnly -and $notes.Text.Length -gt 300 -and $notes.Width -gt 200) 'Release summary contains embedded readable notes at both DPI scales'
            Assert ($notes.SelectionLength -eq 0 -and $whatsNew.Controls.Find('updateSuccess',$true)[0].Top -ge 0) 'Release summary opens at the top without selecting all notes'
            $expected=if($language -eq 'da'){'Spil-GUI er markeret Eksperimentel'}else{'Game GUI is marked Experimental'}
            Assert ($notes.Text.Contains($expected)) 'Release notes use the selected app language'
            (Field $whatsNew 'CancelAction').PerformClick();[Windows.Forms.Application]::DoEvents()
            Assert (!$whatsNew.Visible) 'Close dismisses the nonmodal release summary'
        } finally {$whatsNew.Dispose()}
    }
    foreach ($minimize in @($true,$false)) {
        $dialog=[Activator]::CreateInstance($assembly.GetType('AOLauncher.WindowActionDialog'),$flags,$null,[object[]]@($minimize),$null)
        try {
            Show-Fixture $dialog
            $check=Field $dialog 'remember'
            Assert ($check.Visible -and !$check.Checked -and $check.Parent.ClientRectangle.Contains($check.Bounds)) 'Confirmation checkbox is visible and defaults off'
            $guide=$dialog.Controls.Find('windowActionGuide',$true)[0]
            Assert ($guide.Top -ge 0 -and $guide.Height -gt 30) 'Confirmation opens with its explanation visible above the checkbox'
            Assert ([object]::ReferenceEquals($dialog.AcceptButton,$dialog.CancelButton)) 'Enter defaults to Cancel'
        } finally { $dialog.Dispose() }
    }
}
$tree=[Activator]::CreateInstance($assembly.GetType('AOLauncher.NexusTreeView'),$true)
try {
    $parameters=$tree.GetType().GetProperty('CreateParams',$flags).GetValue($tree)
    Assert (($parameters.Style -band 0x80) -ne 0 -and !$tree.ShowNodeToolTips) 'Native and node tree tooltips are disabled'
    Assert ($null -eq $assembly.GetType('AOLauncher.NexusToolTips')) 'Custom hover tooltips have been removed'
} finally { $tree.Dispose() }
Write-Output "PASS: $checks confirmation, startup/update-exit, localization, release summary and tooltip removal checks. Synthetic fixtures only."
