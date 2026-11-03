using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace AOLauncher
{
    internal static class PluginThemeTests
    {
        static int checks;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        static void Invoke(Control control, string method, object argument)
            => control.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new[] { argument });
        static void Pump() { for (int i = 0; i < 4; i++) Application.DoEvents(); }
        [STAThread]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            ServerRules.Install();
            var settings = new LauncherSettings { AOSharpPackPath = args[Array.IndexOf(args, "--pack") + 1], IncludeHealthMonitor = false };
            var account = new AccountConfig { Label = "Preview account" };
            for (int i = 0; i < 25; i++) {
                var set = new PluginSet { Name = "Preview set " + i.ToString("00") }; settings.PluginSets.Add(set);
                account.Characters.Add(new CharacterConfig { Name = "Preview character " + i, CharacterId = i + 1, PluginSetId = set.Id });
            }
            settings.Accounts.Add(account);
            var pack = AOSharpPack.FromFolder(settings.AOSharpPackPath, out var error);
            Check(pack != null, error);
            foreach (string language in new[] { "en", "da" })
            foreach (float scale in new[] { 1f, 1.5f })
            {
                Localization.Texts.SetLanguage(language);
                using (var form = new PluginsDialog(settings, pack, null))
                {
                    form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-18000, -18000); form.ShowInTaskbar = false;
                    if (scale != 1) form.Scale(new SizeF(scale, scale));
                    form.Show(); Pump();
                    var grid = (PluginGrid)typeof(PluginsDialog).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    var section = new PluginGrid.Section { Key = "preview", Title = "PREVIEW PLUGINS" };
                    for (int i = 0; i < 100; i++) section.Plugins.Add(new PluginInfo { Id = "preview-" + i, Name = "Preview plugin " + i });
                    grid.SetSections(new List<PluginGrid.Section> { section }); Pump();
                    var bar = grid.Controls.OfType<SlimScrollBar>().Single();
                    Check(bar.Visible && bar.Maximum > 0 && !grid.Controls.OfType<VScrollBar>().Any(), "Plugin grid uses a themed overflowing rail");
                    bar.ScrollTo(int.MaxValue); Check(bar.Position == bar.Maximum, "Thumb scrolling clamps at the final plugin");
                    Invoke(grid, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 5, 5, 120));
                    Check(bar.Position < bar.Maximum, "Mouse wheel moves the plugin viewport");
                    section.Collapsed = true; grid.SetSections(new List<PluginGrid.Section> { section });
                    Check(!bar.Visible && bar.Position == 0, "Folding resets scrolling and hides the rail");
                    section.Collapsed = false; grid.SetSections(new List<PluginGrid.Section> { section });
                    var list = form.Controls.Find("pluginSetList", true).Single();
                    Check(list.Parent.Parent is SlimScrollHost, "Set list shares the slim scrollbar");
                    var assignments = (DataGridView)form.Controls.Find("pluginAssignments", true).Single();
                    Check(assignments.Rows[0].Cells[1] is PluginSetComboCell, "Plugin choices use themed cells");
                    assignments.CurrentCell = assignments.Rows[0].Cells[1]; assignments.BeginEdit(true); Pump();
                    var editor = assignments.EditingControl as PluginSetComboEditor;
                    Check(editor != null && editor.DrawMode == DrawMode.OwnerDrawFixed && editor.BackColor == Theme.SurfaceRaised, "Active dropdown is dark and owner drawn");
                    editor.DroppedDown = true; Pump();
                    // Render the actual popup items as well as the resting page for visual inspection.
                    using (var item = new Bitmap(300, editor.ItemHeight))
                    using (var graphics = Graphics.FromImage(item))
                    {
                        typeof(ComboBox).GetMethod("OnDrawItem", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor,
                            new object[] { new DrawItemEventArgs(graphics, editor.Font, new Rectangle(Point.Empty, item.Size), 0, DrawItemState.None) });
                        Check(item.GetPixel(299, 1).ToArgb() == Theme.SurfaceRaised.ToArgb(), "Popup choices use the dark palette");
                    }
                    editor.SelectedIndex = 1; editor.DroppedDown = false; assignments.EndEdit(); Pump();
                    Check(form.Working.Accounts[0].Characters[0].PluginSetId == PluginSet.None, "Selecting No plugins still commits the assignment");
                    Check(settings.Accounts[0].Characters[0].PluginSetId != PluginSet.None, "Editing remains a draft");
                    assignments.CurrentCell = null; grid.Focus(); Pump();
                    var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugin-theme-previews"); Directory.CreateDirectory(path);
                    using (var image = new Bitmap(form.Width, form.Height))
                    {
                        using (var graphics = Graphics.FromImage(image)) { var dc = graphics.GetHdc(); try { Check(PrintWindow(form.Handle, dc, 0), "Native page capture succeeds"); } finally { graphics.ReleaseHdc(dc); } }
                        image.Save(Path.Combine(path, language + "-" + scale + ".png"));
                    }
                }
            }
            Console.WriteLine("PASS: " + checks + " plugin theme/scrolling/edit checks, synthetic accounts only."); return 0;
        }
    }
}
