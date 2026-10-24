param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'),
    [string]$PreviewDirectory = (Join-Path $PSScriptRoot '..\logs\side-details-previews')
)
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Use 32-bit Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Web.Extensions
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SideDetailsNative {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
}
'@
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$formType = $assembly.GetType('AOLauncher.MainForm')
$settingsType = $assembly.GetType('AOLauncher.SettingsDialog')
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$script:checks = 0
$script:callbackCalls = 0
function Assert([bool]$Value, [string]$Message) { if (!$Value) { throw $Message }; $script:checks++ }
function Field([string]$Name) { $formType.GetField($Name, $flags).GetValue($script:form) }
function Invoke-Main([string]$Name, [object[]]$Arguments = @()) { $formType.GetMethod($Name, $flags).Invoke($script:form, $Arguments) }
function Descendants($Control) { foreach ($child in $Control.Controls) { $child; Descendants $child } }
function Nodes($Collection) { foreach ($node in $Collection) { $node; Nodes $node.Nodes } }
function Layout($Control) { $Control.PerformLayout(); foreach ($child in $Control.Controls) { Layout $child }; [Windows.Forms.Application]::DoEvents() }
function Preview($Control, [string]$Name) {
    if (!$PreviewDirectory) { return }
    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PreviewDirectory)) | Out-Null
    Layout $Control
    $bitmap = [Drawing.Bitmap]::new($Control.Width, $Control.Height)
    try {
        # Render only the synthetic fixture. Native printing respects the nested
        # viewport clips which DrawToBitmap ignores for native scrollbars.
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $dc = $graphics.GetHdc()
        try { Assert ([SideDetailsNative]::PrintWindow($Control.Handle,$dc,0)) 'The owned synthetic window must render successfully.' }
        finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        $bitmap.Save((Join-Path $PreviewDirectory $Name), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
}
function Show-Offscreen($Control) {
    $Control.ShowInTaskbar = $false
    $Control.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $Control.Location = [Drawing.Point]::new(-32000,-32000)
    $Control.Show()
    Layout $Control
}
function Fits-Text($Label, [string]$Description) {
    if (!$Label.Visible -or [string]::IsNullOrEmpty($Label.Text)) { return }
    $needed = [Windows.Forms.TextRenderer]::MeasureText($Label.Text, $Label.Font, [Drawing.Size]::new($Label.Width, 3000), [Windows.Forms.TextFormatFlags]::WordBreak)
    Assert ($needed.Height -le $Label.Height) ($Description + ' text must fit its label. Text=' + $Label.Text + ', bounds=' + $Label.Bounds + ', measured=' + $needed)
}
foreach ($scale in @(1, 1.5)) {
    foreach ($language in @('en','da')) {
        $script:form = [Activator]::CreateInstance($formType, [object[]]@($true))
        $dialog = $null
        try {
            $config = Field 'settings'
            $config.Language = $language
            Invoke-Main 'ApplyLanguage' | Out-Null
            $script:form.Size = $script:form.MinimumSize
            if ($scale -gt 1) {
                $script:form.Font = [Drawing.Font]::new($script:form.Font.FontFamily, $script:form.Font.Size * $scale, $script:form.Font.Style)
                $script:form.Scale([Drawing.SizeF]::new($scale,$scale))
            }
            Show-Offscreen $script:form
            Assert ((Field 'smoke') -and (Field 'newsPanel').GetType().GetField('offlinePreview', $flags).GetValue((Field 'newsPanel'))) 'Sidebar checks must use fictional accounts and offline news.'
            $tree = Field 'tree'
            $body = Field 'detailsBody'
            $details = @(Descendants $script:form | Where-Object Name -eq 'selectionDetails')[0]
            $accounts = $tree.Parent
            while ($accounts -and $accounts.GetType().Name -ne 'NexusPanel') { $accounts = $accounts.Parent }
            Assert ($null -ne $details -and $null -ne $accounts) 'The dashboard must contain distinct account and detail panels.'
            Assert ($body.Visible -and $details.Visible) 'Details must be visible immediately without opening a toggle.'
            Assert ($null -eq $formType.GetField('detailsToggleButton', $flags)) 'The old details show/hide toggle must be removed.'
            Assert ([object]::ReferenceEquals($accounts.Parent,$details.Parent)) 'Accounts and details must share a side-by-side container.'
            Assert ($details.Left -ge $accounts.Right + 8 * $scale) 'Details must sit to the right of accounts with a clear gap.'
            Assert ([Math]::Abs($details.Top - $accounts.Top) -le 1 -and [Math]::Abs($details.Height - $accounts.Height) -le 1) 'Accounts and details must align at the top and share their full container height.'
            Assert ($details.Width -lt $details.Parent.ClientSize.Width / 2 -and $accounts.Width -gt $details.Width) 'The details sidebar must remain narrower than the account list and use less than half its workspace.'
            Assert ($details.Width -ge 225 * $scale -and $details.Width -le 285 * $scale) 'The details sidebar must stay around 260 logical pixels, including at 150% scale.'
            Assert ($accounts.Parent.ClientRectangle.Contains($accounts.Bounds) -and $details.Parent.ClientRectangle.Contains($details.Bounds)) 'Both panels must fit inside their shared workspace.'
            Assert ($body.Parent.GetType().Name -eq 'Panel' -and $body.Parent.Parent.GetType().Name -eq 'SlimScrollHost') 'Long details must remain inside a slim scrolling viewport.'
            Assert (!(Field 'createCharacterButton').Visible) 'The create-char action must stay hidden for an ordinary server selection.'
            Assert ($null -eq $formType.GetField('forgetButton', $flags)) 'The dashboard must not contain the old password action field.'
            Assert (@(Descendants $script:form | Where-Object { $_.Name -eq 'forgetSavedPasswords' }).Count -eq 0) 'Forget passwords must be absent from the dashboard.'
            $before = $serializer.Serialize($config)
            $initialBounds = $details.Bounds
            Fits-Text (Field 'detailTitle') 'Server title'
            Fits-Text (Field 'detailText') 'Server details'
            Preview $script:form ('side-dashboard-' + $language + '-' + $scale + '.png')

            $accountNode = @(Nodes $tree.Nodes | Where-Object { $_.Tag.GetType().Name -eq 'AccountConfig' })[0]
            $tree.SelectedNode = $accountNode; Invoke-Main 'UpdateSelection' | Out-Null; Layout $script:form
            Assert ((Field 'detailTitle').Text -ceq $accountNode.Tag.Label -and (Field 'detailText').Text.Contains($accountNode.Tag.Username)) 'Selecting an account must show its name and username in the sidebar.'
            Assert ($body.Visible -and !(Field 'createCharacterButton').Visible -and $details.Bounds -eq $initialBounds) 'Account selection must keep the sidebar visible and its size stable.'
            Fits-Text (Field 'detailTitle') 'Account title'
            Fits-Text (Field 'detailText') 'Account details'
            $charNode = @(Nodes $tree.Nodes | Where-Object { $_.Tag.GetType().Name -eq 'CharacterConfig' -and !$_.Tag.NamePending })[0]
            $tree.SelectedNode = $charNode; Invoke-Main 'UpdateSelection' | Out-Null; Layout $script:form
            Assert ((Field 'detailTitle').Text -ceq $charNode.Tag.Name -and (Field 'detailText').Text.Contains($charNode.Tag.CharacterId.ToString())) 'Selecting a char must show its resolved name and ID.'
            Fits-Text (Field 'detailTitle') 'Char title'
            Fits-Text (Field 'detailText') 'Char details'
            $tree.SelectedNode = $tree.Nodes[0]; Invoke-Main 'UpdateSelection' | Out-Null; Layout $script:form
            $serverGroup = $tree.Nodes[0].Tag
            $serverName = $serverGroup.GetType().GetProperty('Name', $flags).GetValue($serverGroup, $null)
            $serverEndpoint = $serverGroup.GetType().GetProperty('Endpoint', $flags).GetValue($serverGroup, $null)
            Assert ((Field 'detailTitle').Text -ceq $serverName -and (Field 'detailText').Text.Contains($serverEndpoint)) 'Selecting a server must show its dynamic name and endpoint.'
            Assert ($serializer.Serialize($config) -ceq $before) 'Viewing account, char and server details must not change saved data or selected checkboxes.'

            $originalLabel = $accountNode.Tag.Label
            $accountNode.Tag.Label = 'Synthetic squad with a deliberately descriptive multi-word account label'
            $tree.SelectedNode = $accountNode; Invoke-Main 'UpdateSelection' | Out-Null; Layout $script:form
            Fits-Text (Field 'detailTitle') 'Long wrapping account title'
            Assert ((Field 'detailTitle').Height -gt (Field 'detailTitle').Font.Height -and (Field 'detailTitle').Width -lt $details.Width) 'A long title must wrap inside the narrow sidebar.'
            Assert ($details.Bounds -eq $initialBounds) 'A long title must not widen or grow the sidebar.'
            Preview $script:form ('side-long-title-' + $language + '-' + $scale + '.png')
            $accountNode.Tag.Label = $originalLabel

            $action = @(Nodes $tree.Nodes | Where-Object { $_.Tag.GetType().Name -eq 'CreationAction' })[0]
            $tree.SelectedNode = $action
            Invoke-Main 'UpdateSelection' | Out-Null
            Layout $script:form
            Assert ($body.Visible -and (Field 'createCharacterButton').Visible) 'Creation selection must expose instructions and its bottom action.'
            Fits-Text (Field 'detailTitle') 'Creation title'
            Fits-Text (Field 'detailText') 'Creation details'
            Fits-Text (Field 'detailHelp') 'Creation instructions'
            $create = Field 'createCharacterButton'
            Assert ($create.Parent.ClientRectangle.Contains($create.Bounds)) ('The creation button must fit inside the sidebar. Language=' + $language + ', scale=' + $scale + ', button=' + $create.Bounds + ', parent=' + $create.Parent.ClientRectangle)
            $createScreen = $create.RectangleToScreen($create.ClientRectangle)
            $viewport = $body.Parent.Parent
            $viewportScreen = $viewport.RectangleToScreen($viewport.ClientRectangle)
            $detailsScreen = $details.RectangleToScreen($details.ClientRectangle)
            Assert ($createScreen.Top -ge $viewportScreen.Bottom - 1 -and $createScreen.Bottom -le $detailsScreen.Bottom) 'The create action must remain below the scrolling details, inside the sidebar.'
            Assert ($createScreen.Bottom -ge $detailsScreen.Bottom - 30 * $scale) 'The create action must stay at the bottom of the sidebar.'
            Assert ($details.Bounds -eq $initialBounds) 'Displaying creation instructions must not grow the sidebar or shrink the account list.'
            $verticalBar = $viewport.GetType().GetProperty('VerticalBar', [Reflection.BindingFlags]'NonPublic,Instance').GetValue($viewport, $null)
            if ($verticalBar.Visible) {
                $verticalBar.GetType().GetMethod('ScrollTo', [Reflection.BindingFlags]'NonPublic,Instance').Invoke($verticalBar, [object[]]@([int]::MaxValue)) | Out-Null
                Layout $script:form
                $help = Field 'detailHelp'
                $helpScreen = $help.RectangleToScreen($help.ClientRectangle)
                Assert ($helpScreen.Bottom -le $viewportScreen.Bottom + 1 -and $helpScreen.Bottom -gt $viewportScreen.Top) 'Scrolling to the end must expose the final creation instructions.'
            }
            Assert ($serializer.Serialize($config) -ceq $before) 'Creation details must not alter accounts, presets or selection.'
            Assert (!(Field 'running') -and $null -eq (Field 'backendRunner')) 'Synthetic dashboard checks must never start a backend.'
            Preview $script:form ('side-creation-' + $language + '-' + $scale + '.png')

            $dialog = [Activator]::CreateInstance($settingsType, [object[]]@($config))
            if ($scale -gt 1) {
                $dialog.Font = [Drawing.Font]::new($dialog.Font.FontFamily, $dialog.Font.Size * $scale, $dialog.Font.Style)
                $dialog.Scale([Drawing.SizeF]::new($scale,$scale))
            }
            Show-Offscreen $dialog
            $button = @(Descendants $dialog | Where-Object Name -eq 'forgetSavedPasswords')[0]
            $status = @(Descendants $dialog | Where-Object Name -eq 'passwordActionStatus')[0]
            Assert ($null -ne $button -and $button.Visible -and $button.Enabled) 'Settings must contain the enabled password action.'
            Assert ($button.Parent.ClientRectangle.Contains($button.Bounds)) 'The password action must fit inside Settings.'
            $callback = [Func[bool]]{ $script:callbackCalls++; return $false }
            $settingsType.GetProperty('ForgetPasswordsAction').SetValue($dialog, $callback)
            $callCount = $script:callbackCalls
            $button.PerformClick(); Layout $dialog
            Assert ($script:callbackCalls -eq $callCount + 1) 'The injected Settings action must be called exactly once.'
            $failureText = if ($language -eq 'da') { 'Adgangskoderne kunne ikke ryddes. Prøv igen.' } else { 'Passwords could not be cleared. Try again.' }
            Assert ($status.Text -ceq $failureText) ('Settings must show a localized failure message when the action fails. Language=' + $language + ', actual=' + $status.Text)
            Fits-Text $status 'Password failure status'
            $callback = [Func[bool]]{ $script:callbackCalls++; return $true }
            $settingsType.GetProperty('ForgetPasswordsAction').SetValue($dialog, $callback)
            $button.PerformClick(); Layout $dialog
            Assert ($script:callbackCalls -eq $callCount + 2) 'Retrying must call the injected action exactly once again.'
            $successText = if ($language -eq 'da') { 'Gemte adgangskoder er ryddet. Konti og presets er uændrede.' } else { 'Saved passwords cleared. Accounts and presets are unchanged.' }
            Assert ($status.Text -ceq $successText) 'Settings must show a localized success message.'
            Assert ($serializer.Serialize($config) -ceq $before) 'Injected password actions must leave accounts, presets and selections unchanged.'
            Assert ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::None) 'Password actions must leave Settings open.'
            Fits-Text $status 'Password success status'
            $settingsFields = $status.Parent
            $settingsViewport = $settingsFields.Parent
            $settingsHost = $settingsViewport.Parent
            Assert ($settingsHost.GetType().Name -eq 'SlimScrollHost') 'Settings content must retain the slim scroll viewport.'
            Assert (!$settingsFields.HorizontalScroll.Visible) 'Settings text must wrap without a horizontal native scrollbar.'
            Assert ($settingsFields.ClientSize.Width -eq $settingsViewport.ClientSize.Width -and $settingsFields.ClientSize.Height -eq $settingsViewport.ClientSize.Height) 'Settings native chrome must be fully cropped while its usable client area matches the viewport.'
            $settingsBar = $settingsHost.GetType().GetProperty('VerticalBar', $flags).GetValue($settingsHost, $null)
            if ($settingsFields.VerticalScroll.Visible) { Assert $settingsBar.Visible 'Overflowing Settings must expose the slim vertical thumb.' }
            if ($settingsBar.Visible) {
                $settingsBar.GetType().GetMethod('ScrollTo', $flags).Invoke($settingsBar, [object[]]@([int]::MaxValue)) | Out-Null
                Layout $dialog
            }
            $stableBounds = $settingsFields.Bounds
            $stableThumb = $settingsBar.Bounds
            for ($settle = 0; $settle -lt 5; $settle++) { Layout $dialog }
            Assert ($settingsFields.Bounds -eq $stableBounds -and $settingsBar.Bounds -eq $stableThumb) 'Repeated Settings layout must settle without alternating scrollbar bounds.'
            Preview $dialog ('side-settings-' + $language + '-' + $scale + '.png')
            Assert ($status.Parent.ClientRectangle.Contains($status.Bounds)) ('The password result must fit inside Settings. Language=' + $language + ', scale=' + $scale + ', status=' + $status.Bounds + ', parent=' + $status.Parent.ClientRectangle)
        } finally {
            if ($dialog) { $dialog.Dispose() }
            $script:form.Dispose()
        }
    }
}
"PASS: $script:checks right-sidebar details and Settings action checks in English/Danish at minimum size and simulated 150% DPI. Synthetic accounts and offline news only; no real profiles, password store, network, backend or game was accessed."
