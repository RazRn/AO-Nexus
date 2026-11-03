using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        LinkLabel graphicsLink;
        LinkLabel startupLink;
        string graphicsStatusKey;
        int graphicsStatusRequest;
        internal Func<string, bool?> ReadGraphicsMode;
        static bool? ReadInstalledGraphicsMode(string folder) {
            try { return AovkGraphics.Read(folder).Enabled; }
            catch (Exception) { return null; } // Settings explains missing/unsupported/recovery states.
        }
        internal string[] SelectedGraphicsFolders() => settings.Accounts
            .Where(account => account.Characters.Any(character => character.Selected))
            .Select(account => {
                try { return AovkGraphics.NormalizeFolder(account.GamePath); }
                catch (Exception) { return account.GamePath ?? ""; }
            }).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        internal static string GraphicsStatusText(bool?[] modes)
        {
            if (modes.Length == 0) return AOLauncher.Localization.Texts.Get("Text_AO_VK_on_off_5F8D17890F04");
            if (modes.Any(mode => !mode.HasValue)) return AOLauncher.Localization.Texts.Get("Text_AO_VK_check_setup_C5F68740B458");
            if (modes.All(mode => mode.Value)) return AOLauncher.Localization.Texts.Get("Text_AO_VK_on_Change_0370BF5E3F53");
            if (modes.All(mode => !mode.Value)) return AOLauncher.Localization.Texts.Get("Text_AO_VK_off_Change_2BCAD5C81094");
            return AOLauncher.Localization.Texts.Get("Text_AO_VK_mixed_Change_C411DA1F2DEE");
        }
        internal static string StartupStatusText(bool enabled, NexusEngine.StartupProfile[] profiles)
        {
            if(!enabled)return AOLauncher.Localization.Texts.Get("Text_Fast_loading_off_Settings_1930B6E2F6F8");
            if(profiles.Length==0)return AOLauncher.Localization.Texts.Get("Text_Fast_loading_select_characters_B1C43FACF3B6");
            if(profiles.Any(p=>p==NexusEngine.StartupProfile.None))return AOLauncher.Localization.Texts.Get("Text_Fast_loading_unsupported_client_77BFB211842B");
            return AOLauncher.Localization.Texts.Get("Text_Fast_loading_ready_D6BC810CFEF5");
        }
        async void RefreshGraphicsStatus(bool force = false) { await RefreshGraphicsStatusAsync(force); }
        internal async Task RefreshGraphicsStatusAsync(bool force)
        {
            if (graphicsLink == null || !IsHandleCreated || IsDisposed) return;
            graphicsLink.Enabled = !running && !guiOperationBusy;
            startupLink.Enabled = graphicsLink.Enabled;
            var folders = SelectedGraphicsFolders();
            string key = settings.Language + "\n" + settings.FastStartup + "\n" + settings.ExperimentalAovk + "\n" + string.Join("\n", folders);
            if (!force && key == graphicsStatusKey) return;
            graphicsStatusKey = key;
            int request = ++graphicsStatusRequest;
            if ((smoke && ReadGraphicsMode == null) || folders.Length == 0) { graphicsLink.Text = GraphicsStatusText(new bool?[0]);startupLink.Text=StartupStatusText(settings.FastStartup,new NexusEngine.StartupProfile[0]); return; }
            graphicsLink.Text = AOLauncher.Localization.Texts.Get("Text_AO_VK_checking_7A6240D52052");
            bool?[] modes;
            bool fast=settings.FastStartup;
            var profiles=new NexusEngine.StartupProfile[0];
            try { modes = await Task.Run(() => folders.Select(folder => (ReadGraphicsMode ?? ReadInstalledGraphicsMode)(folder)).ToArray()); }
            catch (Exception error) { FailureDiagnostics.Install.Report("graphics-status", "Could not inspect installed graphics", error); modes = folders.Select(folder => (bool?)null).ToArray(); }
            if(!smoke && fast) profiles=await Task.Run(()=>folders.Select(folder=> {
                try {return NexusEngine.FastStartup.GetAllowedProfile(folder,settings.ExperimentalAovk);}
                catch (Exception error) { FailureDiagnostics.Launch.Report("startup-profile", "Compatibility check failed", error); return NexusEngine.StartupProfile.None; }
            }).ToArray());
            if (IsDisposed || request != graphicsStatusRequest) return;
            graphicsLink.Text = GraphicsStatusText(modes);
            startupLink.Text=StartupStatusText(fast,profiles);
            startupLink.LinkColor=fast && profiles.Length>0 && profiles.All(p=>p!=NexusEngine.StartupProfile.None)?Theme.Accent:Theme.Amber;
            loginTips.SetToolTip(startupLink,AOLauncher.Localization.Texts.Get("Text_Checks_the_selected_game_files_against_0510F5DBD1D6"));
            loginTips.SetToolTip(graphicsLink, AOLauncher.Localization.Texts.Get("Text_Next_login_uses_each_folder_s_32D1945899C7")
                + "\n" + string.Join("\n", folders.Select((folder, index) => folder + " — " + (modes[index] == true ? "AO-VK" : modes[index] == false ? AOLauncher.Localization.Texts.Get("Text_Original_graphics_05A6C4BF6DB4") : AOLauncher.Localization.Texts.Get("Text_Check_setup_264F7CBA00A4")))));
        }
    }
}
