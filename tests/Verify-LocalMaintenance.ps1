param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../dist/AONexus.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Web.Extensions
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance';$checks=0
function Assert($ok,$message){if(!$ok){throw $message};$script:checks++}
function Call($type,$method,[object[]]$arguments){for($i=0;$i -lt $arguments.Count;$i++){if($null -ne $arguments[$i]){$arguments[$i]=$arguments[$i].psobject.BaseObject}};$a.GetType('AOLauncher.'+$type).GetMethod($method,$flags).Invoke($null,$arguments)}
$root=Join-Path ([IO.Path]::GetTempPath()) ('Nexus-maintenance-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
try {
    $settings=[Activator]::CreateInstance($a.GetType('AOLauncher.LauncherSettings'),$true)
    Assert ($settings.MaxLogSizeMb -eq 5) 'Default log limit is 5 MB'
    foreach($invalid in @(-1,0,101,[int]::MaxValue)){Assert ((Call LogRetention NormalizeMb @($invalid)) -eq 5) 'Invalid persisted limits normalize safely'}
    $json=New-Object Web.Script.Serialization.JavaScriptSerializer
    Assert ($json.Deserialize('{}',$settings.GetType()).MaxLogSizeMb -eq 5) 'Old profiles receive the default'
    $settings.MaxLogSizeMb=2
    Assert ($json.Deserialize($json.Serialize($settings),$settings.GetType()).MaxLogSizeMb -eq 2) 'Custom limit survives serialization'
    $main=[Activator]::CreateInstance($a.GetType('AOLauncher.MainForm'),[object[]]@($true))
    try {
        $main.GetType().GetField('settings',$flags).GetValue($main).MaxLogSizeMb=1
        $append=$main.GetType().GetMethod('AppendLog',$flags)
        for($i=0;$i -lt 130;$i++){$append.Invoke($main,[object[]]@(('x'*10000)+" entry-$i")) | Out-Null}
        $log=$main.GetType().GetField('activityLog',$flags).GetValue($main).ToString()
        Assert ([Text.Encoding]::UTF8.GetByteCount($log) -le 1MB -and $log.Contains('entry-129') -and !$log.Contains(' entry-0')) 'Actual activity log drops oldest entries and preserves the newest within configured cap'
    } finally {$main.Dispose()}
    $inputText=('æ😀 old entry'+"`r`n")*1000+'NEWEST'+"`r`n"
    $tail=Call LogRetention Tail @($inputText,1024)
    Assert ([Text.Encoding]::UTF8.GetByteCount($tail) -le 1024 -and $tail.EndsWith("NEWEST`r`n") -and !$tail.Contains([char]0xFFFD)) 'Memory log retains newest UTF-8 entries within the byte cap'
    $tail=Call LogRetention Tail @(('😀'*10000),1001)
    Assert ([Text.Encoding]::UTF8.GetByteCount($tail) -le 1001 -and !$tail.Contains([char]0xFFFD)) 'Huge single entry is bounded without splitting Unicode'
    $file=Join-Path $root 'history.log'
    [IO.File]::WriteAllText($file,$inputText)
    Call LogRetention AppendFile @($file,"last`r`n",1024)
    Assert ((Get-Item -LiteralPath $file).Length -le 1024 -and [IO.File]::ReadAllText($file).EndsWith("last`r`n")) 'Disk log is capped after append'
    Call LogRetention AppendFile @($file,'',256)
    Assert ((Get-Item -LiteralPath $file).Length -le 256) 'Lowering limit trims immediately'
    Call LogRetention AppendFile @($file,('x'*10000),512)
    Assert ((Get-Item -LiteralPath $file).Length -le 512) 'Oversized journal message cannot exceed limit'
    $updates=Join-Path $root 'Updates';[IO.Directory]::CreateDirectory($updates)|Out-Null
    function Stage { $p=Join-Path $updates ([guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($p)|Out-Null;[IO.File]::WriteAllText((Join-Path $p 'release.zip'),'fixture');return $p }
    $complete=Stage;[IO.File]::WriteAllText((Join-Path $complete 'complete'),'')
    $old=Stage;[IO.Directory]::SetLastWriteTimeUtc($old,[DateTime]::UtcNow.AddDays(-2))
    $fresh=Stage
    $unknown=Stage;[IO.File]::WriteAllText((Join-Path $unknown 'personal.txt'),'keep');[IO.Directory]::SetLastWriteTimeUtc($unknown,[DateTime]::UtcNow.AddDays(-2))
    $busy=Stage;[IO.File]::WriteAllText((Join-Path $busy 'complete'),'');$lock=[IO.File]::Open((Join-Path $busy '.active'),'OpenOrCreate','ReadWrite','None')
    $target=Join-Path $root 'AONexus.exe'
    Copy-Item -LiteralPath $AssemblyPath -Destination $target
    $backup1=$target+'.previous-'+[guid]::NewGuid().ToString('N');Copy-Item -LiteralPath $target -Destination $backup1;[IO.File]::SetLastWriteTimeUtc($backup1,[DateTime]::UtcNow.AddDays(-2))
    $backup2=$target+'.previous-'+[guid]::NewGuid().ToString('N');Copy-Item -LiteralPath $target -Destination $backup2
    $unrelated=$target+'.previous-not-a-guid';[IO.File]::WriteAllText($unrelated,'keep')
    try {Call UpdateCleanup Sweep @($updates,$target,[DateTime]::UtcNow)}finally{$lock.Dispose()}
    Assert (!(Test-Path -LiteralPath $complete) -and !(Test-Path -LiteralPath $old)) 'Completed and stale update stages are removed'
    Assert ((Test-Path -LiteralPath $fresh) -and (Test-Path -LiteralPath $unknown) -and (Test-Path -LiteralPath $busy)) 'Recent, unknown and actively locked stages are preserved'
    Assert (!(Test-Path -LiteralPath $backup1) -and (Test-Path -LiteralPath $backup2) -and (Test-Path -LiteralPath $unrelated)) 'One actual previous Nexus executable remains; unrelated files survive'
    Assert (!(Call UpdateCleanup RemoveStage @($updates,$root))) 'Cleanup rejects a folder outside the update root'
    Assert (Call UpdateCleanup RemoveStage @($updates,$busy)) 'Completed stage is removable once helper releases it'
    $nested=Stage;[IO.Directory]::CreateDirectory((Join-Path $nested 'nested'))|Out-Null
    Assert (!(Call UpdateCleanup RemoveStage @($updates,$nested))) 'Cleanup never recursively descends into directories'
    $link=Join-Path $updates ([guid]::NewGuid().ToString('N'));$outside=Join-Path $root 'outside';[IO.Directory]::CreateDirectory($outside)|Out-Null
    New-Item -ItemType Junction -Path $link -Target $outside | Out-Null
    try {Assert (!(Call UpdateCleanup RemoveStage @($updates,$link))) 'Junctions cannot redirect deletion'}finally{[IO.Directory]::Delete($link)}
    Write-Output "PASS: $checks log retention, migration, updater cleanup and path-boundary checks. Isolated temporary files only."
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($resolved).StartsWith('Nexus-maintenance-')) {throw 'Unexpected test cleanup root'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
