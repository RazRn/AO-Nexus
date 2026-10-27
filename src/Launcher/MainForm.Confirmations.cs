using System;
using System.Windows.Forms;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        // Isolated UI fixtures can answer without launching or closing a game.
        internal Func<bool, string, Tuple<bool, bool>> GameActionPrompt;

        private bool ConfirmGameAction(bool teamLogout, string summary)
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
                { accepted = dialog.ShowDialog(this) == DialogResult.OK; remember = dialog.DontShowAgain; }
            }
            else
            {
                using (var dialog = new LaunchConfirmationDialog(summary))
                { accepted = dialog.ShowDialog(this) == DialogResult.OK; remember = dialog.DontShowAgain; }
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
