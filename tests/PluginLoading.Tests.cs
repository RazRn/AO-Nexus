using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AOLauncher
{
    internal static class PluginLoadingTests
    {
        static int checks;
        static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        static object Field(MainForm form, string name) => typeof(MainForm).GetField(name, Fields).GetValue(form);
        static Task Open(MainForm form, string section) => (Task)typeof(MainForm).GetMethod("ShowSection", Fields).Invoke(form, new object[] { section });
        static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        static void Finish(Task task)
        {
            var time = Stopwatch.StartNew();
            while (!task.IsCompleted && time.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(1); }
            Check(task.IsCompleted, "Preparation continuation completes");
            task.GetAwaiter().GetResult();
        }
        [STAThread]
        public static int Main()
        {
            Application.EnableVisualStyles();
            foreach (string language in new[] { "en", "da" })
            using (var form = new MainForm(true))
            {
                Localization.Texts.SetLanguage(language);
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-18000, -18000); form.ShowInTaskbar = false;
                form.Show(); Application.DoEvents();
                var slow = new TaskCompletionSource<bool>();
                form.BeforePluginsLoad = () => slow.Task;
                Task first = Open(form, "plugins");
                Check(!first.IsCompleted && (string)Field(form, "currentSection") == "plugins", "Tab switches before file preparation finishes");
                var loading = form.Controls.Find("pluginsLoading", true).Single();
                Check(loading.Visible && loading.Text.Contains(language == "da" ? "Klargør" : "Preparing"), "Loading status is visible in the selected language");
                Check(!form.UseWaitCursor, "Other controls are not marked busy");
                Finish(Open(form, "characters"));
                Check((string)Field(form, "currentSection") == "characters", "Navigation is available during first preparation");
                slow.SetResult(true); Finish(first);
                Check(Field(form, "pluginsView") == null && loading.IsDisposed, "An abandoned load cannot create a page");

                var older = new TaskCompletionSource<bool>(); form.BeforePluginsLoad = () => older.Task;
                Task oldRequest = Open(form, "plugins"); Finish(Open(form, "characters"));
                var newer = new TaskCompletionSource<bool>(); form.BeforePluginsLoad = () => newer.Task;
                Task newRequest = Open(form, "plugins"); newer.SetResult(true); Finish(newRequest);
                var view = (PluginsDialog)Field(form, "pluginsView");
                Check(view != null && view.Visible, "Latest request shows the usable plugin page");
                older.SetResult(true); Finish(oldRequest);
                Check(ReferenceEquals(view, Field(form, "pluginsView")) && !view.IsDisposed, "Late older completion preserves the current editor");
                Check(form.Controls.Find("pluginsLoading", true).Length == 0, "Loading controls are released after completion");
                Finish(Open(form, "characters"));
                var closing = new TaskCompletionSource<bool>(); form.BeforePluginsLoad = () => closing.Task;
                Task pending = Open(form, "plugins"); form.Dispose(); closing.SetResult(true); Finish(pending);
                Check(form.IsDisposed, "Closing during preparation does not reopen or access the disposed window");
            }
            Console.WriteLine("PASS: " + checks + " plugin loading/navigation checks; isolated smoke data, no games or injection.");
            return 0;
        }
    }
}
