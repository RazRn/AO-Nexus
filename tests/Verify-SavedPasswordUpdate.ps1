param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\src\Launcher\bin\Release\AONexus.exe'),
    [string]$PreviewDirectory = (Join-Path $PSScriptRoot '..\logs\saved-password-update-previews'),
    [switch]$SkipUi
)
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Run in 32-bit Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Web.Extensions, System.Security, System.Windows.Forms, System.Drawing
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$static = [Reflection.BindingFlags]'Public,NonPublic,Static'
$texts = $assembly.GetType('AOLauncher.Localization.Texts', $true)
foreach ($registration in @('GuiStrings','NexusStrings')) { $assembly.GetType('AOLauncher.' + $registration, $true).GetMethod('Register', $static).Invoke($null, @()) | Out-Null }
$texts.GetMethod('SetLanguage', $static).Invoke($null, [object[]]@('en')) | Out-Null
$script:checks = 0
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message }; $script:checks++ }
function Make([string]$Name) { [Activator]::CreateInstance($assembly.GetType('AOLauncher.' + $Name, $true)) }
function FieldInfo($Control, [string]$Name) {
    $type = $Control.GetType()
    while ($type) { $field = $type.GetField($Name, $flags); if ($field) { return $field }; $type = $type.BaseType }
    throw "Field not found: $Name"
}
function Field($Control, [string]$Name) { (FieldInfo $Control $Name).GetValue($Control) }
function Account([string]$Id, [bool]$Remember) {
    $account = Make 'AccountConfig'; $account.Id = $Id; $account.Label = 'Synthetic account'; $account.Username = 'synthetic-' + $Id
    $account.Server = '192.0.2.47'; $account.Port = 7047; $account.RememberPassword = $Remember
    return $account
}
function Dialog($Account) { [Activator]::CreateInstance($assembly.GetType('AOLauncher.SavedPasswordDialog', $true), $flags, $null, [object[]]@($Account), $null) }
function AllNodes($Nodes) { foreach ($node in $Nodes) { $node; AllNodes $node.Nodes } }
Add-Type -Path (Join-Path $PSScriptRoot 'SavedPasswordUpdate.Tests.cs') -ReferencedAssemblies System.Core
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$testRoot = [IO.Path]::GetFullPath((Join-Path $tempParent ('AONexus-SavedPasswordUpdate-' + [Guid]::NewGuid().ToString('N'))))
try {
    [IO.Directory]::CreateDirectory($testRoot) | Out-Null
    $script:checks += [SavedPasswordUpdateTests]::Run($assembly, $testRoot)
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (!$resolved.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'AONexus-SavedPasswordUpdate-*') { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
$settings = Make 'LauncherSettings'; $settings.Accounts.Add((Account 'serialization-only' $true))
$json = (New-Object System.Web.Script.Serialization.JavaScriptSerializer).Serialize($settings)
Assert ($json -notmatch '"Password"\s*:') 'Account settings must retain no password property after adding the update feature.'

if (!$SkipUi) {
    Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class SavedPasswordUpdateNative {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
}
public static class SavedPasswordUpdateProbe {
    public static int Calls;
    public static string Received, Error;
    public static bool Throw;
    public static void Reset() { Calls=0; Received=null; Error=null; Throw=false; }
    public static string Save(string password) { Calls++; Received=password; if(Throw) throw new Exception("synthetic callback exception: "+password); return Error; }
}
'@
    function ShowFixture($Form, [double]$Scale) {
        $Form.ShowInTaskbar = $false; $Form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
        $Form.Location = [Drawing.Point]::new(-32000,-32000)
        if ($Scale -ne 1) { $Form.Scale([Drawing.SizeF]::new($Scale,$Scale)); $Form.Font = [Drawing.Font]::new($Form.Font.FontFamily,$Form.Font.Size * $Scale) }
        $Form.Show(); [Windows.Forms.Application]::DoEvents()
    }
    function Probe($Dialog) { $Dialog.SavePasswordAction = [Delegate]::CreateDelegate([Func[string,string]],[SavedPasswordUpdateProbe].GetMethod('Save')) }
    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PreviewDirectory)) | Out-Null
    foreach ($language in @('en','da')) {
        $texts.GetMethod('SetLanguage', $static).Invoke($null, [object[]]@($language)) | Out-Null
        foreach ($scale in @(1.0,1.5)) {
            $account = Account 'dialog-only' $false
            $dialog = Dialog $account
            try {
                ShowFixture $dialog $scale
                $password = Field $dialog 'passwordInput'; $confirmation = Field $dialog 'confirmPasswordInput'
                $save = Field $dialog 'SaveButton'; $errorLabel = Field $dialog 'ErrorLabel'; $fields = Field $dialog 'Fields'
                Assert ($password.UseSystemPasswordChar -and $confirmation.UseSystemPasswordChar -and !$password.Text -and !$confirmation.Text) 'Both new-password fields must start blank and masked.'
                Assert ($password.GetType().Name -eq 'NexusTextBox' -and $confirmation.GetType().Name -eq 'NexusTextBox') 'Password fields must use the shared dark control.'
                $expectedTitle = if ($language -eq 'da') { 'Opdatér gemt adgangskode — Synthetic account' } else { 'Update saved password — Synthetic account' }
                Assert ($dialog.Text -ceq $expectedTitle) 'The dedicated dialog must use the selected language.'
                $guide = @($fields.Controls | Where-Object Name -eq 'savedPasswordGuide')[0]
                $notice = @($fields.Controls | Where-Object Name -eq 'savedPasswordRememberNotice')[0]
                Assert ($guide -and $guide.Text.Length -gt 50 -and $notice -and $notice.Text.Length -gt 20) 'Dialog must explain encrypted local replacement, removal and enabling remembering.'
                $viewport = Field $dialog 'FieldsViewport'
                $identity = @($fields.Controls | Where-Object Name -eq 'savedPasswordAccount')[0]
                $identityTop = $viewport.PointToClient($identity.PointToScreen([Drawing.Point]::Empty)).Y
                Assert ($identityTop -ge 0 -and ($identityTop + $identity.Height) -le $viewport.ClientSize.Height) "The account identity must be fully visible when the dialog first opens ($language/$scale, top=$identityTop, height=$($identity.Height), viewport=$($viewport.ClientSize.Height))."
                $noticeTop = $viewport.PointToClient($notice.PointToScreen([Drawing.Point]::Empty)).Y
                Assert ($noticeTop -ge 0 -and ($noticeTop + $notice.Height) -le $viewport.ClientSize.Height) 'The opt-in disclosure must be fully visible when the dialog first opens.'
                foreach ($label in @($guide,$notice)) {
                    $measure = [Windows.Forms.TextRenderer]::MeasureText($label.Text,$label.Font,[Drawing.Size]::new($label.ClientSize.Width-$label.Padding.Horizontal,10000),[Windows.Forms.TextFormatFlags]'WordBreak,TextBoxControl,NoPrefix')
                    Assert ($label.Height -ge $measure.Height) 'English and Danish password help must fit at normal and enlarged scaling.'
                }
                [SavedPasswordUpdateProbe]::Reset(); Probe $dialog
                $save.PerformClick()
                Assert ([SavedPasswordUpdateProbe]::Calls -eq 0 -and $dialog.DialogResult -eq 'None' -and $errorLabel.Text.Length -gt 0) 'Empty inputs must keep the dialog open without saving.'
                $password.Text = 'synthetic-CaseSensitive'; $confirmation.Text = 'synthetic-casesensitive'; $save.PerformClick()
                Assert ([SavedPasswordUpdateProbe]::Calls -eq 0 -and $dialog.DialogResult -eq 'None') 'Confirmation must compare passwords case-sensitively before saving.'
                $confirmation.Text = $password.Text
                [SavedPasswordUpdateProbe]::Error = 'Synthetic storage unavailable'; $save.PerformClick()
                Assert ([SavedPasswordUpdateProbe]::Calls -eq 1 -and $dialog.DialogResult -eq 'None' -and $errorLabel.Text -ceq 'Synthetic storage unavailable') 'A storage failure must keep the form open with a useful error.'
                Assert (!$account.RememberPassword) 'The dialog itself must not change account settings before its policy callback succeeds.'
                [SavedPasswordUpdateProbe]::Throw = $true; $save.PerformClick()
                Assert ($dialog.DialogResult -eq 'None' -and !$errorLabel.Text.Contains('synthetic-CaseSensitive') -and !$errorLabel.Text.Contains('synthetic callback exception')) 'Unexpected callback exceptions must be redacted and keep the dialog open.'
                [SavedPasswordUpdateProbe]::Throw = $false
                [SavedPasswordUpdateProbe]::Error = 'Synthetic failure: the settings could not be saved and the new encrypted password could not be removed. Remember password remains disabled. Try Settings and Forget passwords, then update this account again.'
                $save.PerformClick(); [Windows.Forms.Application]::DoEvents()
                $errorMeasure = [Windows.Forms.TextRenderer]::MeasureText($errorLabel.Text,$errorLabel.Font,[Drawing.Size]::new($errorLabel.ClientSize.Width,10000),[Windows.Forms.TextFormatFlags]'WordBreak,TextBoxControl,NoPrefix')
                Assert ($errorLabel.Height -ge $errorMeasure.Height) 'The complete rollback failure message must fit without clipping at normal and enlarged scaling.'
                [SavedPasswordUpdateProbe]::Reset()
                $password.Text = ''; $confirmation.Text = ''; $errorLabel.Text = ''
                $fields.AutoScrollPosition = [Drawing.Point]::Empty; [Windows.Forms.Application]::DoEvents()
                $bitmap = [Drawing.Bitmap]::new($dialog.ClientSize.Width,$dialog.ClientSize.Height)
                try {
                    $graphics = [Drawing.Graphics]::FromImage($bitmap); $dc = $graphics.GetHdc()
                    try { Assert ([SavedPasswordUpdateNative]::PrintWindow($dialog.Handle,$dc,1)) 'The owned password dialog preview must render.' } finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
                    $bitmap.Save((Join-Path ([IO.Path]::GetFullPath($PreviewDirectory)) ('SavedPasswordDialog-' + $language + '-' + [int]($scale*100) + '.png')), [Drawing.Imaging.ImageFormat]::Png)
                } finally { $bitmap.Dispose() }
                $password.Text = ' synthetic-preserve-spaces '; $confirmation.Text = $password.Text; $save.PerformClick()
                Assert ([SavedPasswordUpdateProbe]::Received -ceq ' synthetic-preserve-spaces ' -and $dialog.DialogResult -eq 'OK') 'A matching password must be saved exactly without trimming and close successfully.'
                Assert (!$password.Text -and !$confirmation.Text) 'Successful saving must clear both password fields.'
            } finally { $dialog.Dispose() }

            $cancelled = Dialog (Account 'cancel-only' $true)
            try {
                ShowFixture $cancelled $scale; [SavedPasswordUpdateProbe]::Reset(); Probe $cancelled
                $cancelPassword = Field $cancelled 'passwordInput'; $cancelConfirm = Field $cancelled 'confirmPasswordInput'
                $cancelPassword.Text = 'synthetic-cancelled'; $cancelConfirm.Text = 'synthetic-cancelled'
                (Field $cancelled 'CancelAction').PerformClick(); $cancelled.Close()
                Assert ([SavedPasswordUpdateProbe]::Calls -eq 0) 'Cancel must never invoke a credential write.'
            } finally { $cancelled.Dispose() }
            Assert (!$cancelPassword.Text -and !$cancelConfirm.Text -and $null -eq $cancelled.GetType().GetProperty('SavePasswordAction',$flags).GetValue($cancelled,$null)) 'Disposal must clear both password fields and release the callback.'

            # MainForm is constructed only in offline smoke mode, with synthetic
            # settings. Temporarily exercise enabled-state calculation without
            # pumping messages or clicking the button; restore smoke before exit.
            $form = [Activator]::CreateInstance($assembly.GetType('AOLauncher.MainForm',$true),$flags,$null,[object[]]@($true),$null)
            $smokeInfo = FieldInfo $form 'smoke'
            try {
                Assert ((Field $form 'smoke') -and (Field (Field $form 'newsPanel') 'offlinePreview')) 'Main-window tests must use sample accounts and offline news.'
                (Field $form 'settings').Language = $language
                $form.GetType().GetMethod('ApplyLanguage',$flags).Invoke($form,@()) | Out-Null
                ShowFixture $form $scale
                $tree = Field $form 'tree'; $tree.ExpandAll(); $button = Field $form 'updatePasswordButton'
                foreach ($kind in @('ServerGroup','CharacterConfig','CreationAction','AccountConfig')) {
                    $node = @(AllNodes $tree.Nodes | Where-Object { $_.Tag -and $_.Tag.GetType().Name -eq $kind })[0]
                    Assert ($null -ne $node) "The synthetic tree must provide a $kind selection."
                    $tree.SelectedNode = $node; [Windows.Forms.Application]::DoEvents()
                    Assert ($button.Visible -eq ($kind -eq 'AccountConfig')) 'Update saved password must appear only for the selected account row.'
                }
                Assert (!$button.Enabled) 'Offline smoke mode must disable saved-password changes.'
                $smokeInfo.SetValue($form,$false)
                $form.GetType().GetMethod('UpdateSelection',$flags).Invoke($form,@()) | Out-Null
                Assert ($button.Enabled) 'An idle account selection must offer password updating.'
                (FieldInfo $form 'running').SetValue($form,$true)
                $form.GetType().GetMethod('UpdateSelection',$flags).Invoke($form,@()) | Out-Null
                Assert (!$button.Enabled) 'Password updating must be disabled while login is running.'
                (FieldInfo $form 'running').SetValue($form,$false); (FieldInfo $form 'storageBlocked').SetValue($form,$true)
                $form.GetType().GetMethod('UpdateSelection',$flags).Invoke($form,@()) | Out-Null
                Assert (!$button.Enabled) 'Password updating must be disabled while profile storage is blocked.'
            } finally { $smokeInfo.SetValue($form,$true); $form.Dispose() }
        }
    }
}
"PASS: $script:checks saved-password update checks; synthetic encrypted storage, failure rollback and owned offline dialogs only. No real profiles, credentials, network or games accessed."
