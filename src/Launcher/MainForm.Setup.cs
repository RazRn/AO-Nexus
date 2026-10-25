using System;
using System.IO;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        private void RunSetup()
        {
            if (storageBlocked || running || guiOperationBusy) return;
            bool startup;
            try { startup = !smoke && WindowsStartup.IsEnabled; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException)
            { ShowInfo(error.Message); return; }
            using (var wizard = new SetupWizard(settings, startup, !settings.HasCompletedSetup))
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
                            catch (Exception rollback) { detail += "\r\n" + L("Windows startup could not be restored: ", "Windows-start kunne ikke gendannes: ") + rollback.Message; }
                        }
                        return L("Setup could not be saved. Your profile has not been replaced.\r\n", "Opsætningen kunne ikke gemmes. Din profil er ikke erstattet.\r\n") + detail;
                    }
                };
                if (wizard.ShowDialog(this) != DialogResult.OK) { ApplyLanguage(); return; }
                settings = wizard.Result;
            }
            ApplyLanguage(); UpdateSelection();
        }
    }
}
