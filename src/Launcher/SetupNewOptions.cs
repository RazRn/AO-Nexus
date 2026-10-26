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
            internal string Name, English, Danish, HelpEn, HelpDa;
            internal Func<LauncherSettings, bool> Read;
            internal Action<LauncherSettings, bool> Write;
        }
        internal static readonly Option[] All = {
            new Option { Introduced=1, Name="quietLaunch", English="Skip login screens (faster)", Danish="Spring login-skærme over (hurtigere)",
                HelpEn="Go straight to the selected character. Login errors remain visible.", HelpDa="Gå direkte til den valgte char. Login-fejl vises stadig.", Read=s=>s.QuietLaunch, Write=(s,v)=>s.QuietLaunch=v },
            new Option { Introduced=1, Name="fastStartup", English="Faster game loading", Danish="Hurtigere indlæsning af spillet",
                HelpEn="Shortens initial loading on supported classic clients. Turn off if loading causes problems.", HelpDa="Forkorter indlæsningen på understøttede klassiske klienter. Slå fra ved problemer med indlæsning.", Read=s=>s.FastStartup, Write=(s,v)=>s.FastStartup=v },
            new Option { Introduced=1, Name="minimizeInactiveClients", English="Minimize inactive game clients (experimental)", Danish="Minimer inaktive spilklienter (eksperimentel)",
                HelpEn="Only when switching to another Nexus-managed AO client. Other apps leave game windows unchanged. Nexus must keep running.", HelpDa="Kun ved skift til en anden Nexus-styret AO-klient. Andre apps ændrer ikke spilvinduerne. Nexus skal køre.", Read=s=>s.MinimizeInactiveClients, Write=(s,v)=>s.MinimizeInactiveClients=v },
            new Option { Introduced=1, Name="monitorClientHealth", English="Show AOSharp responsiveness in header and tray", Danish="Vis AOSharp-respons i header og bakke",
                HelpEn="Requires NexusHealthPlugin.dll in your AOSharp profile. Measures update pauses and local IPC replies, not server packet loss. Uses AOSharp's UDP multicast transport while measured clients are open.",
                HelpDa="Kræver NexusHealthPlugin.dll i din AOSharp-profil. Måler pauser og lokale IPC-svar, ikke pakketab til serveren. Bruger AOSharps UDP-multicast, mens målte klienter er åbne.", Read=s=>s.MonitorClientHealth, Write=(s,v)=>s.MonitorClientHealth=v },
            new Option { Introduced=1, Name="collectClientExitReports", English="Keep local client exit reports", Danish="Gem lokale rapporter om afsluttede klienter",
                HelpEn="Keeps up to 20 reports with recent measurements while Nexus runs. Save a ZIP from the tray menu; no passwords, chat, memory dumps or automatic uploads. An exit alone does not prove a crash.",
                HelpDa="Gemmer op til 20 rapporter med seneste målinger, mens Nexus kører. Gem ZIP fra bakkemenuen; ingen passwords, chat, hukommelsesdumps eller automatisk upload. Et exit alene beviser ikke et crash.", Read=s=>s.CollectClientExitReports, Write=(s,v)=>s.CollectClientExitReports=v }
        };
        internal static bool NeedsReview(LauncherSettings settings) => settings.HasCompletedSetup && settings.OptionsReviewVersion < CurrentVersion;
        internal static void MarkReviewed(LauncherSettings settings) => settings.OptionsReviewVersion = Math.Max(settings.OptionsReviewVersion, CurrentVersion);
        internal static void Add(Control parent, LauncherSettings draft, int afterVersion)
        {
            foreach (var option in All.Where(o => o.Introduced > afterVersion)) {
                var captured = option;
                var check = new NexusCheckBox { Name = "setup-" + option.Name, Text = L(option.English, option.Danish), Checked = option.Read(draft),
                    AutoSize = true, Dock = DockStyle.Top, MaximumSize = new Size(Math.Max(100, parent.ClientSize.Width - 24), 0), Margin = new Padding(0, 8, 0, 8) };
                check.CheckedChanged += (sender, args) => captured.Write(draft, check.Checked);
                var note = new Label { Text = L(option.HelpEn, option.HelpDa), AutoSize = true, Dock = DockStyle.Top,
                    ForeColor = Theme.Muted, MaximumSize = check.MaximumSize, Margin = new Padding(0, 0, 0, 16) };
                EventHandler resized = (sender, args) => { if (!check.IsDisposed) check.MaximumSize = note.MaximumSize = new Size(Math.Max(100, parent.ClientSize.Width - 24), 0); };
                parent.ClientSizeChanged += resized;
                check.Disposed += (sender, args) => parent.ClientSizeChanged -= resized;
                parent.Controls.Add(check); parent.Controls.Add(note);
            }
        }
    }
}
