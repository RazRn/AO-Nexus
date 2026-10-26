param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Web.Extensions
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$settingsType=$assembly.GetType('AOLauncher.LauncherSettings',$true)
$dialogType=$assembly.GetType('AOLauncher.SettingsDialog',$true)
$requestType=$assembly.GetType('AOLauncher.LaunchRequest',$true)
$json=New-Object Web.Script.Serialization.JavaScriptSerializer
foreach($enabled in @($true,$false)) {
    $settings=[Activator]::CreateInstance($settingsType,$true)
    if (!$settings.FastStartup) { throw 'New profiles must enable the guarded fast path.' }
    $settings.FastStartup=$enabled
    $roundTrip=$json.Deserialize($json.Serialize($settings),$settingsType)
    if ($roundTrip.FastStartup -ne $enabled) { throw 'Fast loading preference was not preserved.' }
    $dialog=[Activator]::CreateInstance($dialogType,[object[]]@($roundTrip))
    try {
        $checkbox=$dialog.Controls.Find('fastStartup',$true)
        if ($checkbox.Count -ne 1 -or $checkbox[0].Checked -ne $enabled) { throw 'Settings checkbox does not reflect saved choice.' }
        $checkbox[0].Checked=!$enabled
        if ($dialog.FastStartup -ne !$enabled) { throw 'Settings does not expose the changed choice.' }
    } finally { $dialog.Dispose() }
    $request=[Activator]::CreateInstance($requestType,$true)
    $request.FastStartup=$enabled
    if ($json.Deserialize($json.Serialize($request),$requestType).FastStartup -ne $enabled) { throw 'Launch request lost the preference.' }
}
if (!$json.Deserialize('{}',$settingsType).FastStartup) { throw 'Older profiles must get the guarded default.' }
'PASS: fast loading preference, old-profile migration, opt-out, settings control and request serialization.'
