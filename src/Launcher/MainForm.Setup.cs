using System;
using System.IO;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        private void RunSetup(bool newOptionsOnly = false)
        {
            if (storageBlocked || running || guiOperationBusy) return;
            bool startup;
            try { startup = !smoke && WindowsStartup.IsEnabled; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException)
            { ShowInfo(error.Message); return; }
            using (var wizard = new SetupWizard(settings, startup, !settings.HasCompletedSetup, newOptionsOnly))
            {
                wizard.Commit = (choice, windowsStartup) =>
                {
                    if (smoke) return null;
                    bool startupChanged = windowsStartup != startup;
                    try
                    {
                        if (startupChanged) WindowsStartup.SetEnabled(windowsStartup, Application.ExecutablePath);
                        SettingsStore.Save(choice);
                        return null;
                    }
                    catch (Exception error)
                    {
                        string detail = error.Message;
                        if (startupChanged)
                        {
                            try { WindowsStartup.SetEnabled(startup, Application.ExecutablePath); }
                            catch (Exception rollback) { detail += "\r\n" + AOLauncher.Localization.Texts.Get("Text_Windows_startup_could_not_be_restored_0915547895E2") + rollback.Message; }
                        }
                        return AOLauncher.Localization.Texts.Get("Text_Setup_could_not_be_saved_Your_A765E9899023") + detail;
                    }
                };
                if (wizard.ShowDialog(this) != DialogResult.OK) { ApplyLanguage(); return; }
                settings = wizard.Result;
            }
            ApplyLanguage(); UpdateSelection();
            backgroundClients?.SetEnabled(settings.MinimizeInactiveClients);
        }
    }
}
