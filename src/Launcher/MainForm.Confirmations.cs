using System;
using System.Windows.Forms;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        // Isolated UI fixtures can answer without launching or closing a game.
        internal Func<bool, string, Tuple<bool, bool>> GameActionPrompt;

        private bool ConfirmGameAction(bool teamLogout, string summary) => ConfirmGameActionWithOwner(teamLogout,summary,this);

        private bool ConfirmTeamWindowLogout(string summary, IWin32Window owner)
        {
            // Card and bulk clicks always confirm, independently of preset/hotkey preferences.
            if (GameActionPrompt != null) return GameActionPrompt(true, summary).Item1;
            using (var dialog = new TeamWindowLogoutDialog(summary))
                return dialog.ShowDialog(owner ?? this) == DialogResult.OK;
        }

        private bool ConfirmTeamWindowRelog(string summary, IWin32Window owner)
        {
            if (GameActionPrompt != null) return GameActionPrompt(true, summary).Item1;
            using (var dialog = new TeamWindowLogoutDialog(summary,true))
                return dialog.ShowDialog(owner ?? this) == DialogResult.OK;
        }

        private bool ConfirmTeamWindowRecovery(string character,bool restart,IWin32Window owner)
        {
            string title=restart?AOLauncher.Localization.Texts.Get("Text_Force_restart_9F8A0BA2F49F"):AOLauncher.Localization.Texts.Get("Text_Force_close_B63FAD86B93B");
            string message=title+" · "+character+"?\r\n\r\n"+AOLauncher.Localization.Texts.Get("Text_This_ends_only_this_character_s_C9D55C23CAFC");
            if(restart)message+="\r\n\r\n"+AOLauncher.Localization.Texts.Get("Text_Nexus_will_log_this_character_back_69DD7FC9BDD5");
            if(GameActionPrompt!=null)return GameActionPrompt(true,message).Item1;
            return NexusMessageBox.Show(owner??this,message,title,MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
        }

        private bool ConfirmGameActionWithOwner(bool teamLogout, string summary, IWin32Window owner)
        {
            if (teamLogout ? settings.SkipTeamLogoutConfirmation : settings.SkipLoginConfirmation) return true;
            bool accepted, remember;
            if (GameActionPrompt != null)
            {
                var answer = GameActionPrompt(teamLogout, summary);
                accepted = answer.Item1; remember = answer.Item2;
            }
            else if (teamLogout)
            {
                using (var dialog = new LogoutConfirmationDialog(summary))
                { accepted = dialog.ShowDialog(owner ?? this) == DialogResult.OK; remember = dialog.DontShowAgain; }
            }
            else
            {
                using (var dialog = new LaunchConfirmationDialog(summary))
                { accepted = dialog.ShowDialog(owner ?? this) == DialogResult.OK; remember = dialog.DontShowAgain; }
            }
            if (accepted && remember)
            {
                if (teamLogout) settings.SkipTeamLogoutConfirmation = true;
                else settings.SkipLoginConfirmation = true;
                SaveSettings();
            }
            return accepted;
        }
    }
}
