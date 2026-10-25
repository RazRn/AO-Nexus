using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using AOLauncher;

internal static class DesktopLifetimeTests
{
    private static int checks;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        checks++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            using (var instance = new DesktopInstance(args[1]))
            {
                if (args[0] == "hold")
                {
                    Console.WriteLine(instance.IsOwner ? "READY" : "BUSY");
                    Console.Out.Flush();
                    Thread.Sleep(30000);
                    return 0;
                }
                if (instance.IsOwner) return 5;
                if (args[0] == "show") instance.RequestShow();
                return 0;
            }
        }
        try
        {
            string name = @"Local\AONexus.Desktop.Test." + Guid.NewGuid().ToString("N");
            using (var owner = new DesktopInstance(name))
            using (var form = new Form())
            {
                Check(owner.IsOwner, "The first process must own the instance.");
                var handle = form.Handle;
                // Signal before the UI/listener exists: no activation may be lost.
                using (var child = Child("show", name))
                {
                    Check(child.WaitForExit(5000) && child.ExitCode == 0, "A second process must yield ownership.");
                }
                int activations = 0;
                owner.Listen(form, () => activations++);
                PumpUntil(() => activations == 1);
                Check(activations == 1, "A signal sent during startup must reach the UI.");
                using (var child = Child("show", name))
                {
                    PumpUntil(() => child.HasExited && activations == 2);
                    Check(child.HasExited && child.ExitCode == 0 && activations == 2, "A second launch must request activation exactly once.");
                }
                using (var child = Child("silent", name))
                {
                    PumpUntil(() => child.HasExited);
                    Check(child.HasExited && child.ExitCode == 0 && activations == 2, "Automatic startup must not wake an existing launcher.");
                }
            }
            using (var next = new DesktopInstance(name)) Check(next.IsOwner, "A normal exit must release the singleton.");
            using (var crashed = Child("hold", name))
            {
                Check(crashed.StandardOutput.ReadLine() == "READY", "Crash fixture must own the instance.");
                crashed.Kill(); crashed.WaitForExit();
                using (var recovered = new DesktopInstance(name)) Check(recovered.IsOwner, "A crashed owner must not prevent relaunch.");
            }

            string keyPath = @"Software\AONexus\Tests\Startup-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    key.SetValue("Unrelated", "keep");
                    Check(key.GetValue(WindowsStartup.ValueName) == null, "New startup preferences must be off.");
                    string executable = Process.GetCurrentProcess().MainModule.FileName;
                    WindowsStartup.UpdateEntry(key, true, executable);
                    Check((string)key.GetValue(WindowsStartup.ValueName) == "\"" + executable + "\" --startup", "Startup must quote the full path and request tray mode.");
                    WindowsStartup.UpdateEntry(key, true, executable);
                    Check(key.ValueCount == 2, "Enabling repeatedly must not create duplicate entries.");
                    WindowsStartup.UpdateEntry(key, false, executable);
                    WindowsStartup.UpdateEntry(key, false, executable);
                    Check(key.GetValue(WindowsStartup.ValueName) == null && (string)key.GetValue("Unrelated") == "keep", "Disabling must remove only Nexus and remain idempotent.");
                    bool rejected = false;
                    try { WindowsStartup.UpdateEntry(key, true, executable + ".missing"); }
                    catch (IOException) { rejected = true; }
                    Check(rejected && key.GetValue(WindowsStartup.ValueName) == null, "A missing EXE must not be registered.");
                }
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
            Console.WriteLine("PASS: " + checks + " desktop lifetime checks; isolated processes and registry key only.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static Process Child(string mode, string name)
    {
        return Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, mode + " " + name)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
    }

    private static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done() && watch.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(10); }
    }
}
