param([Parameter(Mandatory=$true)][int[]]$ClientIds,[ValidateSet('D:\Games\AORebirth-Live','D:\Games\AORebirth-AOVK-Test')][string]$GamePath='D:\Games\AORebirth-Live')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Add-Type -AssemblyName System.Web.Extensions
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'logs\StartupBenchmark.exe'))
$flags=[Reflection.BindingFlags]'Static,Public,NonPublic'
$type=$assembly.GetType('AOLauncher.PresetCamping+Client',$true)
$list=[Collections.Generic.List``1].MakeGenericType($type)
$serializer=New-Object Web.Script.Serialization.JavaScriptSerializer
$records=$serializer.Deserialize([IO.File]::ReadAllText((Join-Path $env:LOCALAPPDATA 'AONexus\Sessions\ActiveClients.json')),$list)
$camp=$assembly.GetType('AOLauncher.PresetCamping').GetMethods($flags) | Where-Object {$_.Name -eq 'SendCamp' -and $_.GetParameters().Count -eq 1}
$owned=@()
foreach($clientId in $ClientIds) {
    $process=Get-Process -Id $clientId -ErrorAction SilentlyContinue
    if(!$process){continue}
    $record=@($records | Where-Object {$_.ProcessId -eq $clientId -and $_.StartTimeUtcTicks -eq $process.StartTime.ToUniversalTime().Ticks -and $_.GamePath.TrimEnd('\') -eq $GamePath})
    if($record.Count -ne 1 -or $process.Path -ne (Join-Path $GamePath 'AnarchyOnline.exe')){throw 'Benchmark client identity mismatch'}
    $owned+=$process
    $reply=$camp.Invoke($null,[object[]]@($record[0].PSObject.BaseObject))
    Write-Output ("Client $clientId : $reply")
}
foreach($process in $owned) {if(!$process.WaitForExit(30000)){throw "Client $($process.Id) did not complete normal logout; no forced termination attempted"}}
Write-Output ('Normal logout confirmed for '+$owned.Count+' benchmark clients.')
