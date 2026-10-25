param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance';$checks=0
function Assert($ok,$message){if(!$ok){throw $message};$script:checks++}
function Model($name){[Activator]::CreateInstance($a.GetType('AOLauncher.'+$name),$true)}
function Preset($method,[object[]]$arguments){for($i=0;$i -lt $arguments.Count;$i++){if($null -ne $arguments[$i]){$arguments[$i]=$arguments[$i].psobject.BaseObject}};$a.GetType('AOLauncher.PresetService').GetMethod($method,$flags).Invoke($null,$arguments)}
$json=New-Object Web.Script.Serialization.JavaScriptSerializer
$settings=Model LauncherSettings;$account=Model AccountConfig;$account.Username='fixture';$account.Label='Fixture'
foreach($id in @(10,11)){$char=Model CharacterConfig;$char.CharacterId=$id;$char.Name="Fixture $id";$char.Selected=($id -eq 10);$account.Characters.Add($char)}
$settings.Accounts.Add($account)
$first=Preset Create @($settings,'First team')
$account.Characters[0].Selected=$false;$account.Characters[1].Selected=$true
$second=Preset Create @($settings,'Second team')
Assert (!$settings.DefaultPresetId) 'Creating or selecting a preset does not silently make it the startup default'
$before=$json.Serialize($settings.Accounts)
Preset ApplyStartupSelection @($settings) | Out-Null
Assert (!$settings.SelectedPresetId -and $json.Serialize($settings.Accounts) -ceq $before) 'No default starts with no preset selected and preserves manual character checks'
Preset SetStartupDefault @($settings,$first,$true)
Assert ($settings.DefaultPresetId -eq $first.Id -and $json.Serialize($settings.Accounts) -ceq $before) 'Marking a startup default does not change current character selection'
Preset Apply @($settings,$second) | Out-Null
$restored=$json.Deserialize($json.Serialize($settings),$settings.GetType())
$result=Preset ApplyStartupSelection @($restored)
Assert ($restored.SelectedPresetId -eq $first.Id -and $result.SelectedCount -eq 1 -and $restored.Accounts[0].Characters[0].Selected -and !$restored.Accounts[0].Characters[1].Selected) 'A restart applies the explicit default, independently of the last selected team'
Preset SetStartupDefault @($settings,$second,$true)
Assert ($settings.DefaultPresetId -eq $second.Id) 'Choosing another default replaces the previous one'
Preset SetStartupDefault @($settings,$first,$false)
Assert ($settings.DefaultPresetId -eq $second.Id) 'Saving a nondefault preset unchecked does not clear another default'
Preset Rename @($settings,$second,'Renamed team')
Assert ($settings.DefaultPresetId -eq $second.Id) 'Renaming preserves the default through its stable ID'
Preset SetStartupDefault @($settings,$second,$false)
Preset ApplyStartupSelection @($settings) | Out-Null
Assert (!$settings.DefaultPresetId -and !$settings.SelectedPresetId) 'Unchecking the default returns to no startup preset'
Preset SetStartupDefault @($settings,$first,$true)
Preset Delete @($settings,$first)
Assert (!$settings.DefaultPresetId) 'Deleting the default clears its startup reference'
$settings.DefaultPresetId='missing';$settings.SelectedPresetId=$second.Id
Preset ApplyStartupSelection @($settings) | Out-Null
Assert (!$settings.DefaultPresetId -and !$settings.SelectedPresetId) 'Missing default does not fall back to the last-used preset'
Preset SetStartupDefault @($settings,$second,$true)
$account.Characters.Clear()
$result=Preset ApplyStartupSelection @($settings)
Assert ($result.SelectedCount -eq 0 -and $result.MissingCount -eq 1) 'Removed characters are skipped and counted without replacing preset contents'
$legacy=$json.Deserialize('{"SelectedPresetId":"last-used"}',$settings.GetType())
Assert (!$legacy.DefaultPresetId) 'Old profiles do not gain an implicit default'

foreach($language in @('en','da')){
    $a.GetType('AOLauncher.Localization.Texts').GetMethod('SetLanguage',$flags).Invoke($null,[object[]]@($language)) | Out-Null
    $a.GetType('AOLauncher.NexusStrings').GetMethod('Register',$flags).Invoke($null,@()) | Out-Null
    foreach($scale in @(1,1.5)){
        $dialog=[Activator]::CreateInstance($a.GetType('AOLauncher.PresetNameDialog'),[object[]]@('Edit team preset','Fixture',[string[]]@()))
        try {
            $dialog.UseAsStartupDefault=$true
            if($scale -gt 1){$dialog.Font=[Drawing.Font]::new($dialog.Font.FontFamily,$dialog.Font.Size*$scale);$dialog.Scale([Drawing.SizeF]::new($scale,$scale))}
            $dialog.ShowInTaskbar=$false;$dialog.StartPosition='Manual';$dialog.Location=[Drawing.Point]::new(-16000,-16000);$dialog.Show();[Windows.Forms.Application]::DoEvents()
            $box=$dialog.Controls.Find('startupDefaultPreset',$true)[0]
            Assert ($box.Visible -and $box.Checked -and $box.Parent.ClientRectangle.Contains($box.Bounds)) 'Default checkbox is visible and fits in both languages at both scales'
            $box.Checked=$false
            Assert (!$dialog.UseAsStartupDefault) 'Checkbox exposes the edited default choice'
            $dialog.DialogResult='Cancel';$dialog.Close()
            Assert ($settings.DefaultPresetId -eq $second.Id) 'Cancelling the draft editor does not modify settings'
        }finally{$dialog.Dispose()}
    }
}
Write-Output "PASS: $checks startup-default persistence, replacement, removal, migration and bilingual dialog checks. No real profile or game accessed."
