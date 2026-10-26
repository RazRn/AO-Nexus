param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Web.Extensions,System.Windows.Forms,System.Drawing
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$static=[Reflection.BindingFlags]'Static,Public,NonPublic'
$instance=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$settingsType=$assembly.GetType('AOLauncher.LauncherSettings',$true)
$settings=[Activator]::CreateInstance($settingsType,$true)
if($settings.MinimizeInactiveClients){throw 'Existing profiles must opt in.'}
$serializer=[Web.Script.Serialization.JavaScriptSerializer]::new()
$old=$serializer.Deserialize('{"Version":2,"QuietLaunch":true}', $settingsType)
if($old.MinimizeInactiveClients){throw 'Missing option enabled window management.'}
$settings.MinimizeInactiveClients=$true
$roundtrip=$serializer.Deserialize($serializer.Serialize($settings),$settingsType)
if(!$roundtrip.MinimizeInactiveClients){throw 'Option did not survive serialization.'}
$policy=$assembly.GetType('AOLauncher.BackgroundClients',$true).GetMethod('ShouldMinimize',$static)
$cases=@(
    @{Name='active game';Args=@($true,$true,$true,10,[uint32]10,$true,$false);Expected=$false},
    @{Name='switched to another game';Args=@($true,$true,$true,10,[uint32]11,$true,$false);Expected=$true},
    @{Name='switched to browser';Args=@($true,$true,$false,10,[uint32]20,$true,$false);Expected=$false},
    @{Name='switched to Nexus';Args=@($true,$true,$false,10,[uint32]21,$true,$false);Expected=$false},
    @{Name='unverified foreground process';Args=@($true,$true,$false,10,[uint32]11,$true,$false);Expected=$false},
    @{Name='focus transition';Args=@($true,$false,$true,10,[uint32]11,$true,$false);Expected=$false},
    @{Name='no foreground window';Args=@($true,$true,$false,10,[uint32]0,$true,$false);Expected=$false},
    @{Name='already minimized';Args=@($true,$true,$true,10,[uint32]11,$true,$true);Expected=$false},
    @{Name='hidden client';Args=@($true,$true,$true,10,[uint32]11,$false,$false);Expected=$false},
    @{Name='disabled';Args=@($false,$true,$true,10,[uint32]11,$true,$false);Expected=$false}
)
foreach($case in $cases){if($policy.Invoke($null,[object[]]$case.Args) -ne $case.Expected){throw $case.Name}}
foreach($language in @('en','da')) {
    $assembly.GetType('AOLauncher.Localization.Texts',$true).GetMethod('SetLanguage',$static).Invoke($null,@($language)) | Out-Null
    $form=[Activator]::CreateInstance($assembly.GetType('AOLauncher.SettingsDialog'),$instance,$null,[object[]]@($roundtrip),$null)
    try {
        $control=$form.Controls.Find('minimizeInactiveClients',$true)
        if($control.Count -ne 1 -or !$control[0].Checked){throw "Missing or incorrect option: $language"}
        $control[0].Checked=$false
        if($form.MinimizeInactiveClients){throw 'Disabling option was ignored.'}
    } finally {$form.Dispose()}
}
Write-Output 'PASS: default-off migration, persisted opt-in, focus/minimized/hidden guards, and English/Danish settings binding.'
