using System;
using System.Collections.Generic;
using System.Linq;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    // How people normally play with the plugins. A play style adds the plugins for one activity
    // (team play, an Inferno run, a City raid...) on top of each character's own set, so one
    // profession set per character is enough. Styles are chosen per team (LaunchPreset.PlayStyle)
    // or for characters launched without a team (LauncherSettings.DefaultPlayStyle).
    internal sealed class PlayStyle
    {
        internal string Id, Name, About;
        internal string[] Steps = new string[0];
        // Plugin ids without "pack:", per suite. A style missing in a suite adds nothing there.
        internal string[] Z = new string[0], AOSP = new string[0];
        // Buttons shown under Running now; each runs a chat command in the chosen clients.
        internal GameAction[] Actions = new GameAction[0];
        public override string ToString() => Name;
    }

    internal sealed class GameAction
    {
        internal string Label, Command, Hint;
        internal bool LeaderOnly;
        public override string ToString() => Label;
    }

    internal static class PlayStyles
    {
        internal const string None = "none";

        static GameAction Act(string label, string command, string hint = null, bool leader = false) =>
            new GameAction { Label = label, Command = command, Hint = hint, LeaderOnly = leader };

        // Shared by every Z profession handler (Handler.Generic registers them).
        internal static GameAction[] TeamActions => new[]
        {
            Act(AOLauncher.Localization.Texts.Get("Text_Form_team_47202963D3BA"), "form", AOLauncher.Localization.Texts.Get("Text_Run_on_the_leader_invites_the_B102A4CFDF7F"), true),
            Act(AOLauncher.Localization.Texts.Get("Text_Form_raid_1F2C9E7AC0ED"), "form raid", AOLauncher.Localization.Texts.Get("Text_Run_on_the_leader_5E9C8DFCBC9B"), true),
            Act(AOLauncher.Localization.Texts.Get("Text_Disband_25F2A25575B7"), "disband"),
            Act(AOLauncher.Localization.Texts.Get("Text_Rebuff_5497F6125AE4"), "rebuff", AOLauncher.Localization.Texts.Get("Text_Clears_buffs_so_the_handlers_cast_ACCC3F6653EA")),
            Act(AOLauncher.Localization.Texts.Get("Text_Handler_settings_52BC84ECD163"), "handler", AOLauncher.Localization.Texts.Get("Text_Opens_the_profession_handler_window_in_8DA9FAD77D98")),
        };

        static PlayStyle Bot(string id, string name, string about, string zBot, string aospBot, string settings, params string[] extraZ) => new PlayStyle
        {
            Id = id, Name = name, About = about,
            Z = new[] { zBot, "Z.HelpManager" }.Concat(extraZ).ToArray(),
            AOSP = aospBot == null ? new string[0] : new[] { aospBot, "Manager.Help" },
            Steps = new[]
            {
                AOLauncher.Localization.Texts.Get("Text_Log_the_whole_team_in_at_0686AF7B84C5"),
                AOLauncher.Localization.Texts.Get("Text_Press_Form_team_or_type_form_871ECF20850D"),
                string.Format(AOLauncher.Localization.Texts.Get("Text_On_the_leader_open_0_pick_D092D6D7EA04"), settings),
                AOLauncher.Localization.Texts.Get("Text_Stop_it_the_same_way_or_BB84E27D344E"),
            },
            Actions = new[] { Act(string.Format(AOLauncher.Localization.Texts.Get("Text_Open_0_DEF1F898ED65"), "/" + settings), settings, AOLauncher.Localization.Texts.Get("Text_Opens_the_bot_s_window_switch_8358EEEC046C"), true) },
        };

        // Built on each use so names follow the current language.
        internal static List<PlayStyle> All => Build();

        static List<PlayStyle> Build() => new List<PlayStyle>
        {
            new PlayStyle { Id = None, Name = AOLauncher.Localization.Texts.Get("Text_Just_the_character_s_set_7122E8C41C72"),
                About = AOLauncher.Localization.Texts.Get("Text_No_extra_plugins_each_character_loads_5D66F13D8FE5") },
            new PlayStyle
            {
                Id = "team", Name = AOLauncher.Localization.Texts.Get("Text_Team_with_a_leader_0C3B23A5166F"),
                About = AOLauncher.Localization.Texts.Get("Text_You_play_the_leader_the_others_12F8BA0B8BD7"),
                Z = new[] { "Z.SyncManager", "Z.AssistManager", "Z.HelpManager", "Z.MovementManager" },
                AOSP = new[] { "Manager.Sync", "Manager.Assist", "Manager.Help", "Manager.Follow" },
                Steps = new[]
                {
                    AOLauncher.Localization.Texts.Get("Text_Log_the_team_in_F17A6B212EA8"),
                    AOLauncher.Localization.Texts.Get("Text_Press_Form_team_or_form_on_D0FD03F903D6"),
                    AOLauncher.Localization.Texts.Get("Text_Play_the_leader_In_syncmanager_on_3E33753E231F"),
                    AOLauncher.Localization.Texts.Get("Text_To_make_followers_walk_after_you_F1FA15BCFA4D"),
                },
                Actions = new[] { Act(AOLauncher.Localization.Texts.Get("Text_Sync_settings_2EF34C0BC390"), "syncmanager"), Act(AOLauncher.Localization.Texts.Get("Text_Open_bags_9C5465F9E4B0"), "openbags"), Act(AOLauncher.Localization.Texts.Get("Text_Auto_sit_for_kits_D5E92FDBCF97"), "autosit") },
            },
            Bot("inferno", AOLauncher.Localization.Texts.Get("Text_Inferno_missions_B016817D8E25"), AOLauncher.Localization.Texts.Get("Text_InfBuddy_runs_Inferno_missions_with_the_CFC91813DA13"), "Z.InfBuddy", "Automaton.Inf", "infbuddy"),
            Bot("s13", AOLauncher.Localization.Texts.Get("Text_Sector_13_A17D80A42FE3"), AOLauncher.Localization.Texts.Get("Text_S13Buddy_runs_Sector_13_with_the_3E9793FAC974"), "Z.S13Buddy", null, "s13buddy"),
            Bot("mitaar", AOLauncher.Localization.Texts.Get("Text_Mitaar_Hideout_786CF7F02146"), AOLauncher.Localization.Texts.Get("Text_MitaarBuddy_runs_Mitaar_Hideout_932E89B4B40D"), "Z.MitaarBuddy", "Automaton.Mitaar", "mitaarbuddy"),
            Bot("vortexx", AOLauncher.Localization.Texts.Get("Text_Vortexx_9E9108A78E9A"), AOLauncher.Localization.Texts.Get("Text_VortBuddy_runs_Vortexx_C91E686AF943"), "Z.VortBuddy", "Automaton.Vortexx", "vortbuddy"),
            Bot("kitehill", AOLauncher.Localization.Texts.Get("Text_Kite_Hill_29C80A040B44"), AOLauncher.Localization.Texts.Get("Text_KHBuddy_an_enforcer_pulls_and_the_00A85D707325"), "Z.KHBuddy", "Automaton.KiteHill", "khbuddy"),
            Bot("city", AOLauncher.Localization.Texts.Get("Text_City_raid_E56466CD4034"), AOLauncher.Localization.Texts.Get("Text_CityBuddy_the_leader_puts_up_the_71B3DA1A053C"), "Z.CityBuddy", "Automaton.City", "citybuddy", "Z.CloakUtility"),
            Bot("battlestation", AOLauncher.Localization.Texts.Get("Text_Battlestation_0EEAF72C3D81"), AOLauncher.Localization.Texts.Get("Text_BSBuddy_caps_points_Battlestation_Utility_signs_2B40DA45B1B0"), "Z.BSBuddy", null, "bsbuddy", "Z.BattleStationUtility"),
            Bot("farm", AOLauncher.Localization.Texts.Get("Text_Farm_an_area_A465B0969138"), AOLauncher.Localization.Texts.Get("Text_RoamBuddy_follows_saved_waypoints_fights_and_AEBB6987F9CD"), "Z.RoamBuddy", "Automaton.Roamba", "roambuddy", "Z.LootManager"),
            Bot("defend", AOLauncher.Localization.Texts.Get("Text_Hold_a_spot_2EC72C839C86"), AOLauncher.Localization.Texts.Get("Text_DefendBuddy_holds_its_ground_and_fights_FFE2F559DD46"), "Z.DefendBuddy", null, "defendbuddy", "Z.LootManager"),
            Bot("assist", AOLauncher.Localization.Texts.Get("Text_Fight_beside_the_team_882105D8F4E9"), AOLauncher.Localization.Texts.Get("Text_AttackBuddy_attacks_mobs_that_attack_the_74C30EE68DDA"), "Z.AttackBuddy", null, "attackbuddy"),
            new PlayStyle
            {
                Id = "market", Name = AOLauncher.Localization.Texts.Get("Text_Market_and_bank_91D405893D89"),
                About = AOLauncher.Localization.Texts.Get("Text_GMI_bag_sorting_and_selling_to_3D3D147684BD"),
                Z = new[] { "Z.GMIManager", "Z.BagManager", "Z.SellUtility" },
                AOSP = new[] { "Manager.GMI", "Manager.Bag", "Utility.SimpleAutoSeller" },
                Steps = new[]
                {
                    AOLauncher.Localization.Texts.Get("Text_gmimanager_sets_GMI_withdrawals_and_buy_55A3F3EAB39E"),
                    AOLauncher.Localization.Texts.Get("Text_Name_a_bag_Sell_stand_close_542B82F625F1"),
                },
                Actions = new[] { Act(AOLauncher.Localization.Texts.Get("Text_GMI_settings_EE703047DEEB"), "gmimanager"), Act(AOLauncher.Localization.Texts.Get("Text_Sell_E13D6A480CBD"), "sell") },
            },
        };

        internal static PlayStyle Find(string id) => All.FirstOrDefault(style => string.Equals(style.Id, id ?? None, StringComparison.OrdinalIgnoreCase)) ?? All[0];

        // Extra plugin ids ("pack:...") a style adds for a set of the given suite.
        internal static IEnumerable<string> Extras(string styleId, string suite)
        {
            var style = Find(styleId);
            var ids = string.Equals(suite, "AOSP", StringComparison.OrdinalIgnoreCase) ? style.AOSP : style.Z;
            return ids.Select(id => "pack:" + id);
        }

        // The style that applies to a launch: the team's when it was launched with one, else the default.
        internal static string For(LauncherSettings settings, LaunchPreset preset) =>
            preset != null ? (preset.PlayStyle ?? None) : (settings.DefaultPlayStyle ?? None);
    }
}
