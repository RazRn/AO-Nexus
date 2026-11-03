using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        // Before a login: a selected character whose earlier game window never reached the
        // game would be skipped silently by the login engine. Offer to close those windows.
        private async Task<bool> ResolveStuckClients(List<AccountConfig> accounts)
        {
            var snapshot = settings;
            var stuck = await Task.Run(() => LoginPreflight.FindStuck(snapshot, accounts, new LaunchRequest().ReadyTimeoutSeconds));
            if (stuck.Count == 0) return true;

            // Being logged in by another Nexus window right now: leave it alone.
            foreach (var item in stuck.Where(item => item.Kind == LoginPreflight.StuckKind.InProgress))
                AppendLog(item.Character.Name + ": " + AOLauncher.Localization.Texts.Get("Text_another_login_is_still_starting_this_0F0F7B2C2864"));

            // The window you are looking at: you may be using it, so ask.
            var inUse = stuck.Where(item => item.Kind == LoginPreflight.StuckKind.InUse).ToList();
            if (inUse.Count > 0)
            {
                string list = string.Join("\r\n", inUse.Select(item => "• " + item.Character.Name + " — " + AOLauncher.Localization.Texts.Get("Text_started_CCDCAEA4B7EA") + item.StartedLocal.ToString("HH:mm")
                    + (string.IsNullOrWhiteSpace(item.WindowTitle) ? "" : " · \"" + item.WindowTitle + "\"")));
                var answer = NexusMessageBox.Show(this,
                    AOLauncher.Localization.Texts.Get("Text_This_game_window_never_reached_the_C83F8349FB8F")
                    + "\r\n\r\n" + list + "\r\n\r\n"
                    + AOLauncher.Localization.Texts.Get("Text_Close_it_so_Nexus_can_log_02E37049049A"),
                    AOLauncher.Localization.Texts.Get("Text_Game_window_from_an_earlier_login_71B9B26A2614"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
                if (answer == DialogResult.Cancel) return false;
                foreach (var item in inUse)
                {
                    if (answer == DialogResult.Yes) { item.Kind = LoginPreflight.StuckKind.Stale; item.CloseApproved = true; }
                    else AppendLog(item.Character.Name + ": " + AOLauncher.Localization.Texts.Get("Text_skipped_you_kept_its_earlier_game_CADCAB5A51AC"));
                }
            }

            // Abandoned: no login is waiting for it and it never reached the game. Close it.
            foreach (var item in stuck.Where(item => item.Kind == LoginPreflight.StuckKind.Stale))
            {
                AppendLog(item.Character.Name + ": " + string.Format(AOLauncher.Localization.Texts.Get("Text_closing_a_game_window_from_an_7E5FBBD8D35A"), item.StartedLocal.ToString("HH:mm")));
                bool closed = await Task.Run(() => LoginPreflight.CloseOrForce(item));
                AppendLog(item.Character.Name + ": " + (closed ? AOLauncher.Localization.Texts.Get("Text_earlier_window_closed_logging_in_now_E8338B9B5DA9")
                    : AOLauncher.Localization.Texts.Get("Text_the_earlier_window_could_not_be_83409058C502")));
            }
            return true;
        }
    }
}
