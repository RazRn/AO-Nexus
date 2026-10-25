param([string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'))
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Use 32-bit Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.Web.Extensions
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance'
$checks = 0
function Assert($value, $message) { if (!$value) { throw $message }; $script:checks++ }
function Field($target, $name) { $target.GetType().GetField($name, $flags).GetValue($target) }
function Call($target, $name, [object[]]$arguments = @()) { $target.GetType().GetMethod($name, $flags).Invoke($target, $arguments) }
function Show-Fixture($form) { $form.ShowInTaskbar = $false; $form.StartPosition = 'Manual'; $form.Location = [Drawing.Point]::new(-16000,-16000); $form.Show(); [Windows.Forms.Application]::DoEvents() }
$main = [Activator]::CreateInstance($assembly.GetType('AOLauncher.MainForm'), [object[]]@($true))
$serializer = New-Object Web.Script.Serialization.JavaScriptSerializer
try {
    Show-Fixture $main
    $settings = Field $main 'settings'; $preset = $settings.Presets[0]
    # Exercise the actual Edit/Save-new entry points and their modal save/cancel paths.
    function Edit-Fixture([string]$action, [string]$mode) {
        $script:editorError = $null
        $timer = New-Object Windows.Forms.Timer; $timer.Interval = 50
        $handler = {
            $timer.Stop()
            $dialog = @($main.OwnedForms | Where-Object { $_.GetType().Name -eq 'PresetNameDialog' })[0]
            try {
                Assert ($null -ne $dialog) 'Preset action must open the editor'
                Assert (Field $main 'editingPreset') 'Shortcut dispatch must be suspended throughout the modal edit'
                Assert ($dialog.Controls.Find('replacePresetCharacters',$true).Count -eq 0) 'Edit only manages the name and shortcuts'
                (Field $dialog 'nameInput').Text = 'Edited fixture'
                $binding = [Activator]::CreateInstance($assembly.GetType('AOLauncher.HotkeyBinding'), $true)
                $binding.Key = 117; $binding.Modifiers = 3
                (Field $dialog 'startHotkeyField').GetType().GetField('Binding',$flags).SetValue((Field $dialog 'startHotkeyField'),$binding)
                if ($mode -eq 'cancel') { (Field $dialog 'CancelAction').PerformClick() }
                else {
                    (Field $dialog 'SaveButton').PerformClick()
                    Assert ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::OK) 'Save should accept the valid draft'
                }
            } catch { $script:editorError = $_; if ($dialog) { $dialog.Close() } }
        }
        $timer.Add_Tick($handler); $timer.Start()
        try { Call $main $action @($null, [EventArgs]::Empty) }
        finally { $timer.Stop(); $timer.Remove_Tick($handler); $timer.Dispose() }
        if ($script:editorError) { throw $script:editorError }
        Assert (!(Field $main 'editingPreset')) 'Closing the editor resumes shortcut dispatch'
    }
    $before = $serializer.Serialize($settings)
    Edit-Fixture 'EditCurrentPreset' 'cancel'
    Assert ($serializer.Serialize($settings) -ceq $before) 'Cancel must preserve all saved settings'
    $members = $serializer.Serialize($preset.Characters)
    foreach ($account in $settings.Accounts) { foreach ($char in $account.Characters) { $char.Selected = $false } }
    Edit-Fixture 'EditCurrentPreset' 'save'
    Assert ($preset.Name -eq 'Edited fixture' -and $preset.StartHotkey.Key -eq 117) 'Save commits name and shortcut together'
    Assert ($serializer.Serialize($preset.Characters) -ceq $members) 'Editing name or shortcut preserves saved members'
    Call $main 'UpdateCurrentPreset' @($null,[EventArgs]::Empty)
    Assert ($preset.Characters.Count -eq 0) 'Explicit replacement saves the current empty selection'
    Assert ($preset.Name -eq 'Edited fixture' -and $preset.StartHotkey.Key -eq 117) 'Update preserves the name and shortcuts'
    Assert (!(Field $main 'updatePresetButton').Enabled) 'Update is disabled after the current selection has been saved'
    $preset.Name = 'Existing fixture'; $preset.StartHotkey = $null
    Edit-Fixture 'SaveNewPreset' 'save'
    Assert ($settings.Presets.Count -eq 2 -and $settings.Presets[1].StartHotkey.Key -eq 117) 'Create can save its own shortcut'

    foreach ($language in @('en','da')) {
        $settings.Language = $language; Call $main 'ApplyLanguage'
        foreach ($scale in @(1.0,1.5)) {
            $editor = [Activator]::CreateInstance($assembly.GetType('AOLauncher.PresetNameDialog'), [object[]]@('Fixture', 'Example', [string[]]@('Reserved'), $null, $null, 12, $true))
            try {
                Show-Fixture $editor
                if ($scale -gt 1) { $editor.Scale([Drawing.SizeF]::new($scale,$scale)); [Windows.Forms.Application]::DoEvents() }
                $field = Field $editor 'campHotkeyField'; $guide = $editor.Controls.Find('presetShortcutGuide',$true)[0]
                Assert ($field.Visible -and $field.Width -gt 150 -and $field.Height -ge 30) 'Editor shortcut fields remain usable at both DPI sizes'
                Assert ($field.Parent.ClientRectangle.Contains($field.Bounds)) 'Shortcut field must fit its row'
                Assert ($guide.Height -ge [Windows.Forms.TextRenderer]::MeasureText($guide.Text,$guide.Font,[Drawing.Size]::new($guide.Width,10000),[Windows.Forms.TextFormatFlags]::WordBreak).Height) 'Shortcut guide must not clip'
                (Field $editor 'nameInput').Text = 'Reserved'; (Field $editor 'SaveButton').PerformClick()
                Assert ($editor.DialogResult -eq [Windows.Forms.DialogResult]::None) 'Duplicate names must not close the editor'
            } finally { $editor.Dispose() }
        }
    }
    Call $main 'OpenLog'
    $viewer = Field $main 'logViewer'
    $viewer.Location = [Drawing.Point]::new(-16000,-16000)
    Call $main 'OpenLog'
    Assert ([object]::ReferenceEquals($viewer, (Field $main 'logViewer'))) 'Opening log twice reuses its window'
    Call $viewer 'SetLogText' @("[01] Alpha ready`r`n[02] Beta connected`r`n[03] ALPHA done`r`n")
    (Field $viewer 'search').Text = 'alpha'; Call $viewer 'RefreshLog'
    $output = Field $viewer 'output'
    Assert ($output.ReadOnly -and $output.Text.Contains('Alpha') -and $output.Text.Contains('ALPHA') -and !$output.Text.Contains('Beta')) 'Search is case-insensitive and log remains read-only'
    (Field $viewer 'search').Text = '[02]'; Call $viewer 'RefreshLog'
    Assert ($output.Text -eq '[02] Beta connected') 'Search uses literal text, not regex syntax'
    (Field $viewer 'search').Text = 'absent'; Call $viewer 'RefreshLog'
    Assert ($output.TextLength -eq 0) 'No matches shows an empty filtered log'
    (Field $viewer 'search').Clear(); Call $viewer 'RefreshLog'
    Assert ($output.Lines.Count -eq 3) 'Clearing the filter restores the full log'
    Call $main 'AppendLog' @('LIVE-FIXTURE entry'); Call $viewer 'RefreshLog'
    Assert ($output.Text.Contains('LIVE-FIXTURE entry')) 'Open viewer receives new launcher entries'
    $log = Field $main 'log'
    Assert ($log.Font.Name -eq 'Consolas' -and $log.BackColor.GetBrightness() -lt .1 -and $log.ForeColor.G -gt $log.ForeColor.R) 'Compact log uses the terminal palette and font'
    $viewer.GetType().GetField('ConfirmClearLog',$flags).SetValue($viewer,[Func[bool]]{return $false})
    $originalLog=(Field $main 'activityLog').ToString()
    Call $viewer 'ClearActivity'
    Assert ((Field $main 'activityLog').ToString() -eq $originalLog) 'Cancel preserves the complete activity log'
    (Field $viewer 'search').Text='absent';Call $viewer 'RefreshLog'
    Assert ($viewer.Controls.Find('clearActivityLog',$true)[0].Enabled) 'Clear remains available when a filter hides all entries'
    $viewer.GetType().GetField('ConfirmClearLog',$flags).SetValue($viewer,[Func[bool]]{return $true})
    Call $viewer 'ClearActivity'
    Assert ((Field $main 'activityLog').Length -eq 0 -and (Field $main 'activityLogBytes') -eq 0 -and $log.TextLength -eq 0 -and $output.TextLength -eq 0) 'Confirmed clear resets stored history, byte count and both views'
    Assert ((Field $viewer 'search').TextLength -eq 0 -and !$viewer.Controls.Find('clearActivityLog',$true)[0].Enabled) 'Confirmed clear removes the filter and disables the empty-log action'
    $viewer.Close();Call $main 'OpenLog';$viewer=Field $main 'logViewer';Call $viewer 'RefreshLog'
    Assert ((Field $viewer 'output').TextLength -eq 0) 'Reopening does not restore cleared entries'
    Call $main 'AppendLog' @('NEW-AFTER-CLEAR');Call $viewer 'RefreshLog'
    Assert ((Field $viewer 'output').Text.Contains('NEW-AFTER-CLEAR') -and !(Field $viewer 'output').Text.Contains('LIVE-FIXTURE')) 'New activity resumes without old cleared history'
    $viewer.Close(); Call $main 'AppendLog' @('After viewer close')
    Call $main 'OpenLog'
    Assert (![object]::ReferenceEquals($viewer,(Field $main 'logViewer'))) 'A closed viewer can be reopened safely'
} finally { $main.Dispose() }
Write-Output "PASS: $checks preset editor, cancel/save, DPI and live searchable log checks; no real profiles or games touched."
