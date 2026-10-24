param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\logs\previews')
)
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Use 32-bit Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
[Windows.Forms.Application]::EnableVisualStyles()
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$formType = $assembly.GetType('AOLauncher.MainForm')
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
function Save-Preview($Form, [string]$Name) {
    $Form.PerformLayout()
    [Windows.Forms.Application]::DoEvents()
    $bitmap = [Drawing.Bitmap]::new($Form.Width, $Form.Height)
    try {
        $Form.DrawToBitmap($bitmap, [Drawing.Rectangle]::new(0,0,$Form.Width,$Form.Height))
        $bitmap.Save((Join-Path $OutputDirectory $Name), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}
$form = [Activator]::CreateInstance($formType, [object[]]@($true))
try {
    $form.ShowInTaskbar = $false
    $form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $form.Location = [Drawing.Point]::new(-32000,-32000)
    $form.Show()
    [Windows.Forms.Application]::DoEvents()
    Save-Preview $form 'nexus-dashboard-en.png'
    $config = $formType.GetField('settings', $flags).GetValue($form)
    $tree = $formType.GetField('tree', $flags).GetValue($form)
    $tree.SelectedNode = $tree.Nodes[0].Nodes[0]
    $formType.GetMethod('UpdateSelection', $flags).Invoke($form, @()) | Out-Null
    Save-Preview $form 'nexus-account-en.png'
    $form.Size = $form.MinimumSize
    Save-Preview $form 'nexus-minimum-en.png'
    $config.Language = 'da'
    $formType.GetMethod('ApplyLanguage', $flags).Invoke($form, @()) | Out-Null
    Save-Preview $form 'nexus-minimum-da.png'
    $form.Font = [Drawing.Font]::new($form.Font.FontFamily, $form.Font.Size * 1.5, $form.Font.Style)
    $form.Scale([Drawing.SizeF]::new(1.5, 1.5))
    Save-Preview $form 'nexus-minimum-da-150.png'
    'PASS: offscreen synthetic dashboard previews generated without real settings or network requests.'
} finally { $form.Dispose() }
