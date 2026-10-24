param(
    [string]$Compiler = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
)
$ErrorActionPreference = 'Stop'
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('AOQuickLauncher-BackendTest-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
$sourcePath = Join-Path $testDirectory 'BackendRunnerTest.cs'
$exePath = Join-Path $testDirectory 'BackendRunnerTest.exe'
$source = @'
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AOLauncher;

internal static class BackendRunnerTest
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int cb;
        public string reserved, desktop, title;
        public int x, y, width, height, countX, countY, fill, flags;
        public short show, reservedSize;
        public IntPtr reservedBytes, stdin, stdout, stderr;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo { public IntPtr process, thread; public int processId, threadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string app, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory,
        ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int kind);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--child") { Thread.Sleep(6000); return 0; }
            if (args.Length > 0 && args[0].StartsWith("--backend", StringComparison.Ordinal))
            {
                Console.InputEncoding = new UTF8Encoding(false);
                string line = Console.ReadLine();
                if (line != "{\"message\":\"\u00e6\u00f8\u00e5\"}") return 40;
                int childId = SpawnChild();
                Console.WriteLine("CHILD:" + childId);
                Console.Error.WriteLine("backend-stderr");
                if (args[0] == "--backend-wait") Thread.Sleep(6000);
                return 0;
            }
            Tests().GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    static int SpawnChild()
    {
        string executable = Process.GetCurrentProcess().MainModule.FileName;
        var startup = new StartupInfo { cb = Marshal.SizeOf(typeof(StartupInfo)), flags = 0x100,
            stdin = GetStdHandle(-10), stdout = GetStdHandle(-11), stderr = GetStdHandle(-12) };
        ProcessInfo info;
        if (!CreateProcess(executable, new StringBuilder("\"" + executable + "\" --child"),
            IntPtr.Zero, IntPtr.Zero, true, 0x08000000, IntPtr.Zero, null, ref startup, out info))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        CloseHandle(info.thread);
        CloseHandle(info.process);
        return info.processId;
    }

    static ProcessStartInfo StartInfo(string arguments)
    {
        return new ProcessStartInfo { FileName = Process.GetCurrentProcess().MainModule.FileName, Arguments = arguments };
    }

    static async Task Tests()
    {
        const string payload = "{\"message\":\"\u00e6\u00f8\u00e5\"}";
        using (var runner = new BackendRunner())
        {
            var lines = new ConcurrentQueue<string>();
            int childId = 0;
            var watch = Stopwatch.StartNew();
            int result = await runner.RunAsync(StartInfo("--backend"), payload, line => {
                lines.Enqueue(line);
                if (line.StartsWith("CHILD:", StringComparison.Ordinal)) childId = int.Parse(line.Substring(6));
            }, lines.Enqueue);
            if (result != 0 || watch.ElapsedMilliseconds >= 3000 || childId <= 0)
                throw new Exception("Inherited-pipe regression failed: exit=" + result + " elapsed=" + watch.ElapsedMilliseconds);
            using (var child = Process.GetProcessById(childId))
                if (child.HasExited) throw new Exception("Regression child was not alive when the backend runner completed.");
            if (!Array.Exists(lines.ToArray(), line => line == "backend-stderr"))
                throw new Exception("Trailing stderr status was not delivered.");
            Console.WriteLine("PASS: backend returned in " + watch.ElapsedMilliseconds + " ms while its inherited-pipe child remained alive; UTF-8 stdin and stderr verified.");
        }

        using (var runner = new BackendRunner())
        {
            runner.Stop();
            int result = await runner.RunAsync(new ProcessStartInfo { FileName = "nonexistent-test-backend.exe" }, payload, null, null);
            if (result != BackendRunner.StoppedExitCode) throw new Exception("Stop-before-start did not return cancellation.");
            Console.WriteLine("PASS: stop before RunAsync starts no process.");
        }

        using (var runner = new BackendRunner())
        using (var childSeen = new ManualResetEventSlim())
        {
            int childId = 0;
            Task<int> task = runner.RunAsync(StartInfo("--backend-wait"), payload, line => {
                if (line.StartsWith("CHILD:", StringComparison.Ordinal)) { childId = int.Parse(line.Substring(6)); childSeen.Set(); }
            }, null);
            if (!await Task.Run(() => childSeen.Wait(3000))) throw new Exception("Stop regression backend never started.");
            Parallel.For(0, 20, index => runner.Stop());
            if (await Task.WhenAny(task, Task.Delay(3000)) != task || await task != BackendRunner.StoppedExitCode)
                throw new Exception("Stop failed to complete the owned backend promptly.");
            using (var child = Process.GetProcessById(childId))
                if (child.HasExited) throw new Exception("Stop unexpectedly terminated a descendant.");
            Console.WriteLine("PASS: concurrent Stop calls terminate only the backend; its self-expiring child stays alive.");
        }
        Console.WriteLine("Only inert test processes were started. No game, user settings or account data were accessed.");
    }
}
'@
[IO.File]::WriteAllText($sourcePath, $source, [Text.UTF8Encoding]::new($false))
$framework = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
& $Compiler /nologo /langversion:7.3 /target:exe /platform:x86 /nostdlib+ "/out:$exePath" "/reference:$framework\mscorlib.dll" "/reference:$framework\System.dll" "/reference:$framework\System.Core.dll" (Join-Path $PSScriptRoot '..\src\Launcher\BackendRunner.cs') (Join-Path $PSScriptRoot '..\src\Common\Texts.cs') $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'BackendRunner regression harness compilation failed.' }
& $exePath
if ($LASTEXITCODE -ne 0) { throw 'BackendRunner regression failed.' }
"Test artifacts: $testDirectory (test children expire after six seconds)."
