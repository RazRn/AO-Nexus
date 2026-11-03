# Dot-sourced by Capture-GuideScreenshots. Only synthetic models and an isolated
# clock store are used; no client commands, real profile or plugin pack is loaded.
Add-Type -AssemblyName System.Web.Extensions
$guideJson = New-Object Web.Script.Serialization.JavaScriptSerializer
function Guide-Model([string]$name, $data) {
    $guideJson.Deserialize($guideJson.Serialize($data), $assembly.GetType($name, $true))
}
function Set-GuideField($object, [string]$name, $value) {
    $object.GetType().GetField($name, $flags).SetValue($object, $value)
}
function Guide-Children($control) {
    foreach ($child in $control.Controls) { $child; Guide-Children $child }
}
$guideSettings = New-Model 'LauncherSettings'
$guideSettings.ExperimentalNearbyCombat = $true
$guideSettings.ExperimentalTeamAutomation = $true
$guideSettings.ControlPanel = Guide-Model 'AOLauncher.ControlPanelPreferences' @{
    LivePreviews=$false; FormationMode='follow'; FormationSpacing=2
    Sections=@(@{Id='team';Height=420},@{Id='damage';Height=280;Collapsed=$true},@{Id='timers';Collapsed=$true;Height=300})
}
$guideAccount = New-Model 'AccountConfig'
$guideAccount.Id='guide-account'; $guideAccount.Label='Example team'
$guideAccount.Username='example-account'; $guideAccount.PrefsPath='C:\ExamplePrefs\AO\Prefs\example-account'
$guideAccount.Server='192.0.2.10'; $guideAccount.Port=7500
$guideSettings.Accounts.Add($guideAccount)
$guideRows = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($assembly.GetType('AOLauncher.ClientObservation')))
foreach ($i in 0..2) {
    $id=101+$i; $name=@('Aster','Orion','Vesper')[$i]; $profession=@('Enforcer','Doctor','Engineer')[$i]
    $character=New-Model 'CharacterConfig'; $character.Name=$name; $character.CharacterId=$id
    $guideAccount.Characters.Add($character)
    $row=New-Dialog 'ClientObservation' ([object[]]@())
    Set-GuideField $row 'Name' $name
    Set-GuideField $row 'Pid' ([int](900001+$i))
    Set-GuideField $row 'StartTicks' ([long]$id)
    Set-GuideField $row 'Measured' $true
    Set-GuideField $row 'Condition' ([Enum]::Parse($assembly.GetType('AOLauncher.ClientCondition'),'Healthy'))
    $client=Guide-Model 'AOLauncher.PresetCamping+Client' @{ProcessId=(900001+$i);StartTimeUtcTicks=$id;CharacterId=$id;AccountIdentity='id:guide-account';Server='192.0.2.10';Port=7500}
    Set-GuideField $row 'Client' $client
    $actor=@{CharacterId=$id;Name=$name;Profession=$profession;Damage=@(18400,9200,24600)[$i];NanoDamage=@(1200,1300,0)[$i];PetDamage=0;Healing=@(200,14300,0)[$i];Xp=4200;ExperienceEvents=3}
    if($i -eq 2){$actor.PetDamage=19000;$actor.Pets=@(@{CharacterId=201;Name='Clockwork';Active=$true;Damage=13000},@{CharacterId=202;Name='Scout';Active=$true;Damage=6000})}
    $pulse=Guide-Model 'AOLauncher.ClientPulse' @{Pid=(900001+$i);StartTicks=$id;Active=$true;Version=2;Profession=$profession;Health=@(4200,2600,2800)[$i];MaxHealth=@(5000,2600,3000)[$i];Nano=@(1600,2800,1900)[$i];MaxNano=3200;IsTeamLeader=($i -eq 0);Attacking=($i -eq 0);FightingTarget='Example creature';AggroCount=$(if($i -eq 0){2}else{0});AggroNames=@('Example creature','Example guard');CombatMeter=@{Status='Ready';Source='Nexus';Epoch='guide';ElapsedSeconds=120;Actors=@($actor)}}
    $pulse.Movement=Guide-Model 'NexusTelemetry.MovementSnapshot' @{CharacterId=$id;Playfield=655;ZoneName='Andromeda';Adapter='AOSP';Enabled=$true;SendMovement=($i -eq 0);Channel=1;X=(100+$i);Y=10;Z=100}
    Set-GuideField $row 'Pulse' $pulse
    Set-GuideField $row 'Sample' (Guide-Model 'AOLauncher.ClientSample' @{CpuPercent=2.5;FreeClientBytes=2400000000;ReplyMs=16;ReplyAgeMs=100;PulseAgeMs=100})
    $guideRows.Add($row)
}
$guideRead = [scriptblock]{
    $now=[Diagnostics.Stopwatch]::GetTimestamp()
    foreach($r in $guideRows){$p=Field $r 'Pulse';$p.PublishedQpc=$now;$p.LastUpdateQpc=$now;$p.VitalsQpc=$now;$p.TeamStateQpc=$now;$p.AggroQpc=$now;$p.CombatMeter.ObservedQpc=$now;$p.Movement.Qpc=$now;$sample=Field $r 'Sample';$sample.Utc=[DateTime]::UtcNow;$sample.MemoryMeasuredQpc=$now}
    return ,$guideRows
} -as ([Func``1].MakeGenericType($guideRows.GetType()))
$guideConfig = [scriptblock]{return $guideSettings} -as ([Func``1].MakeGenericType($guideSettings.GetType()))
$guideClockDirectory=Join-Path $OutputDirectory 'fixture-clocks'
[IO.Directory]::CreateDirectory($guideClockDirectory)|Out-Null
$guideScope=$assembly.GetType('NexusPlayTools.ToolFiles').GetMethod('Scope',$staticFlags).Invoke($null,@($guideAccount.PrefsPath))
[IO.File]::WriteAllText((Join-Path $guideClockDirectory 'timers.json'),$guideJson.Serialize(@{Timers=@(@{Id='00000000000000000000000000000001';Name='Example boss';Scope=$guideScope;Seconds=3600;WarningSeconds=300;DueUtcTicks=[DateTime]::UtcNow.AddMinutes(42).Ticks;Kind=0;Revision=1})}))
$guideClockStore=[Activator]::CreateInstance($assembly.GetType('NexusPlayTools.ClockStore'),$flags,$null,[object[]]@([string]$guideClockDirectory),$null)
if($guideClockStore.Read().Timers.Count -ne 1){throw 'Timer fixture must contain one valid example timer'}
$guidePanel=New-Dialog 'TeamWindow' ([object[]]@($guideRead,$guideConfig,$null,$null,$null,$null,$null,$null,$null,$null))
try {
    # Prevent even harmless IPC polling of synthetic process IDs.
    Set-GuideField $guidePanel 'nextFormationPoll' ([DateTime]::MaxValue)
    foreach($control in (Guide-Children $guidePanel)) {
        if($control.GetType().Name -eq 'TimersPanel'){Set-GuideField $control 'store' $guideClockStore}
    }
    $guidePanel.ClientSize=[Drawing.Size]::new(1280,850)
    Show-Fixture $guidePanel
    Save-GuideScreenshot $guidePanel 'control-panel.png'
    $prefs=$guideSettings.ControlPanel.Sections
    $damage=@($prefs|Where-Object Id -eq 'damage')[0];$team=@($prefs|Where-Object Id -eq 'team')[0];$timers=@($prefs|Where-Object Id -eq 'timers')[0]
    $team.Collapsed=$true;$damage.Collapsed=$false;$damage.Height=420
    $ordered=[Activator]::CreateInstance($prefs.GetType());$ordered.Add($damage);$ordered.Add($team);$ordered.Add($timers)
    (Field $guidePanel 'sections').GetType().GetMethod('Restore',$flags).Invoke((Field $guidePanel 'sections'),[object[]]@($ordered,[int]0))|Out-Null
    Save-GuideScreenshot $guidePanel 'damage-dumper.png'
} finally {$guidePanel.Dispose()}
$guideTimers=New-Dialog 'TimersWindow' ([object[]]@($guideSettings,$guideClockStore))
try {$guideTimers.ClientSize=[Drawing.Size]::new(880,650);Show-Fixture $guideTimers;Save-GuideScreenshot $guideTimers 'timers.png'}finally{$guideTimers.Dispose()}
$guidePack=New-Dialog 'AOSharpPack' ([object[]]@())
Set-GuideField $guidePack 'Root' 'C:\ExamplePlugins\AOSharp'
Set-GuideField $guidePack 'Description' 'Example local installation'
foreach($entry in @(@('Manager.Sync','Manager · Sync','Managers'),@('Handler.Enf','Enf handler','Profession handlers'),@('Handler.Doc','Doc handler','Profession handlers'),@('Handler.Engi','Engi handler','Profession handlers'))){
    $plugin=New-Model 'PluginInfo'
    Set-GuideField $plugin 'Id' ('pack:'+$entry[0]);Set-GuideField $plugin 'Name' ([string]$entry[1]);Set-GuideField $plugin 'Group' ([string]$entry[2]);Set-GuideField $plugin 'Suite' 'AOSP';Set-GuideField $plugin 'Path' ('C:\ExamplePlugins\AOSharp\'+$entry[0]+'.dll')
    $guideCatalog=$guidePack.GetType().GetField('Plugins',$flags).GetValue($guidePack);$guideCatalog.Add($plugin)
}
$guideSettings.SharedPlugins.Add('pack:Manager.Sync')
$assembly.GetType('AOLauncher.PluginSets').GetMethod('EnsureStarterSets',$staticFlags).Invoke($null,[object[]]@($guideSettings,$guidePack))|Out-Null
$guidePlugins=New-Dialog 'PluginsDialog' ([object[]]@($guideSettings,$guidePack,$null))
try {
    $guidePlugins.ClientSize=[Drawing.Size]::new(1280,740)
    Show-Fixture $guidePlugins
    Save-GuideScreenshot $guidePlugins 'plugins.png'
}finally{$guidePlugins.Dispose()}
