using System;
using System.Collections.Generic;
using System.Linq;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal static class PluginCuration
    {
        internal static string Tier(PluginInfo plugin, IEnumerable<PluginInfo> catalog)
        {
            if (plugin.Custom) return "Custom";
            if (plugin.Suite == "Nexus") return "Nexus";
            bool hasZ = catalog.Any(item => item.Id.StartsWith("pack:Z.Handler.", StringComparison.OrdinalIgnoreCase));
            string recommended = hasZ ? "Z" : "AOSP";
            if (plugin.Suite == recommended && (plugin.Group == "Profession handlers" || plugin.Id == (hasZ ? "pack:Z.SyncManager" : "pack:Manager.Sync"))) return "Recommended";
            if (plugin.Suite != recommended && (PluginSets.AospToZ.ContainsKey(plugin.Id) || PluginSets.AospToZ.ContainsValue(plugin.Id))) return "Alternatives";
            return "Specialist";
        }
        internal static string Caution(PluginInfo plugin)
        {
            if (plugin.Id == "pack:Z.TeamAcceptUtility") return AOLauncher.Localization.Texts.Get("Text_Accepts_any_team_invite_while_not_3C38CD18C0B8");
            if (plugin.Id == "pack:Z.MailManager" || plugin.Id == "pack:Manager.Mail") return AOLauncher.Localization.Texts.Get("Text_Forwarding_can_send_credits_to_the_1E5E10D819F0");
            return null;
        }
    }
}
