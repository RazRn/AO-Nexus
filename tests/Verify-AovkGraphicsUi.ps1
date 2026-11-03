param([Parameter(Mandatory=$true)][string]$PreparedGame, [string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
if (!$AssemblyPath) { $AssemblyPath = Join-Path $PSScriptRoot '../dist/AONexus.exe' }
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Use 32-bit Windows PowerShell -STA.' }
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('NexusGraphicsUi-' + [guid]::NewGuid().ToString('N'))
$preview = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../logs/graphics-previews'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
[IO.Directory]::CreateDirectory($preview) | Out-Null
$checks = 0
function Check($ok, $message) { if (!$ok) { throw $message }; $script:checks++ }
function Control($form, $name) { return $form.Controls.Find($name, $true)[0] }
try {
    foreach ($name in @('AnarchyOnline.exe','GUI.dll','Interfaces.dll','N3.dll','randy31_orig.dll','randy31-aovk-test.dll',
        'version-stock-rebirth.dll','version-aovk-test.dll','randy31.dll','version.dll','randy-vk.ini','NEXUS-AOVK-TEST.txt')) {
        Copy-Item -LiteralPath (Join-Path $PreparedGame $name) -Destination (Join-Path $temporary $name)
    }
    foreach ($language in @('en','da')) {
        $assembly.GetType('AOLauncher.Localization.Texts').GetMethod('SetLanguage',$static).Invoke($null,@($language)) | Out-Null
        foreach ($scale in @(1,1.5)) {
            $settings = [Activator]::CreateInstance($assembly.GetType('AOLauncher.LauncherSettings'),$true)
            $settings.GamePath = $temporary
            $dialog = [Activator]::CreateInstance($assembly.GetType('AOLauncher.SettingsDialog'),$flags,$null,@($settings),$null)
            try {
                if ($scale -ne 1) { $dialog.Font=[Drawing.Font]::new($dialog.Font.FontFamily,$dialog.Font.Size*$scale); $dialog.Scale([Drawing.SizeF]::new($scale,$scale)) }
                $dialog.ShowInTaskbar=$false; $dialog.StartPosition='Manual'; $dialog.Location=[Drawing.Point]::new(-18000,-18000)
                $dialog.Show(); [Windows.Forms.Application]::DoEvents()
                $tabs = Control $dialog 'settingsTabs'; $tabs.SelectedIndex = $tabs.TabCount - 1
                [Windows.Forms.Application]::DoEvents()
                $enabled = Control $dialog 'aovkEnabled'
                Check $enabled.Enabled 'prepared renderer toggle is available'
                Check $enabled.Checked 'AO-VK detected'
                $enabled.Checked=$false
                Check (!(Control $dialog 'aovkEffects').Enabled) 'off disables effects without clearing preferences'
                $enabled.Checked=$true
                (Control $dialog 'RVK_Enhance').Checked=$false
                Check (!(Control $dialog 'RVK_SunShadow').Enabled) 'enhancement dependencies disabled'
                Check (Control $dialog 'RVK_PerfHud').Enabled 'HUD remains independent'
                Check (Control $dialog 'RVK_NexusUI').Enabled 'Modern UI does not depend on world lighting'
                (Control $dialog 'RVK_Enhance').Checked=$true
                (Control $dialog 'aovkTestedPreset').PerformClick()
                Check ((Control $dialog 'aovkFiltering').SelectedIndex -eq 4) 'preset selects 16x filtering'
                Check (!(Control $dialog 'RVK_NexusUI').Checked) 'Lighting preset never opts into the UI preview'
                [Windows.Forms.Application]::DoEvents()
                $image=[Drawing.Bitmap]::new($dialog.Width,$dialog.Height)
                try { $dialog.DrawToBitmap($image,[Drawing.Rectangle]::new(0,0,$image.Width,$image.Height)); $image.Save((Join-Path $preview "graphics-$language-$scale.png")) }
                finally { $image.Dispose() }
                $save=$dialog.GetType().BaseType.GetField('SaveButton',$flags).GetValue($dialog)
                $bounds=$dialog.RectangleToClient($save.RectangleToScreen($save.ClientRectangle))
                Check ($bounds.Bottom -le $dialog.ClientSize.Height) 'Save stays visible'
                # Saving uses only the isolated temporary installation.
                if ($language -eq 'en' -and $scale -eq 1) {
                    $enabled.Checked=$false; $save.PerformClick()
                    Check ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::OK) 'Settings Save applies graphics'
                    Check ((Get-FileHash -LiteralPath (Join-Path $temporary 'randy31.dll')).Hash -eq '9D9E0DA25AC6F8C7ECDCE7A47A2409575945966A29E3B77850F7C62033A04BE3') 'UI switched to stock'
                    Copy-Item -LiteralPath (Join-Path $temporary 'randy31-aovk-test.dll') -Destination (Join-Path $temporary 'randy31.dll') -Force
                    Copy-Item -LiteralPath (Join-Path $temporary 'version-aovk-test.dll') -Destination (Join-Path $temporary 'version.dll') -Force
                }
            } finally { $dialog.Dispose() }
        }
    }
    # An ordinary Rebirth installation, with neither backups nor AO-VK settings.
    $fresh=Join-Path $temporary 'Fresh'; [IO.Directory]::CreateDirectory($fresh)|Out-Null
    foreach($name in @('AnarchyOnline.exe','GUI.dll','Interfaces.dll','N3.dll')){Copy-Item -LiteralPath (Join-Path $PreparedGame $name) -Destination (Join-Path $fresh $name)}
    Copy-Item -LiteralPath (Join-Path $PreparedGame 'randy31_orig.dll') -Destination (Join-Path $fresh 'randy31.dll')
    Copy-Item -LiteralPath (Join-Path $PreparedGame 'version-stock-rebirth.dll') -Destination (Join-Path $fresh 'version.dll')
    foreach($mode in @('cancel','enable','modern','accepted','disable')) {
        $settings=[Activator]::CreateInstance($assembly.GetType('AOLauncher.LauncherSettings'),$true);$settings.GamePath=$fresh
        $dialog=[Activator]::CreateInstance($assembly.GetType('AOLauncher.SettingsDialog'),$flags,$null,@($settings),$null)
        try {
            $dialog.ShowInTaskbar=$false;$dialog.StartPosition='Manual';$dialog.Location=[Drawing.Point]::new(-18000,-18000);$dialog.Show()
            $tabs=Control $dialog 'settingsTabs';$tabs.SelectedIndex=$tabs.TabCount-1;[Windows.Forms.Application]::DoEvents()
            Check (Control $dialog 'graphicsBrowse').Visible 'Any game folder can be chosen from Graphics'
            $enabled=Control $dialog 'aovkEnabled'
            Check $enabled.Enabled 'Unprepared normal installation is selectable'
            if($mode -eq 'cancel') { $enabled.Checked=$true; Check (!(Test-Path (Join-Path $fresh 'randy31_orig.dll'))) 'Selecting AO-VK does not install before Save'; continue }
            $enabled.Checked=($mode -ne 'disable')
            (Control $dialog 'RVK_NexusUI').Checked=($mode -eq 'modern')
            $save=$dialog.GetType().BaseType.GetField('SaveButton',$flags).GetValue($dialog);$save.PerformClick()
            Check ($dialog.DialogResult -eq [Windows.Forms.DialogResult]::OK) 'Save automatically installs or restores the normal folder'
            $expected=if($mode -eq 'modern'){$assembly.GetType('AOLauncher.AovkGraphics').GetField('UiRenderer',$static).GetRawConstantValue()}elseif($mode -ne 'disable'){'60759C61C7383EFA39AE4F09981ED3E42D7A25CE7C9D093E6791A5C4A98040C6'}else{'9D9E0DA25AC6F8C7ECDCE7A47A2409575945966A29E3B77850F7C62033A04BE3'}
            Check ((Get-FileHash -LiteralPath (Join-Path $fresh 'randy31.dll')).Hash -eq $expected) 'Selected mode reaches the expected renderer'
        } finally { $dialog.Dispose() }
    }
    # Recover a simulated interrupted transaction entirely through the button.
    $writeType=$assembly.GetType('AOLauncher.AovkGraphics+Write');$writes=[Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($writeType))
    $write=[Activator]::CreateInstance($writeType,$true)
    $writeType.GetField('Path',$flags).SetValue($write,(Join-Path $fresh 'randy31.dll'))
    $writeType.GetField('Before',$flags).SetValue($write,[IO.File]::ReadAllBytes((Join-Path $fresh 'randy31.dll')))
    $after=[IO.File]::ReadAllBytes((Join-Path $PreparedGame 'randy31-aovk-test.dll'))
    $writeType.GetField('After',$flags).SetValue($write,$after);$writes.Add($write)
    $recoveryArgs = [object[]]::new(2)
    $recoveryArgs[0] = [string]$fresh
    $recoveryArgs[1] = $writes.PSObject.BaseObject
    $assembly.GetType('AOLauncher.AovkGraphics').GetMethod('PrepareRecovery',$static).Invoke($null,$recoveryArgs)|Out-Null
    [IO.File]::WriteAllBytes((Join-Path $fresh 'randy31.dll'),$after)
    $launchSettings=[Activator]::CreateInstance($assembly.GetType('AOLauncher.LauncherSettings'),$true)
    $account=[Activator]::CreateInstance($assembly.GetType('AOLauncher.AccountConfig'),$true)
    $account.Label='Recovery test';$account.Username='synthetic';$account.GamePath=$fresh;$account.Server='192.0.2.80';$account.Port=7080
    $character=[Activator]::CreateInstance($assembly.GetType('AOLauncher.CharacterConfig'),$true)
    $character.Name='Synthetic';$character.CharacterId=980001;$character.Selected=$true;$account.Characters.Add($character)
    $launchSettings.Accounts.Add($account)
    $validate=$assembly.GetType('AOLauncher.MainForm').GetMethod('ValidateSelectedAccounts',$static)
    $launchFailure=$null
    try {$validate.Invoke($null,[object[]]@($launchSettings))|Out-Null} catch {$launchFailure=$_.Exception.GetBaseException()}
    Check ($launchFailure -is [ArgumentException] -and $launchFailure.Message.Contains('Graphics installation was interrupted')) 'Actual login validation blocks the interrupted folder'
    $settings=[Activator]::CreateInstance($assembly.GetType('AOLauncher.LauncherSettings'),$true);$settings.GamePath=$fresh
    $dialog=[Activator]::CreateInstance($assembly.GetType('AOLauncher.SettingsDialog'),$flags,$null,@($settings),$null)
    try {
        $dialog.ShowInTaskbar=$false;$dialog.StartPosition='Manual';$dialog.Location=[Drawing.Point]::new(-18000,-18000);$dialog.Show()
        $tabs=Control $dialog 'settingsTabs';$tabs.SelectedIndex=$tabs.TabCount-1;[Windows.Forms.Application]::DoEvents()
        Check (!(Control $dialog 'aovkEnabled').Enabled -and (Control $dialog 'aovkRecover').Visible) 'Interrupted installation offers recovery instead of a misleading toggle'
        (Control $dialog 'aovkRecover').PerformClick();[Windows.Forms.Application]::DoEvents()
        Check ((Control $dialog 'aovkEnabled').Enabled -and !(Control $dialog 'aovkEnabled').Checked -and !(Control $dialog 'aovkRecover').Visible) 'Recovery button restores a usable stock installation'
        $validate.Invoke($null,[object[]]@($launchSettings))|Out-Null
        Check $true 'Actual login validation accepts the recovered folder without launching a game'
    } finally { $dialog.Dispose() }
    Write-Host "PASS: $checks graphics UI checks in English/Danish at 100%/150%, fresh installation, cancellation and recovery; temporary installations only. Previews: $preview"
} finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolved -Leaf).StartsWith('NexusGraphicsUi-')) { throw 'Unexpected cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
