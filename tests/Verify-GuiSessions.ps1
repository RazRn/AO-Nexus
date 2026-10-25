param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference='Stop'
if([IntPtr]::Size -ne 4){throw 'Run x86 PowerShell -STA'}
Add-Type -AssemblyName System.Windows.Forms,System.Web.Extensions
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static';$checks=0
function Assert($ok,$message){if(!$ok){throw $message};$script:checks++}
function New-Model($name){[Activator]::CreateInstance($a.GetType('AOLauncher.'+$name),$true)}
function Static($type,$name,[object[]]$arguments){for($i=0;$i -lt $arguments.Length;$i++){if($null -ne $arguments[$i]){$arguments[$i]=$arguments[$i].psobject.BaseObject}};$a.GetType('AOLauncher.'+$type).GetMethod($name,$flags).Invoke($null,$arguments)}
Static 'Localization.Texts' 'SetLanguage' @('en')
$notice = Static 'GuiSessions' 'CaptureNotice' @('Example source',$true)
Assert ($notice.Contains('Example source') -and $notice.Contains('/quit') -and $notice.Contains('before reading')) 'Running source confirmation identifies source and orders normal logout before capture'
$notice = Static 'GuiSessions' 'CaptureNotice' @('Example source',$false)
Assert ($notice.Contains('logged out normally') -and $notice.Contains('cancel')) 'Offline source confirmation asks whether latest changes were saved and permits cancellation'
Assert (Static 'GuiSessions' 'MatchesProcess' @(11,[long]20,'C:\fixture\AnarchyOnline.exe',11,[long]20,'c:\fixture\AnarchyOnline.exe')) 'Exact PID/start/path match is accepted'
Assert (!(Static 'GuiSessions' 'MatchesProcess' @(11,[long]20,'C:\fixture\AnarchyOnline.exe',11,[long]21,'C:\fixture\AnarchyOnline.exe'))) 'Reused PID with different start time is not accepted'
Assert (!(Static 'GuiSessions' 'MatchesProcess' @(11,[long]20,'C:\fixture\AnarchyOnline.exe',11,[long]20,'C:\other\AnarchyOnline.exe'))) 'Different installation is not accepted'
Add-Type @'
using System;
public static class GuiExitFixture {
    public static int Calls;
    public static bool AlwaysRunning() { Calls++; return false; }
    public static bool AllExited() { Calls++; return Calls >= 3; }
}
'@
$never=[Delegate]::CreateDelegate([Func[bool]],[GuiExitFixture].GetMethod('AlwaysRunning'))
$task=Static 'GuiSessions' 'WaitForExitAsync' @($never,[TimeSpan]::FromMilliseconds(250),[Threading.CancellationToken]::None)
Assert (!$task.GetAwaiter().GetResult()) 'An acknowledgement or changed window title is not used as proof of exit'
[GuiExitFixture]::Calls=0
$eventual=[Delegate]::CreateDelegate([Func[bool]],[GuiExitFixture].GetMethod('AllExited'))
$task=Static 'GuiSessions' 'WaitForExitAsync' @($eventual,[TimeSpan]::FromSeconds(2),[Threading.CancellationToken]::None)
Assert ($task.GetAwaiter().GetResult() -and [GuiExitFixture]::Calls -ge 3) 'Wait continues until all actual process handles have exited'
$cts=[Threading.CancellationTokenSource]::new();$cts.Cancel();$cancelled=$false
try{$task=Static 'GuiSessions' 'WaitForExitAsync' @($never,[TimeSpan]::FromSeconds(5),$cts.Token);$task.GetAwaiter().GetResult()|Out-Null}catch{$cancelled=$true}finally{$cts.Dispose()}
Assert $cancelled 'Cancellation stops the waiting stage'
$root=Join-Path ([IO.Path]::GetTempPath()) ('NexusGuiSessionTest-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
try {
    [IO.File]::WriteAllBytes((Join-Path $root 'AnarchyOnline.exe'),[byte[]]@())
    $settings=New-Model 'LauncherSettings';$settings.DefaultGuiTemplateId='fixture-template'
    $account=New-Model 'AccountConfig';$account.Username='fixture';$account.Label='Fixture';$account.Server='2.24.96.30';$account.Port=7500;$account.GamePath=$root
    $first=New-Model 'CharacterConfig';$first.CharacterId=1;$first.Name='One';$first.Selected=$false
    $second=New-Model 'CharacterConfig';$second.CharacterId=2;$second.Name='Two';$second.Selected=$true
    $account.Characters.Add($first);$account.Characters.Add($second);$settings.Accounts.Add($account)
    $choice=New-Model 'GuiCharacterChoice';$choice.Account=$account;$choice.Character=$first;$choice.CharacterPath=Join-Path $root 'other/Char1'
    $choiceList=[Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($choice.GetType()));$choiceList.Add($choice)
    $account.PrefsPath=Join-Path $root 'someone-else'
    $inspection=Static 'GuiSessions' 'Inspect' @($settings,$choiceList,$false)
    Assert ($inspection.GetType().GetField('BlockReason',[Reflection.BindingFlags]'NonPublic,Instance').GetValue($inspection)) 'Mismatched Prefs account ownership blocks before any process or file operation'
    $account.PrefsPath=Join-Path $root 'fixture'
    $inspection=Static 'GuiSessions' 'Inspect' @($settings,$choiceList,$false)
    Assert ($inspection.GetType().GetField('BlockReason',[Reflection.BindingFlags]'NonPublic,Instance').GetValue($inspection)) 'Changed character target path blocks before writes'
    $choice.CharacterPath=Join-Path $account.PrefsPath 'Char1'
    $candidate=New-Model 'PrefsCandidate';$candidate.GetType().GetProperty('PrefsPath').SetValue($candidate,$account.PrefsPath);$candidate.GetType().GetProperty('GamePath').SetValue($candidate,$root)
    $candidates=[Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($candidate.GetType()));$candidates.Add($candidate)
    Assert (Static 'GuiSessions' 'VerifiedScope' @($choiceList,$candidates)) 'Verified physical Prefs and game mapping permits narrow scope'
    $candidate.GetType().GetProperty('GamePath').SetValue($candidate,(Join-Path $root 'different-game'))
    Assert (!(Static 'GuiSessions' 'VerifiedScope' @($choiceList,$candidates))) 'Manually mismatched Prefs/game mapping cannot use narrow client scope'
    $client=New-Model 'PresetCamping+Client';$client.AccountIdentity='id:'+$account.Id;$client.Username=$account.Username;$client.CharacterId=1;$client.GamePath=$root;$client.Server=$account.Server;$client.Port=$account.Port
    foreach($verified in @($true,$false)) {
        Assert (Static 'GuiSessions' 'AffectsClient' @($choiceList,$false,$verified,$client,$account,$first)) 'Selected character is included regardless of installation-log availability'
        $client.CharacterId=2
        Assert (!(Static 'GuiSessions' 'AffectsClient' @($choiceList,$false,$verified,$client,$account,$second))) 'Another character on the same account is not relogged for character-only changes'
        Assert (Static 'GuiSessions' 'AffectsClient' @($choiceList,$true,$verified,$client,$account,$second)) 'Shared skin operations still include other characters in the installation'
        $client.CharacterId=1
    }
    $alias=New-Model 'AccountConfig';$alias.Username=$account.Username;$alias.PrefsPath=$account.PrefsPath;$alias.GamePath=Join-Path $root 'alias-installation'
    $client.AccountIdentity='id:'+$alias.Id;$client.GamePath=$alias.GamePath
    Assert (Static 'GuiSessions' 'AffectsClient' @($choiceList,$false,$true,$client,$alias,$first)) 'An alias using the same physical character prefs remains affected'
    $alias.PrefsPath=Join-Path $root 'different-prefs'
    Assert (Static 'GuiSessions' 'AffectsClient' @($choiceList,$false,$false,$client,$alias,$first)) 'Unverified mapping conservatively includes the same username and character ID across installations'
    $client.Username='other-account'
    Assert (!(Static 'GuiSessions' 'AffectsClient' @($choiceList,$false,$false,$client,$alias,$first))) 'A coincident character ID on a different account does not expand the relog'
    Assert (!(Static 'GuiSessions' 'AffectsClient' @($choiceList,$true,$true,$client,$alias,$first))) 'Verified skin scope excludes unrelated installations'
    $client.AccountIdentity='id:'+$account.Id;$client.Username=$account.Username;$client.GamePath=$root
    $listType=[Collections.Generic.List``1].MakeGenericType($client.GetType());$clients=[Activator]::CreateInstance($listType);$clients.Add($client)
    $passwords=[Collections.Generic.Dictionary[string,string]]::new();$passwords.Add($account.Id,'synthetic-password')
    $json=[Web.Script.Serialization.JavaScriptSerializer]::new();$before=$json.Serialize($settings)
    $request=Static 'MainForm' 'CreateGuiRelogRequest' @($settings,$clients,$passwords)
    Assert ($request.Characters.Count -eq 1 -and $request.Characters[0].CharacterId -eq 1) 'Relog starts the exact confirmed clients, not checked dashboard chars'
    Assert ($json.Serialize($settings) -ceq $before) 'Relog request preserves main selection, presets and account settings'
    Assert ($request.Characters[0].AccountId -eq $account.Id -and $request.Characters[0].Server -eq $account.Server) 'Relog stays bound to account and endpoint'
    $client.Server='127.0.0.1'
    $wrong=Static 'MainForm' 'CreateGuiRelogRequest' @($settings,$clients,$passwords)
    Assert ($wrong.Characters.Count -eq 0) 'Changed endpoint cannot relaunch the old identity'
    $arguments=[object[]]@($settings,$account,[int[]]@(1,2,3),$null)
    Assert ($a.GetType('AOLauncher.MainForm').GetMethod('TryMergeImportedCharacters',$flags).Invoke($null,$arguments)) 'Newly imported character is added'
    Assert ($account.Characters[2].PendingGuiTemplateId -eq 'fixture-template') 'Newly added character queues the chosen GUI default'
    Assert (!$first.PendingGuiTemplateId -and !$second.PendingGuiTemplateId) 'Existing characters are not retroactively assigned a new GUI default'
    $clone=$json.Deserialize($json.Serialize($settings),$settings.GetType())
    Assert ($clone.DefaultGuiTemplateId -eq 'fixture-template' -and $clone.Accounts[0].Characters[2].PendingGuiTemplateId -eq 'fixture-template') 'GUI default and pending application survive restart'
    foreach($entry in $request.Characters){$entry.Password=$null};$passwords.Clear()
}finally{
    $resolved=[IO.Path]::GetFullPath($root);$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or !([IO.Path]::GetFileName($resolved)).StartsWith('NexusGuiSessionTest-')){throw 'Unsafe test cleanup'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Output "PASS: $checks GUI relog identity, waiting, cancellation, selection and pending-default checks. No game clients or real profiles touched."
