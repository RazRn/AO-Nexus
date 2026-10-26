param([string]$AssemblyPath=(Join-Path $PSScriptRoot '../src/Launcher/bin/Release/AONexus.exe'))
$ErrorActionPreference='Stop'
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$method=$a.GetType('AOLauncher.ClientMonitor').GetMethod('ReadPulse',[Reflection.BindingFlags]'Static,NonPublic')
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
using System.Threading;
public static class SnapshotRaceTest {
    public static int Run(MethodInfo read, string path) {
        File.WriteAllText(path, "{\"Updates\":0}");
        Exception failure = null;
        var ready = new ManualResetEvent(false);
        var writer = new Thread(() => {
            ready.Set();
            try {
                for (int i = 1; i <= 1000; i++) {
                    File.WriteAllText(path + ".tmp", "{\"Updates\":" + i + "}");
                    File.Replace(path + ".tmp", path, null);
                    Thread.Sleep(1);
                }
            } catch (Exception e) { failure = e; }
        });
        writer.Start(); ready.WaitOne();
        int reads = 0; long last = -1;
        try {
            do {
                var pulse = read.Invoke(null, new object[] { path });
                long value = (long)pulse.GetType().GetProperty("Updates").GetValue(pulse, null);
                if (value < last) throw new Exception("Snapshot moved backwards");
                last = value; reads++;
                Thread.Sleep(1);
            } while (writer.IsAlive);
        } finally { writer.Join(); ready.Dispose(); }
        if (failure != null) throw new Exception("Atomic writer failed", failure);
        var final = read.Invoke(null, new object[] { path });
        if ((long)final.GetType().GetProperty("Updates").GetValue(final, null) != 1000)
            throw new Exception("Reader did not see final publication");
        File.WriteAllText(path, new string(' ', 8192));
        if (read.Invoke(null, new object[] { path }) != null) throw new Exception("Oversize snapshot accepted");
        return reads;
    }
}
'@
$path=Join-Path ([IO.Path]::GetTempPath()) ('NexusSnapshotRace-'+[Guid]::NewGuid().ToString('N')+'.json')
try {
 $reads=[SnapshotRaceTest]::Run($method,$path)
 if($reads -lt 100){throw 'Insufficient concurrent reads'}
 Write-Output "PASS: 1000 atomic publications, $reads concurrent production-reader calls, monotonic complete snapshots and bounded size."
}finally{
 foreach($file in @($path,$path+'.tmp')){if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file -Force}}
}
