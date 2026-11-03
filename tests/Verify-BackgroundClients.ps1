param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'),[string]$AovkFolder)
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
$background=$assembly.GetType('AOLauncher.BackgroundClients',$true)
$graphics=$assembly.GetType('AOLauncher.AovkGraphics',$true)
$known=$background.GetMethod('KnownVkRenderer',$static)
foreach($name in @('VkRenderer','Release081Renderer','UiRenderer','PreviousUiRenderer','PreviousUi13Renderer','PreviousUi14Renderer','PreviousUi15Renderer')) {
    if(!$known.Invoke($null,@($graphics.GetField($name,$static).GetRawConstantValue()))){throw "Recognized AO-VK build rejected: $name"}
}
if($known.Invoke($null,@(('F'*64)))){throw 'Unknown renderer admitted'}
$inspect=$background.GetMethod('Inspect',$static)
function Allowed($mode){$mode.GetType().GetField('Allowed',$instance).GetValue($mode)}
function Description($mode){$mode.GetType().GetField('Description',$instance).GetValue($mode)}
[string]$temporary=Join-Path ([IO.Path]::GetTempPath()) ('NexusBackgroundTests-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary)|Out-Null
try {
    $missing=$inspect.Invoke($null,@($temporary))
    if((Allowed $missing) -or !(Description $missing)){throw 'Missing renderer needs an explicit skip reason'}
    [IO.File]::WriteAllText((Join-Path $temporary 'randy31.dll'),'unknown renderer')
    if(Allowed ($inspect.Invoke($null,@($temporary)))){throw 'Unknown renderer admitted'}
    if($AovkFolder) {
        $live=$inspect.Invoke($null,@([IO.Path]::GetFullPath($AovkFolder)))
        if(!(Allowed $live)){throw ('Selected AO-VK installation was rejected: '+(Description $live))}
        # Read-only source. Mutations below affect copies in the checked temporary directory only.
        foreach($name in @('randy31.dll','version.dll','randy31_orig.dll','version-stock-rebirth.dll','AnarchyOnline.exe','GUI.dll','Interfaces.dll','N3.dll','randy-vk.ini')) {
            $source=Join-Path $AovkFolder $name
            if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination (Join-Path $temporary $name) -Force}
        }
        if(!(Allowed ($inspect.Invoke($null,@($temporary))))){throw 'Valid copied AO-VK installation was rejected'}
        $patch=Join-Path $temporary 'version.dll'
        if(Test-Path -LiteralPath $patch) {
            $original=[IO.File]::ReadAllBytes($patch);$bad=[byte[]]$original.Clone();$bad[0]=$bad[0] -bxor 1;[IO.File]::WriteAllBytes($patch,$bad)
            if(Allowed ($inspect.Invoke($null,@($temporary)))){throw 'Mismatched/unverified AO-VK patch was admitted'}
            [IO.File]::WriteAllBytes($patch,$original)
        }
        $pending=Join-Path $temporary 'NexusGraphics.pending.json';[IO.File]::WriteAllText($pending,'{}')
        if(Allowed ($inspect.Invoke($null,@($temporary)))){throw 'Pending graphics repair was admitted'}
        [IO.File]::Delete($pending)
        [IO.File]::WriteAllText((Join-Path $temporary 'Cheetah.dll'),'new engine fixture')
        if(Allowed ($inspect.Invoke($null,@($temporary)))){throw 'New engine was admitted'}
        Write-Output 'PASS: selected AO-VK installation and isolated copy accepted; mismatched patch, pending repair and new engine rejected.'
    }
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary);$tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($resolved).StartsWith('NexusBackgroundTests-')){throw 'Unsafe fixture cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Output 'PASS: default-off migration, persisted opt-in, focus/minimized/hidden guards, supported AO-VK builds, skip reasons and English/Danish settings binding.'
