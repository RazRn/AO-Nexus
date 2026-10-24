param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'),
    [string]$PreviewDirectory
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') {
    throw 'Run with 32-bit Windows PowerShell -NoProfile -STA.'
}
Add-Type -AssemblyName System.Web.Extensions
Add-Type -AssemblyName System.Windows.Forms
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CreationTreeTest {
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr item, IntPtr mask);
    public static int StateImage(IntPtr tree, IntPtr node) {
        return (int)SendMessage(tree, 0x1127, node, new IntPtr(0xF000)) & 0xF000;
    }
}
'@
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$static = [Reflection.BindingFlags]'NonPublic,Static'
$instance = [Reflection.BindingFlags]'NonPublic,Instance'
$formType = $assembly.GetType('AOLauncher.MainForm')
$accountType = $assembly.GetType('AOLauncher.AccountConfig')
$characterType = $assembly.GetType('AOLauncher.CharacterConfig')
$presetType = $assembly.GetType('AOLauncher.PresetService')
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$script:checks = 0
$script:nextId = 980000
function Assert([bool]$Condition, [string]$Description) {
    if (!$Condition) { throw $Description }
    $script:checks++
}
function Field([string]$Name) { return $formType.GetField($Name, $instance).GetValue($script:form) }
function Call([string]$Name, [object[]]$Arguments = @()) { return $formType.GetMethod($Name, $instance).Invoke($script:form, $Arguments) }
function Nodes($Collection) {
    foreach ($node in $Collection) {
        $node
        Nodes $node.Nodes
    }
}
function AccountNode($Account) { return @(Nodes $script:tree.Nodes | Where-Object { [object]::ReferenceEquals($_.Tag, $Account) })[0] }
function Actions { return @(Nodes $script:tree.Nodes | Where-Object { $_.Tag.GetType().Name -eq 'CreationAction' }) }
function ActionNode($Account) { return @((AccountNode $Account).Nodes | Where-Object { $_.Tag.GetType().Name -eq 'CreationAction' })[0] }
function Add-Account([string]$Label, $Slots, [int]$Characters, [bool]$Selected) {
    $account = [Activator]::CreateInstance($accountType)
    $account.Label = $Label; $account.Username = 'synthetic-' + $Label
    $account.Server = '192.0.2.80'; $account.Port = 7080; $account.ServerProfileId = 'creation-test'
    $account.GamePath = $script:tempRoot; $account.CharacterSlots = $Slots
    for ($index = 0; $index -lt $Characters; $index++) {
        $character = [Activator]::CreateInstance($characterType)
        $script:nextId++; $character.CharacterId = $script:nextId
        $character.Name = 'Synthetic char ' + $script:nextId; $character.Selected = $Selected
        $account.Characters.Add($character)
    }
    $script:config.Accounts.Add($account)
    return $account
}
function Expect-Rejection([scriptblock]$Action, [string]$Description) {
    $failure = $null
    try { & $Action | Out-Null } catch { $failure = $_.Exception.GetBaseException() }
    Assert ($failure -is [ArgumentException]) $Description
    return $failure.Message
}
function Layout($Control) {
    $Control.PerformLayout()
    foreach ($child in $Control.Controls) { Layout $child }
}
function Descendants($Control) { foreach ($child in $Control.Controls) { $child; Descendants $child } }
function Assert-CreationSidebar([string]$Language) {
    $sidebar = @(Descendants $script:form | Where-Object Name -eq 'selectionDetails')[0]
    $treeHost = $script:tree.Parent.Parent
    $treeOrigin = $script:form.PointToClient($treeHost.PointToScreen([Drawing.Point]::Empty))
    $sidebarOrigin = $script:form.PointToClient($sidebar.PointToScreen([Drawing.Point]::Empty))
    Assert ($sidebar.Visible -and $sidebarOrigin.X -ge $treeOrigin.X + $treeHost.Width) ('Creation details must stay visible to the right of the accounts in ' + $Language)
    Assert ($sidebar.Width -lt $treeHost.Width) ('Creation sidebar must remain narrower than the account list in ' + $Language)
    Assert ((Field 'detailsBody').Visible) 'Creation instructions must be visible without an expand action.'
    Assert (@(Descendants $sidebar | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Text -in @('Show details','Hide details','Vis detaljer','Skjul detaljer') }).Count -eq 0) 'Creation sidebar must not require an expand/collapse button.'
    $button = Field 'createCharacterButton'
    Assert ($button.Visible -and $button.Parent.ClientRectangle.Contains($button.Bounds)) ('Creation action must fit within the sidebar in ' + $Language)
}
function Save-Preview([string]$Name) {
    if (!$PreviewDirectory) { return }
    $directory = [IO.Path]::GetFullPath($PreviewDirectory)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    Layout $script:form
    $bitmap = [Drawing.Bitmap]::new($script:form.Width, $script:form.Height)
    try {
        # Render only this owned synthetic form. Native rendering preserves parent
        # clipping for slim-scroll viewports; DrawToBitmap fabricates pale gutters.
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $dc = $graphics.GetHdc()
        try { if (![CreationTreeTest]::PrintWindow($script:form.Handle, $dc, 0)) { throw 'Could not render synthetic creation preview.' } }
        finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        $path = Join-Path $directory $Name
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
        'PREVIEW: ' + $path
    } finally { $bitmap.Dispose() }
}
$script:tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('AOLauncher-CreationTest-' + [Guid]::NewGuid().ToString('N'))
$script:form = $null
try {
    [IO.Directory]::CreateDirectory($script:tempRoot) | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $script:tempRoot 'AnarchyOnline.exe'), [byte[]]@())
    $script:form = [Activator]::CreateInstance($formType, [object[]]@($true))
    $script:config = Field 'settings'
    $script:config.Accounts.Clear(); $script:config.Presets.Clear(); $script:config.SelectedPresetId = $null
    $script:config.LoginServers.Clear()
    $profile = [Activator]::CreateInstance($assembly.GetType('AOLauncher.LoginServerProfile'))
    $profile.Id = 'creation-test'; $profile.Name = 'Synthetic creation realm'
    $profile.Server = '192.0.2.80'; $profile.Port = 7080
    $profile.MaxAccounts = 2; $profile.MaxCharactersPerAccount = 1; $profile.CharacterSlots = 6
    $script:config.LoginServers.Add($profile)
    $available = Add-Account 'Available' 2 1 $true
    $full = Add-Account 'Full' 1 1 $false
    $unknown = Add-Account 'Unknown' 0 1 $true
    $empty = Add-Account 'Empty' $null 0 $false
    $script:tree = Field 'tree'
    $script:form.ShowInTaskbar = $false
    $script:form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $script:form.Location = [Drawing.Point]::new(-32000, -32000)
    $script:form.Size = $script:form.MinimumSize
    $script:form.Show()
    [Windows.Forms.Application]::DoEvents()
    Call 'RefreshTree' ([object[]]@($null)) | Out-Null
    foreach ($language in @('en', 'da')) {
        $script:config.Language = $language
        Call 'ApplyLanguage' | Out-Null
        $expectedTitle = if ($language -eq 'da') { 'Opret ny char' } else { 'Create new char' }
        $slotLabel = if ($language -eq 'da') { '1/2 char-pladser i listen' } else { '1/2 char slots listed' }
        $emptyLabel = if ($language -eq 'da') { '0/6 char-pladser i listen' } else { '0/6 char slots listed' }
        $unknownLabel = if ($language -eq 'da') { '1 chars i listen · antal pladser ukendt' } else { '1 chars listed · slot limit unknown' }
        Assert ((Actions).Count -eq 3) 'Only accounts with spare or unknown capacity may have a creation action.'
        Assert ($null -eq (ActionNode $full)) 'An account at its configured slot capacity must not have a creation action.'
        foreach ($account in @($available, $unknown, $empty)) {
            $action = ActionNode $account
            Assert ($null -ne $action -and $action.Text -ceq ('+ ' + $expectedTitle)) ('Creation action must be available and localized for ' + $account.Label + ' in ' + $language)
            Assert ([CreationTreeTest]::StateImage($script:tree.Handle, $action.Handle) -eq 0) 'Creation actions must have no native checkbox image.'
            $beforeCheck = $serializer.Serialize($script:config)
            $action.Checked = $true
            Assert (!$action.Checked -and $serializer.Serialize($script:config) -ceq $beforeCheck) 'Checking an action must be canceled without changing settings or selected characters.'
            $script:tree.SelectedNode = $action
            Call 'UpdateSelection' | Out-Null
            Assert ([object]::ReferenceEquals($formType.GetProperty('CurrentAccount', $instance).GetValue($script:form), $account)) 'Selecting an action must resolve its owning account.'
            Assert ((Field 'createCharacterButton').Visible -and !(Field 'createCharacterButton').Enabled) 'Creation button must be visible with spare capacity but disabled in smoke mode.'
            Assert ((Field 'createCharacterButton').Text -ceq $expectedTitle -and (Field 'detailTitle').Text -ceq $expectedTitle) 'Action details and button must use the current language.'
            Assert ($null -eq $formType.GetField('forgetButton', $instance)) 'Password removal must be absent from the dashboard action panel.'
            Assert ((Field 'detailsBody').Visible) 'Selecting a creation action must show its instructions in the always-visible sidebar.'
            Assert (!(Field 'removeButton').Enabled) 'Remove must be disabled for the ephemeral creation action.'
            Call 'RemoveSelected' ([object[]]@($null, [EventArgs]::Empty)) | Out-Null
            Assert ($serializer.Serialize($script:config) -ceq $beforeCheck) 'RemoveSelected must ignore the action without deleting its account or changing settings.'
            $help = (Field 'detailHelp').Text
            $expectedHelp = if ($language -eq 'da') { 'Hold AO Nexus åben' } else { 'Keep AO Nexus open' }
            $expectedAutomatic = if ($language -eq 'da') { 'tilføjes dens ID og navn automatisk' } else { 'its ID and name are added here automatically' }
            Assert ($help.Contains($expectedHelp) -and $help.Contains($expectedAutomatic)) 'Creation help must explain keeping Nexus open and automatic ID/name detection in both languages.'
        }
        $script:tree.SelectedNode = ActionNode $empty
        Call 'UpdateSelection' | Out-Null
        Assert ((Field 'detailText').Text.Contains($emptyLabel)) 'An empty account must show its inherited character-slot capacity.'
        $script:tree.SelectedNode = ActionNode $unknown
        Call 'UpdateSelection' | Out-Null
        Assert ((Field 'detailText').Text.Contains($unknownLabel)) 'Unknown slot capacity must be stated without implying a server-confirmed limit.'
        $script:tree.SelectedNode = AccountNode $full
        Call 'UpdateSelection' | Out-Null
        Assert (!(Field 'createCharacterButton').Visible) 'The creation button must be hidden when all configured slots are filled.'
        $script:tree.SelectedNode = AccountNode $available
        Call 'UpdateSelection' | Out-Null
        Assert (!(Field 'createCharacterButton').Visible) 'Ordinary account selection must hide the creation action button; creation starts from its dedicated row.'
        $script:tree.SelectedNode = ActionNode $available
        Call 'UpdateSelection' | Out-Null
        Assert ((Field 'detailText').Text.Contains($slotLabel) -and (Field 'detailText').Text.Contains('Synthetic creation realm')) 'Creation details must show the account capacity and dynamic server name.'
        Layout $script:form
        Assert-CreationSidebar $language
        foreach ($label in @((Field 'detailText'), (Field 'detailHelp'))) {
            $needed = [Windows.Forms.TextRenderer]::MeasureText($label.Text, $label.Font, [Drawing.Size]::new($label.Width, 1000), [Windows.Forms.TextFormatFlags]::WordBreak)
            Assert ($needed.Height -le $label.Height) ('Creation details/help must fit in ' + $language + ': ' + $label.Text)
        }
        $script:tree.TopNode = $script:tree.Nodes[0]
        Save-Preview ('character-creation-' + $language + '.png')
    }
    'PASS: bilingual creation actions, native checkbox hiding, checkbox cancellation, owning account, guarded removal, capacity summary, helper text and button visibility use isolated smoke data.'

    Call 'SetAll' ([object[]]@($true)) | Out-Null
    Assert ((@($script:config.Accounts | ForEach-Object Characters | Where-Object Selected).Count -eq 3) -and (@(Actions | Where-Object Checked).Count -eq 0)) 'Select all must select only the three saved characters and no action rows.'
    $preset = $presetType.GetMethod('Create', $static).Invoke($null, [object[]]@($script:config, 'Creation-is-not-a-preset-entry'))
    Assert ($preset.Characters.Count -eq 3 -and @($preset.Characters | Where-Object { $_.CharacterId -le 0 }).Count -eq 0) 'Preset capture must exclude creation actions and ID zero.'
    Call 'SetAll' ([object[]]@($false)) | Out-Null
    $applied = $presetType.GetMethod('Apply', $static).Invoke($null, [object[]]@($script:config, $preset))
    Assert ($applied.SelectedCount -eq 3 -and $applied.MissingCount -eq 0) 'Applying a preset must restore saved characters without creating an action entry.'
    $saved = $serializer.Serialize($script:config)
    Assert (!$saved.Contains('CreationAction') -and !$saved.Contains('"CharacterId":0') -and !$saved.Contains('"CreateCharacter"')) 'Persisted settings and presets must never contain the ephemeral action or a fake character ID.'

    $create = $formType.GetMethod('CreateCharacterRequest', $static)
    foreach ($account in @($available, $unknown, $empty)) {
        $beforeRequest = $serializer.Serialize($script:config)
        $request = $create.Invoke($null, [object[]]@($script:config, $account, 'synthetic-password-only'))
        Assert ($request.Characters.Count -eq 1 -and $request.Language -eq 'da') 'A creation request must contain exactly one action and the current language.'
        $entry = $request.Characters[0]
        Assert ($entry.CreateCharacter -and $entry.CharacterId -eq 0 -and $entry.AccountId -eq $account.Id -and $entry.Account -eq $account.Username -and $entry.Password -eq 'synthetic-password-only') 'The creation DTO must explicitly mark creation, use ID zero only in transit, and address the selected account.'
        Assert ($entry.GamePath -eq $script:tempRoot -and $entry.Server -eq '192.0.2.80' -and $entry.Port -eq 7080 -and $entry.ServerName -eq 'Synthetic creation realm' -and $entry.MaxAccounts -eq 2 -and $entry.MaxCharactersPerAccount -eq 1) 'The creation DTO must carry its account target, dynamic name and independent simultaneous-login limits.'
        Assert ($serializer.Serialize($script:config) -ceq $beforeRequest) 'Building a creation request must preserve normal selections, presets and saved settings.'
        $entry.Password = $null
    }
    Expect-Rejection { $create.Invoke($null, [object[]]@($script:config, $full, 'synthetic-password-only')) } 'A full account must reject creation before any launch request exists.' | Out-Null
    $outside = [Activator]::CreateInstance($accountType)
    Expect-Rejection { $create.Invoke($null, [object[]]@($script:config, $outside, 'synthetic-password-only')) } 'An account outside the current settings must not get a creation request.' | Out-Null
    Expect-Rejection { $create.Invoke($null, [object[]]@($script:config, $null, 'synthetic-password-only')) } 'A missing selected account must not get a creation request.' | Out-Null
    $credentials = New-Object 'System.Collections.Generic.Dictionary[string,string]'
    foreach ($account in $script:config.Accounts) { $credentials.Add($account.Id, 'synthetic-password-only') }
    $normal = $formType.GetMethod('CreateLaunchRequest', $static).Invoke($null, [object[]]@($script:config, $credentials.PSObject.BaseObject))
    Assert ($normal.Characters.Count -eq 3 -and @($normal.Characters | Where-Object { $_.CreateCharacter -or $_.CharacterId -le 0 }).Count -eq 0) 'Ordinary login requests must remain independent and contain only real selected characters.'
    foreach ($entry in $normal.Characters) { $entry.Password = $null }
    $credentials.Clear()
    Assert (!(Field 'running') -and $null -eq (Field 'backendRunner')) 'Tests must never start login preparation, a backend or a game client.'
    'PASS: select-all, preset save/apply and settings exclude action rows; isolated creation DTOs carry the chosen account/target/caps without changing ordinary selections, and full/nonmember/missing accounts are rejected.'
    $script:form.Font = [Drawing.Font]::new($script:form.Font.FontFamily, $script:form.Font.Size * 1.5, $script:form.Font.Style)
    $script:form.Scale([Drawing.SizeF]::new(1.5, 1.5))
    foreach ($language in @('en', 'da')) {
        $script:config.Language = $language
        Call 'ApplyLanguage' | Out-Null
        $script:tree.SelectedNode = ActionNode $available
        Call 'UpdateSelection' | Out-Null
        Layout $script:form
        Assert-CreationSidebar ($language + ' at simulated 150% DPI')
        foreach ($label in @((Field 'detailText'), (Field 'detailHelp'))) {
            $needed = [Windows.Forms.TextRenderer]::MeasureText($label.Text, $label.Font, [Drawing.Size]::new($label.Width, 1000), [Windows.Forms.TextFormatFlags]::WordBreak)
            Assert ($needed.Height -le $label.Height) ('Creation details/help must fit at simulated 150% DPI in ' + $language + ': ' + $label.Text)
        }
        $button = Field 'createCharacterButton'
        Assert ($button.Visible -and $button.Parent.ClientRectangle.Contains($button.Bounds) -and $button.Height -ge $button.PreferredSize.Height) ('Creation button must fit at simulated 150% DPI in ' + $language + ': button=' + $button.Bounds + ', parent=' + $button.Parent.ClientRectangle + ', preferred=' + $button.PreferredSize)
        $script:tree.TopNode = $script:tree.Nodes[0]
        Save-Preview ('character-creation-' + $language + '-150.png')
    }
    'PASS: creation details, help and button fit English/Danish at minimum size and simulated 150% DPI.'
    "PASS: $script:checks character creation checks; no real settings, credentials, games or login connections used."
} finally {
    if ($script:form) { $script:form.Dispose() }
    $resolvedTemp = [IO.Path]::GetFullPath($script:tempRoot)
    $allowedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolvedTemp.StartsWith($allowedTemp, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedTemp) -notmatch '^AOLauncher-CreationTest-[0-9a-f]{32}$') { throw 'Refusing cleanup outside the isolated test directory.' }
    if (Test-Path -LiteralPath $resolvedTemp) { Remove-Item -LiteralPath $resolvedTemp -Recurse -Force }
}
