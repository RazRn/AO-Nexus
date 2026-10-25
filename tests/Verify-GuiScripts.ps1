param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Web.Extensions
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$type=$assembly.GetType('AOLauncher.GuiScripts',$true)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static'
$json=New-Object Web.Script.Serialization.JavaScriptSerializer
$checks=0
function Assert($condition,$message){if(!$condition){throw $message};$script:checks++}
function Call($name,[object[]]$values=@()){for($i=0;$i -lt $values.Length;$i++){if($null -ne $values[$i]){$values[$i]=$values[$i].psobject.BaseObject}};$type.GetMethod($name,$flags).Invoke($null,$values)}
function Reject($action,$message){$failed=$false;try{&$action|Out-Null}catch{$failed=$true};Assert $failed $message}
function Write-Text($path,$text){[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))|Out-Null;[IO.File]::WriteAllText($path,$text,[Text.UTF8Encoding]::new($false))}
function Names($prefs){@(Call List @($prefs) | ForEach-Object {$_.Name})}
function Strings([string[]]$values){,[string[]]$values}
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('AONexus-GuiScripts-tests-'+[guid]::NewGuid().ToString('N'))
$backups=Join-Path $testRoot 'ScriptBackups'
$prefsA=Join-Path $testRoot 'InstA/Prefs';$prefsB=Join-Path $testRoot 'InstB/Prefs';$prefsC=Join-Path $testRoot 'InstC/Prefs'
$scriptsA=Join-Path $testRoot 'InstA/scripts';$scriptsB=Join-Path $testRoot 'InstB/scripts'
try {
    foreach($prefs in @($prefsA,$prefsB,$prefsC)){Write-Text (Join-Path $prefs 'Prefs.xml') '<Root/>'}
    Assert ((Names $prefsA).Count -eq 0) 'A missing scripts folder lists no scripts'
    Assert ((Call ScriptsRoot @($prefsA)) -eq [IO.Path]::GetFullPath($scriptsA)) 'Scripts live beside the installation Prefs folder'
    foreach($name in @('rkunique','buff-1','heal.txt','A_b.2')){Assert (Call ValidName @($name)) "Valid script name $name"}
    foreach($name in @('','../x','..','x y','con','COM1.txt','run.bat','tool.EXE','.hidden','end.','a/b','a\b','x:y',('a'*65))){Assert (!(Call ValidName @($name))) "Rejected script name '$name'"}
    Write-Text (Join-Path $scriptsA 'rkunique') "/selectself`r`n/delay 300`r`n/say .tp 1 2 3"
    Write-Text (Join-Path $scriptsA 'assist') '/assist SOURCE'
    Write-Text (Join-Path $scriptsA 'run.bat') 'echo no'
    [IO.File]::WriteAllBytes((Join-Path $scriptsA 'binary'),[byte[]](65,0,66))
    [IO.File]::WriteAllBytes((Join-Path $scriptsA 'program'),[byte[]](77,90,1,2))
    Write-Text (Join-Path $scriptsA 'huge') ('x'*(64*1024+1))
    Write-Text (Join-Path $scriptsA 'old.nexus-0123.tmp') 'temp'
    [IO.Directory]::CreateDirectory((Join-Path $scriptsA 'text'))|Out-Null
    $listed=Names $prefsA
    Assert ($listed.Count -eq 2 -and $listed -contains 'rkunique' -and $listed -contains 'assist') 'Only plain text scripts are listed'
    Assert ([Text.Encoding]::ASCII.GetString((Call ReadScript @($prefsA,'assist'))) -eq '/assist SOURCE') 'Script content is read'
    Reject {Call ReadScript @($prefsA,'binary')} 'Binary content is rejected'
    Reject {Call ReadScript @($prefsA,'../InstA/Prefs/Prefs.xml')} 'Script reads cannot leave the scripts folder'

    $copy=Call Copy @($prefsA,(Strings 'rkunique','assist'),$prefsB,$backups)
    Assert ($copy.State -eq 'Applied' -and @($copy.Changes).Count -eq 2 -and @($copy.Changes | Where-Object Existed).Count -eq 0) 'Copy creates new scripts with a backup record'
    Assert ((Get-FileHash (Join-Path $scriptsA 'rkunique')).Hash -eq (Get-FileHash (Join-Path $scriptsB 'rkunique')).Hash) 'Copied scripts are byte-identical'
    $same=Call Copy @($prefsA,(Strings 'rkunique','assist'),$prefsB,$backups)
    Assert ($same.State -eq 'Unchanged' -and !$same.Id) 'Copying identical scripts changes nothing'
    Write-Text (Join-Path $scriptsB 'assist') '/assist TARGET'
    Write-Text (Join-Path $scriptsB 'own') '/say keep me'
    $overwrite=Call Copy @($prefsA,(Strings 'rkunique','assist'),$prefsB,$backups)
    Assert (@($overwrite.Changes).Count -eq 1 -and $overwrite.Changes[0].Name -eq 'assist' -and $overwrite.Changes[0].Existed) 'Only the differing script is replaced'
    Assert ((Get-Content -Raw (Join-Path $scriptsB 'own')) -eq '/say keep me') 'Other target scripts are kept'
    $countBefore=@(Call ListBackups @($prefsB,$backups)).Count
    Call Restore @($prefsB,$overwrite.Id,$backups)|Out-Null
    Assert ((Get-Content -Raw (Join-Path $scriptsB 'assist')) -eq '/assist TARGET') 'Restore returns the replaced script'
    Assert (Test-Path (Join-Path $scriptsB 'rkunique')) 'Restore leaves scripts the backup did not change'
    Assert (@(Call ListBackups @($prefsB,$backups)).Count -eq $countBefore+1) 'Restore backs up the current scripts first'
    Assert (@(Call ListBackups @($prefsB,$backups) | Where-Object State -eq 'Restored').Count -eq 1) 'Restored backups are marked'
    Call Restore @($prefsB,$copy.Id,$backups)|Out-Null
    Assert (!(Test-Path (Join-Path $scriptsB 'rkunique')) -and (Test-Path (Join-Path $scriptsB 'own'))) 'Restoring a copy removes only scripts it created'
    Reject {Call Restore @($prefsA,$overwrite.Id,$backups)} 'A backup cannot be restored to another installation'

    $delete=Call Delete @($prefsA,(Strings 'assist'),$backups)
    Assert (!(Test-Path (Join-Path $scriptsA 'assist')) -and $delete.State -eq 'Applied') 'Delete removes the script after backing it up'
    Call Restore @($prefsA,$delete.Id,$backups)|Out-Null
    Assert ((Get-Content -Raw (Join-Path $scriptsA 'assist')) -eq '/assist SOURCE') 'A deleted script can be restored'
    Reject {Call Delete @($prefsA,(Strings 'missing'),$backups)} 'Deleting a missing script is rejected'

    $incoming=Join-Path $testRoot 'Incoming'
    Write-Text (Join-Path $incoming 'heal') '/macro heal /nano heal'
    Write-Text (Join-Path $incoming 'evil.cmd') 'del *'
    [IO.File]::WriteAllBytes((Join-Path $incoming 'image'),[byte[]](137,80,78,71,0,1))
    $import=Call Import @($prefsA,(Join-Path $incoming 'heal'),$backups)
    Assert ($import.State -eq 'Applied' -and (Test-Path (Join-Path $scriptsA 'heal'))) 'A text script can be imported'
    Reject {Call Import @($prefsA,(Join-Path $incoming 'evil.cmd'),$backups)} 'Program files cannot be imported'
    Reject {Call Import @($prefsA,(Join-Path $incoming 'image'),$backups)} 'Binary files cannot be imported'
    Reject {Call Copy @($prefsA,(Strings '..\Prefs\Prefs.xml'),$prefsB,$backups)} 'Copy rejects path traversal'

    Write-Text (Join-Path $scriptsB 'assist') '/assist OTHER';Write-Text (Join-Path $scriptsB 'heal') '/say OTHER'
    $beforeAssist=Get-Content -Raw (Join-Path $scriptsB 'assist');$beforeHeal=Get-Content -Raw (Join-Path $scriptsB 'heal')
    $type.GetField('BeforeWrite',$flags).SetValue($null,[Action[int]]{param($index) if($index -eq 1){throw 'Synthetic write failure'}})
    Reject {Call Copy @($prefsA,(Strings 'assist','heal'),$prefsB,$backups)} 'A failed copy reports failure'
    $type.GetField('BeforeWrite',$flags).SetValue($null,$null)
    Assert ((Get-Content -Raw (Join-Path $scriptsB 'assist')) -eq $beforeAssist -and (Get-Content -Raw (Join-Path $scriptsB 'heal')) -eq $beforeHeal) 'A failed copy rolls back every script'
    Assert (@(Call ListBackups @($prefsB,$backups) | Where-Object State -eq 'Rolled back').Count -eq 1) 'The rollback is recorded'

    $tamper=Call Copy @($prefsA,(Strings 'assist'),$prefsB,$backups)
    $folder=Join-Path $backups $tamper.Id;$manifestPath=Join-Path $folder 'manifest.json';$manifest=Get-Content -Raw $manifestPath
    $modified=$json.DeserializeObject($manifest);$modified.Changes[0].Name='..\escape';Write-Text $manifestPath ($json.Serialize($modified))
    Reject {Call Restore @($prefsB,$tamper.Id,$backups)} 'Manifest traversal is rejected'
    Write-Text $manifestPath $manifest
    Write-Text (Join-Path $folder 'Files/assist') 'tampered'
    Reject {Call Restore @($prefsB,$tamper.Id,$backups)} 'Backup hash tampering is rejected'
    Reject {Call Restore @($prefsB,'../outside',$backups)} 'Backup IDs cannot escape the backup folder'

    for($round=0;$round -lt 14;$round++){Write-Text (Join-Path $scriptsA 'assist') ("/assist round $round");Call Copy @($prefsA,(Strings 'assist'),$prefsB,$backups)|Out-Null}
    Assert (@(Call ListBackups @($prefsB,$backups) | Where-Object {$_.State -eq 'Applied' -or $_.State -eq 'Restored' -or $_.State -eq 'Rolled back'}).Count -le 10) 'Complete script backups are bounded to ten per installation'

    $junction=Join-Path $testRoot 'InstC/scripts'
    New-Item -ItemType Junction -Path $junction -Target $scriptsA | Out-Null
    try {
        Reject {Call List @($prefsC)} 'A linked scripts folder is rejected'
        Reject {Call Copy @($prefsA,(Strings 'assist'),$prefsC,$backups)} 'Copy never writes through a linked scripts folder'
        Assert ((Get-Content -Raw (Join-Path $scriptsA 'assist')) -eq '/assist round 13') 'The junction target is untouched'
    } finally {[IO.Directory]::Delete($junction,$false)}
    Write-Output "PASS: $checks game script listing, copy, import, delete, backup, restore, rollback, integrity, traversal, bounds and junction checks. Synthetic files only."
} finally {
    $type.GetField('BeforeWrite',$flags).SetValue($null,$null)
    $resolved=[IO.Path]::GetFullPath($testRoot)
    if(!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'AONexus-GuiScripts-tests-*'){throw 'Unsafe cleanup path'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
