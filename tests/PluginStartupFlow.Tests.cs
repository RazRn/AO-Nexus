using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AOLauncher
{
    internal static class PluginStartupFlowTests
    {
        static int checks;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        static void Finish(Task task) {
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted && watch.ElapsedMilliseconds < 20000) { Application.DoEvents(); Thread.Sleep(1); }
            Check(task.IsCompleted, "Test flow completes without real IPC"); task.GetAwaiter().GetResult();
        }
        [STAThread]
        public static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        static int Run(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Control.CheckForIllegalCrossThreadCalls = true;
            Application.EnableVisualStyles();
            ServerRules.Install();
            var pack = AOSharpPack.FromFolder(args[0], out var error); Check(pack != null, error);
            foreach (string language in new[] { "en", "da" })
            foreach (float scale in new[] { 1f, 1.5f })
            foreach (string scenario in new[] { "core", "extended", "failed", "noanswer", "wrongcharacter", "unknownprofession", "sendfailed", "restorefailed" })
            {
                Console.WriteLine("Checking " + language + " / " + scale + " / " + scenario);
                Localization.Texts.SetLanguage(language);
                var row = new PluginAutoInjector.Row { Pid = -1, CharacterName = "Preview", AccountId = "fixture" };
                var settings = new LauncherSettings();

                settings.Accounts.Add(new AccountConfig { Id = "fixture", Server = "test.invalid", Port = 7500 });
                using (var dialog = new PluginTestDialog(new List<PluginAutoInjector.Row> { row }, pack.Plugins.ToList(), settings, row))
                {

                    dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-18000, -18000); dialog.ShowInTaskbar = false;
                    var sent = new List<string[]>(); var saved = new List<PluginTestRecord>(); int restored = 0, unloaded = 0; bool testing = false;
                    dialog.ReadCharacter = pid => { return Tuple.Create(scenario == "unknownprofession" ? "Unknown" : "Doctor", scenario == "wrongcharacter" ? "Different" : "Preview"); };
                    dialog.SendPlugins = (client, paths, why) => { sent.Add(paths); return new PluginHostClient.Response { Ok = scenario != "sendfailed", Error = scenario == "sendfailed" ? "fixture send failure" : null }; };
                    dialog.ObserveStartup = (client, paths, nameOf) => Task.FromResult(scenario == "noanswer" ? null : paths.Select(path => new PluginCheck {
                        Path = path, Name = nameOf(path), Status = scenario == "failed" ? PluginStatus.Failed : PluginStatus.Running }).ToList());
                    dialog.SetTesting = (client, active) => testing = active;
                    dialog.LoadNow = (client, config) => { restored++; return scenario == "restorefailed" ? "fixture restore failure" : null; };
                    dialog.UnloadNow = client => { unloaded++; return null; };
                    dialog.SaveResults = records => saved.AddRange(records);
                    if (scale != 1) dialog.Scale(new SizeF(scale, scale));
                    dialog.Show(); Application.DoEvents();
                    foreach (var resultGrid in dialog.Controls.OfType<Control>().SelectMany(Descendants).OfType<DataGridView>())
                        resultGrid.DataError += (sender, e) => { Console.Error.WriteLine("GRID ERROR: " + e.Exception); e.ThrowException = true; };

                    ((ComboBox)dialog.Controls.Find("testScope", true).Single()).SelectedIndex = 1;
                    ((CheckBox)dialog.Controls.Find("testScenarios", true).Single()).Checked = scenario == "extended";

                    // Application.Run installs this in production; this bounded test pumps manually.
                    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    var task = (Task)typeof(PluginTestDialog).GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, null);

                    Finish(task);
                    int expected = scenario == "extended" ? 82 : scenario == "wrongcharacter" ? 0 :
                        new[] { "failed", "noanswer", "sendfailed", "unknownprofession" }.Contains(scenario) ? 1 : 2;
                    Check(sent.Count == expected, scenario + " admits expected targets: " + sent.Count);
                    Check(sent.All(paths => paths.Length == 2 && paths[0] == pack.Plugins.Single(p => p.Id == PluginSet.ShortcutsId).Path), "Only one target plus instrumentation per load");
                    Check(restored == (expected == 0 ? 0 : 1) && !testing, "Restore only after modification and release testing flag");
                    Check(unloaded == (scenario == "restorefailed" ? 1 : 0), "Failed restore removes test plugins");
                    int expectedRecords = new[] { "noanswer", "wrongcharacter", "sendfailed" }.Contains(scenario) ? 0 : expected;
                    Check(saved.Count == expectedRecords, "Only observed targets get saved results");
                    Check(saved.All(record => record.TestKind == "Startup" && record.PluginSha256.Length == 64 && record.ServerEndpoint.Contains("test.invalid") && record.Profession != null), "Saved evidence includes hash, test kind, server and profession");
                    if (scenario == "core") Check(saved.Select(r => r.PluginId).OrderBy(x => x).SequenceEqual(new[] { "pack:Z.Handler.Doc", "pack:Z.SyncManager" }), "Doctor gets only matching core handler and sync");
                    var grid = dialog.Controls.OfType<Control>().SelectMany(Descendants).OfType<DataGridView>().Single();
                    Check(grid.Height > 100, "Results retain usable space in " + language);
                    if (scenario == "core") {
                        using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                        using (var graphics = Graphics.FromImage(bitmap)) {
                            var dc = graphics.GetHdc(); try { Check(PrintWindow(dialog.Handle, dc, 0), "Dialog renders"); } finally { graphics.ReleaseHdc(dc); }
                            bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-test-" + language + "-" + (int)(scale * 100) + ".png"));
                        }
                    }
                }
            }
            using (var empty = new PluginTestDialog(new List<PluginAutoInjector.Row>(), pack.Plugins.ToList(), new LauncherSettings(), null))
                Check(((ComboBox)empty.Controls.Find("testCharacter", true).Single()).SelectedIndex == -1, "No running clients is supported");
            Console.WriteLine("PASS: " + checks + " startup flow checks; simulated responses only, no game or plugin execution.");
            return 0;
        }
        static IEnumerable<Control> Descendants(Control control) {
            yield return control;
            foreach (Control child in control.Controls) foreach (var item in Descendants(child)) yield return item;
        }
    }
}
