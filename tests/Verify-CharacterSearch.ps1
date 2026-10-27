param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'),
    [string]$PreviewDirectory = (Join-Path $PSScriptRoot '..\logs\character-search-previews')
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
public static class SearchPreviewNative {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
}
'@
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$formType = $assembly.GetType('AOLauncher.MainForm')
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$script:checks = 0
function Assert([bool]$Value, [string]$Message) { if (!$Value) { throw $Message }; $script:checks++ }
function Field([string]$Name) { $formType.GetField($Name, $flags).GetValue($script:form) }
function Invoke-Main([string]$Name, [object[]]$Arguments = @()) { $formType.GetMethod($Name, $flags).Invoke($script:form, $Arguments) }
function Nodes($Collection) { foreach ($node in $Collection) { $node; Nodes $node.Nodes } }
function Characters { @(Nodes (Field 'tree').Nodes | Where-Object { $_.Tag -and $_.Tag.GetType().Name -eq 'CharacterConfig' }) }
function Layout($Control) { $Control.PerformLayout(); foreach ($child in $Control.Controls) { Layout $child }; [Windows.Forms.Application]::DoEvents() }
function Query([string]$Value) { (Field 'characterSearch').Text = $Value; Layout $script:form }
function Preview([string]$Name) {
    if (!$PreviewDirectory) { return }
    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($PreviewDirectory)) | Out-Null
    $bitmap = [Drawing.Bitmap]::new($script:form.Width, $script:form.Height)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $dc = $graphics.GetHdc()
        try { Assert ([SearchPreviewNative]::PrintWindow($script:form.Handle, $dc, 0)) 'Synthetic search preview must render.' }
        finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        $bitmap.Save((Join-Path $PreviewDirectory $Name), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}
foreach ($scale in @(1, 1.5)) {
    foreach ($language in @('en', 'da')) {
        $script:form = [Activator]::CreateInstance($formType, [object[]]@($true))
        try {
            $config = Field 'settings'
            $config.Language = $language
            Invoke-Main 'ApplyLanguage' | Out-Null
            $script:form.Size = $script:form.MinimumSize
            if ($scale -gt 1) {
                $script:form.Font = [Drawing.Font]::new($script:form.Font.FontFamily, $script:form.Font.Size * $scale, $script:form.Font.Style)
                $script:form.Scale([Drawing.SizeF]::new($scale, $scale))
            }
            $script:form.ShowInTaskbar = $false
            $script:form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
            $script:form.Location = [Drawing.Point]::new(-32000, -32000)
            $script:form.Show(); Layout $script:form
            Assert (Field 'smoke') 'Search fixture must use synthetic, non-persistent accounts.'
            Assert (@(Characters).Count -eq 5) 'All five synthetic characters must initially be present.'
            $before = $serializer.Serialize($config)
            Query '  aStEr  '
            Assert (@(Characters).Count -eq 1 -and (Characters)[0].Tag.CharacterId -eq 123456789) 'Search must trim whitespace and ignore name casing.'
            Assert ($serializer.Serialize($config) -ceq $before) 'Searching must not alter selections, presets or collapse state.'
            Assert ((Field 'characterSearchStatus').Text -match '2') 'Hidden selected characters must remain visible in search status.'
            Assert ((Field 'selectionLabel').Text -match '3') 'The total selected count must include hidden characters.'
            $status = Field 'characterSearchStatus'
            $needed = [Windows.Forms.TextRenderer]::MeasureText($status.Text, $status.Font, [Drawing.Size]::new($status.Width, 3000), [Windows.Forms.TextFormatFlags]::WordBreak)
            Assert ($needed.Height -le $status.Height) ('Search status must wrap without clipping: ' + $language + ' / ' + $scale)
            Assert ((Field 'characterSearch').Width -ge 100 * $scale) 'Search input must remain usable at the minimum window size.'
            $inputHost=(Field 'characterSearch').Parent;$clear=Field 'clearCharacterSearchButton'
            Assert ($inputHost.Height -eq $clear.Height -and $inputHost.Top -eq $clear.Top) 'Search border and Clear button must align at both scales'
            Assert ([Math]::Abs((Field 'characterSearch').Top*2+(Field 'characterSearch').Height-$inputHost.Height) -le 1) 'Single-line search text must be vertically centred'
            Preview ('search-' + $language + '-' + $scale + '.png')
            Query '123456791'
            Assert (@(Characters).Count -eq 1 -and (Characters)[0].Tag.Name -match 'Solstice') 'Exact character IDs must be searchable.'
            Assert ((Characters)[0].Parent.IsExpanded -and (Characters)[0].Parent.Parent.IsExpanded) 'Search must reveal matches in collapsed accounts.'
            Query 'WEEKEND'
            Assert (@(Characters).Count -eq 1) 'Account labels must be searchable.'
            Query 'third-example-account'
            Assert (@(Characters).Count -eq 1 -and (Characters)[0].Tag.Name -match 'Lyra') 'Account login names must be searchable.'
            Query 'Ithaca'
            Assert (@(Characters).Count -eq 1) 'Server names must be searchable.'
            Query 'aster'
            $accountNode = (Characters)[0].Parent
            $accountNode.Checked = $false
            Assert (!$config.Accounts[0].Characters[0].Selected -and $config.Accounts[0].Characters[1].Selected -and $config.Accounts[0].Characters[2].Selected) 'A filtered account checkbox must affect matches only.'
            $accountNode.Parent.Checked = $true
            Assert ($config.Accounts[0].Characters[0].Selected) 'A filtered server checkbox must select matching characters.'
            Invoke-Main 'SetAll' ([object[]]@($false)) | Out-Null
            Assert (!$config.Accounts[0].Characters[0].Selected -and $config.Accounts[0].Characters[1].Selected) 'Clear matches must preserve hidden selections.'
            Invoke-Main 'SetAll' ([object[]]@($true)) | Out-Null
            Invoke-Main 'SetAccountsCollapsed' ([object[]]@($true)) | Out-Null
            Query 'no-such-character'
            Assert ((Field 'tree').Nodes.Count -eq 0) 'An unmatched search must leave the roster empty.'
            Assert ((Field 'characterSearchStatus').Text -match $(if ($language -eq 'da') { 'Ingen chars' } else { 'No matching' })) 'No matches must have an explicit localized message.'
            Invoke-Main 'SetAll' ([object[]]@($false)) | Out-Null
            Assert (@($config.Accounts | ForEach-Object { $_.Characters } | Where-Object Selected).Count -eq 3) 'Clearing an empty result must preserve hidden selections.'
            Preview ('search-empty-' + $language + '-' + $scale + '.png')
            (Field 'clearCharacterSearchButton').PerformClick(); Layout $script:form
            Assert (@(Characters).Count -eq 5 -and (Field 'characterSearch').Text.Length -eq 0) 'Clear must restore the whole roster.'
            Assert ($serializer.Serialize($config) -ceq $before) 'Search expansion, selection roundtrip and clearing must preserve the original profile.'
            Assert ($config.Accounts[1].Collapsed -and !(Characters | Where-Object { $_.Tag.CharacterId -eq 123456791 })[0].Parent.IsExpanded) 'Clearing search must restore saved account collapse state.'
        } finally { $script:form.Dispose() }
    }
}
"PASS: $script:checks character-search checks in English/Danish at minimum size and simulated 150% DPI. Synthetic accounts and offline news only; no real profile, game login or password store was accessed."
