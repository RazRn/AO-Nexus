using System.Drawing;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed class PrivacyDialog : EditorDialog
    {
        internal PrivacyDialog() : base(AOLauncher.Localization.Texts.Get("Text_Privacy_security_921BF4095BEF"), 620)
        {
            FitContentOnOpen = false;
            ClientSize = new Size(670, 620);
            Section(AOLauncher.Localization.Texts.Get("Text_Passwords_9BC183AC738E"),
                AOLauncher.Localization.Texts.Get("Text_Saving_is_optional_for_each_account_98B36AE62384"));
            Section(AOLauncher.Localization.Texts.Get("Text_Local_protection_has_limits_531E93C5E745"),
                AOLauncher.Localization.Texts.Get("Text_Passwords_must_be_available_in_memory_21FDDE6A5C35"));
            Section(AOLauncher.Localization.Texts.Get("Text_Network_connections_5D8160B918FD"),
                AOLauncher.Localization.Texts.Get("Text_Login_connects_to_the_address_and_F8C1277F9D0D"));
            Section(AOLauncher.Localization.Texts.Get("Text_Updates_and_links_BF9392DC6693"),
                AOLauncher.Localization.Texts.Get("Text_Optional_startup_checks_and_manual_checks_812C98F225AF"));
            Section(AOLauncher.Localization.Texts.Get("Text_Game_GUI_templates_skins_scripts_and_DE5AA562D172"),
                AOLauncher.Localization.Texts.Get("Text_Templates_and_recovery_backups_stay_in_41EA999D6C31"));
            Section(AOLauncher.Localization.Texts.Get("Text_Diagnostics_and_problem_reports_EE5C76D2D840"),
                AOLauncher.Localization.Texts.Get("Text_An_anonymised_diagnostics_log_records_errors_C6841B46F1C5"));
            Section(AOLauncher.Localization.Texts.Get("Text_Client_measurements_and_exit_reports_40C182A4D9A3"),
                AOLauncher.Localization.Texts.Get("Text_Optional_AO_measurements_require_the_NexusHealthPlugin_C3F579E2B2AB"));
            Section(AOLauncher.Localization.Texts.Get("Text_Files_and_cleanup_6FE4CDC64AF6"),
                AOLauncher.Localization.Texts.Get("Text_Profiles_encrypted_credentials_news_cache_bundled_8F59B703C96B"));
            SaveButton.Visible = false; CancelAction.Text = AOLauncher.Localization.Texts.Get("Text_Close_B182D0987175");
            AcceptButton = CancelAction; ActiveControl = CancelAction;
            Shown += (sender, e) => { Fields.AutoScrollPosition = Point.Empty; CancelAction.Focus(); };
        }
        private void Section(string heading, string text)
        {
            Fields.Controls.Add(new Label { Text = heading, Font = new Font(Font, FontStyle.Bold), AutoSize = true,
                Dock = DockStyle.Top, ForeColor = Theme.Accent, Margin = new Padding(0, 14, 0, 4) });
            AddWrappedNote(text, "privacyNote");
        }
    }
}
