using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    // Add future options with a new Introduced value and bump CurrentVersion.
    // Review progress is a feature schema, independent of the app's release number.
    internal static class SetupNewOptions
    {
        internal const int CurrentVersion = 1;
        internal sealed class Option
        {
            internal int Introduced;
            internal string Name, TextKey, HelpKey;
            internal Func<LauncherSettings, bool> Read;
            internal Action<LauncherSettings, bool> Write;
        }
        internal static readonly Option[] All = {
            new Option { Introduced=1, Name="quietLaunch", TextKey="Option_quietLaunch_Label", HelpKey="Option_quietLaunch_Help", Read=s=>s.QuietLaunch, Write=(s,v)=>s.QuietLaunch=v },
            new Option { Introduced=1, Name="fastStartup", TextKey="Option_fastStartup_Label", HelpKey="Option_fastStartup_Help", Read=s=>s.FastStartup, Write=(s,v)=>s.FastStartup=v },
            new Option { Introduced=1, Name="minimizeInactiveClients", TextKey="Option_minimizeInactiveClients_Label", HelpKey="Option_minimizeInactiveClients_Help", Read=s=>s.MinimizeInactiveClients, Write=(s,v)=>s.MinimizeInactiveClients=v },
            new Option { Introduced=1, Name="monitorClientHealth", TextKey="Option_monitorClientHealth_Label", HelpKey="Option_monitorClientHealth_Help", Read=s=>s.MonitorClientHealth, Write=(s,v)=>s.MonitorClientHealth=v },
            new Option { Introduced=1, Name="collectClientExitReports", TextKey="Option_collectClientExitReports_Label", HelpKey="Option_collectClientExitReports_Help", Read=s=>s.CollectClientExitReports, Write=(s,v)=>s.CollectClientExitReports=v }
        };
        internal static bool NeedsReview(LauncherSettings settings) => settings.HasCompletedSetup && settings.OptionsReviewVersion < CurrentVersion;
        internal static void MarkReviewed(LauncherSettings settings) => settings.OptionsReviewVersion = Math.Max(settings.OptionsReviewVersion, CurrentVersion);
        internal static void Add(Control parent, LauncherSettings draft, int afterVersion)
        {
            foreach (var option in All.Where(o => o.Introduced > afterVersion)) {
                var captured = option;
                var check = new NexusCheckBox { Name = "setup-" + option.Name, Text = AOLauncher.Localization.Texts.Get(option.TextKey), Checked = option.Read(draft),
                    AutoSize = true, Dock = DockStyle.Top, MaximumSize = new Size(Math.Max(100, parent.ClientSize.Width - 24), 0), Margin = new Padding(0, 8, 0, 8) };
                check.CheckedChanged += (sender, args) => captured.Write(draft, check.Checked);
                var note = new Label { Text = AOLauncher.Localization.Texts.Get(option.HelpKey), AutoSize = true, Dock = DockStyle.Top,
                    ForeColor = Theme.Muted, MaximumSize = check.MaximumSize, Margin = new Padding(0, 0, 0, 16) };
                EventHandler resized = (sender, args) => { if (!check.IsDisposed) check.MaximumSize = note.MaximumSize = new Size(Math.Max(100, parent.ClientSize.Width - 24), 0); };
                parent.ClientSizeChanged += resized;
                check.Disposed += (sender, args) => parent.ClientSizeChanged -= resized;
                parent.Controls.Add(check); parent.Controls.Add(note);
            }
        }
    }
}
