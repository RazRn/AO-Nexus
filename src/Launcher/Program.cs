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
            bool preview = args.Any(arg => arg == "--preview-ui");
            bool smoke = preview || args.Any(arg => arg == "--smoke" || arg == "--check-ui");
            using (var form = new MainForm(smoke))
            {
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
