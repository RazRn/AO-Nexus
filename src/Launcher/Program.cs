using System;
using System.Linq;
using System.Windows.Forms;

namespace AOLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--apply-update") { UpdateInstaller.Apply(args[1]); return; }
            bool preview = args.Any(arg => arg == "--preview-ui");
            bool smoke = preview || args.Any(arg => arg == "--smoke" || arg == "--check-ui");
            bool startup = args.Any(arg => arg == "--startup");
            // Synthetic previews must never activate or interfere with a real session.
            using (var instance = smoke ? null : new DesktopInstance(DesktopInstance.InstanceName))
            {
                if (instance != null && !instance.IsOwner)
                {
                    if (!startup) instance.RequestShow();
                    return;
                }
                using (var form = new MainForm(smoke))
                {
                    form.Shown += (sender, e) =>
                    {
                        if (startup) form.MinimizeToTray();
                        instance?.Listen(form, form.ShowLauncher);
                    };
                    if (smoke)
                    {
                        var timer = new Timer { Interval = preview ? 180000 : 15000 };
                        timer.Tick += (sender, e) => { timer.Stop(); timer.Dispose(); form.Close(); };
                        form.Shown += (sender, e) => timer.Start();
                    }
                    Application.Run(form);
                }
            }
        }
    }
}
