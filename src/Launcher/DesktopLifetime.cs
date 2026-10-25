using System;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AOLauncher
{
    internal static class WindowsStartup
    {
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string ValueName = "AO Nexus";

        internal static bool IsEnabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return !string.IsNullOrWhiteSpace(key?.GetValue(ValueName) as string);
            }
        }

        internal static void SetEnabled(bool enabled, string executable)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                UpdateEntry(key, enabled, executable);
        }

        // The registry location is supplied separately so tests can use an isolated key.
        internal static void UpdateEntry(RegistryKey key, bool enabled, string executable)
        {
            if (!enabled) { key.DeleteValue(ValueName, false); return; }
            string fullPath = Path.GetFullPath(executable);
            if (fullPath.IndexOf('"') >= 0 || !File.Exists(fullPath)) throw new IOException("The launcher executable could not be found.");
            key.SetValue(ValueName, "\"" + fullPath + "\" --startup", RegistryValueKind.String);
        }
    }

    // One owner per Windows user and interactive session, independent of EXE path/version.
    // The signal carries no arguments, account information or commands: only "show".
    internal sealed class DesktopInstance : IDisposable
    {
        private readonly Mutex mutex;
        private readonly EventWaitHandle activate;
        private RegisteredWaitHandle listener;
        internal bool IsOwner { get; }

        internal static string InstanceName => @"Local\AONexus.Desktop." + WindowsIdentity.GetCurrent().User.Value;

        internal DesktopInstance(string name)
        {
            activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show");
            mutex = new Mutex(false, name + ".Owner");
            try { IsOwner = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { IsOwner = true; }
        }

        internal void RequestShow() => activate.Set();

        internal void Listen(Form window, Action show)
        {
            listener = ThreadPool.RegisterWaitForSingleObject(activate, (state, timedOut) =>
            {
                try
                {
                    if (!window.IsDisposed && window.IsHandleCreated)
                        window.BeginInvoke((Action)(() => { if (!window.IsDisposed) show(); }));
                }
                catch (InvalidOperationException) { /* Window closed while activation was queued. */ }
            }, null, Timeout.Infinite, false);
        }

        public void Dispose()
        {
            listener?.Unregister(null);
            if (IsOwner) mutex.ReleaseMutex();
            mutex.Dispose();
            activate.Dispose();
        }
    }
}
