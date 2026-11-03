using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Common.Unmanaged.Interfaces;
using NexusPlayTools;

namespace NexusShortcuts
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct ToolsState
    {
        public uint Size, Version, Flags, Profile, Shape, Slot, Leader;
        public float Side, Row, Behind;
        public uint Walk, Hold, Mode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Character;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 160)] public string Status;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)] public string Timers;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct ToolsCommand
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Text;
    }
    internal sealed class NexusTools : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ExchangeFn(ref ToolsState state, ref ToolsCommand command);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        readonly Dictionary<string, Action<string, string[], ChatWindow>> commands;
        readonly Dictionary<string, Action<string, string[], ChatWindow>> registered = new Dictionary<string, Action<string, string[], ChatWindow>>();
        readonly ClockStore clocks = new ClockStore();
        readonly ExchangeFn exchange;
        readonly string scope, pref;
        readonly uint profile;
        readonly NexusFormation formation;
        readonly PanelFormationController panelFormation;
        readonly SyncRefreshController syncRefresh;
        internal string FormationReport => panelFormation.Report;
        internal string QueueFormation(string json) => panelFormation.Queue(json);
        readonly SyncFormationControls syncControls;
        readonly Dictionary<int, string> seenAlive = new Dictionary<int, string>();
        Window classic;
        List<BossClock> visible = new List<BossClock>();
        bool hud, useClassic;
        float elapsed, discovery, controlsElapsed;
        string error;
        internal NexusTools(Dictionary<string, Action<string, string[], ChatWindow>> commandTable)
        {
            commands = commandTable;
            pref = Path.Combine(Preferences.GetCharacterPath(), "AOSharp", "NexusTools.json");
            scope = ToolFiles.Scope(Preferences.GetCharacterPath());
            profile = uint.Parse(ToolFiles.Hash(pref).Substring(0, 8), NumberStyles.HexNumber);
            formation = new NexusFormation(scope);
            syncRefresh = new SyncRefreshController(scope);
            panelFormation = new PanelFormationController(scope) { BeforeStart = () => formation.Stop("Control Panel took control"),
                SetSyncRate=syncRefresh.Configure,ReadSyncRefresh=syncRefresh.Capture };
            var stored = ToolFiles.Read<PreferencesData>(pref, 8192); formation.Options = stored.Formation ?? new FormationOptions(); hud = stored.Hud; useClassic = stored.Classic;
            syncControls = new SyncFormationControls(formation, Save, Path.GetDirectoryName(pref));
            var module = GetModuleHandle("randy31.dll"); var address = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "NexusTools_Exchange");
            if (address != IntPtr.Zero) exchange = (ExchangeFn)Marshal.GetDelegateForFunctionPointer(address, typeof(ExchangeFn));
            foreach (string name in new[] { "nexustools", "nexustimers", "nexusformation", "nexusregroup" }) {
                if (commands.ContainsKey(name)) continue;
                Action<string, string[], ChatWindow> handler = (cmd, args, output) => {
                    try {
                        if(cmd=="nexusregroup") {
                            if(formation.Mode!=0 || panelFormation.Active)throw new InvalidOperationException("Stop formation mode before using ordinary regroup/follow.");
                            if(args.Length!=2 || !int.TryParse(args[0],out int leader) || !int.TryParse(args[1],out int channel))
                                throw new ArgumentException("Use Regroup in the Nexus Control Panel.");
                            var roster = ClientTeamRoster.Capture();
                            Chat.WriteLine("Regroup: "+NexusTelemetry.SyncMovementProbe.Regroup(leader,channel,
                                roster.Ready && roster.InTeam ? roster.Members : null));
                        }
                        else if (args.Length == 0 && cmd == "nexusformation") OpenSync(); else if (args.Length == 0) Open(); else Execute((cmd == "nexusformation" ? "formation " : cmd == "nexustimers" ? "timer " : "") + string.Join(" ", args));
                    }
                    catch (Exception ex) { Chat.WriteLine("Nexus tools: " + ex.Message); }
                };
                Chat.RegisterCommand(name, handler); registered[name] = handler;
            }
        }
        sealed class PreferencesData { public FormationOptions Formation { get; set; } public bool Hud { get; set; } public bool Classic { get; set; } }
        void Save() => ToolFiles.Write(pref, new PreferencesData { Formation = formation.Options, Hud = hud, Classic = useClassic });
        void OpenSync()
        {
            syncControls.Update();
            if (commands.TryGetValue("syncmanager",out var open)) open("syncmanager",new string[0],null);
            else Chat.WriteLine("Load SyncManager, then open /syncmanager. Formation is below Movement.");
        }
        internal void Open()
        {
            if (Game.IsZoning) { Chat.WriteLine("Open Nexus tools after zoning."); return; }
            Refresh();
            if (!useClassic && exchange != null && Send(3)) return;
            OpenClassic();
        }
        internal void Update(float delta)
        {
            panelFormation.Update();
            syncRefresh.Update(panelFormation.Active || formation.Mode!=0);
            controlsElapsed += delta;
            if (controlsElapsed >= .2f) { controlsElapsed = 0; try { syncControls.Update(); } catch (Exception ex) { error = ex.Message; } }
            int previousMode = formation.Mode; formation.Update();
            if (formation.Mode != previousMode) syncControls.RefreshEnabled();
            if (Game.IsZoning) { seenAlive.Clear(); Send(4); return; }
            elapsed += delta; discovery += delta;
            if (discovery >= 1) {
                discovery = 0;
                try { Refresh(); Observe(); UpdateClassic(); } catch (Exception ex) { error = ex.Message; }
            }
            if (elapsed < .1f) return; elapsed = 0;
            try { Send(1); } catch (Exception ex) { error = ex.Message; }
        }
        bool Send(uint flags)
        {
            if (exchange == null) return false;
            var o = formation.Options;
            var state = new ToolsState { Size = 2324, Version = 1, Flags = flags | (hud && !useClassic ? 8u : 0) | (syncControls.Ready ? 16u : 0), Profile = profile,
                Shape = (uint)o.Shape, Slot = (uint)o.Slot, Leader = (uint)o.Leader, Side = o.Side, Row = o.Row, Behind = o.Behind,
                Walk = o.Walk ? 1u : 0, Hold = o.Hold ? 1u : 0, Mode = (uint)formation.Mode,
                Character = DynelManager.LocalPlayer?.Name ?? "", Status = (error ?? formation.Status).Substring(0, Math.Min(150, (error ?? formation.Status).Length)),
                Timers = string.Join("\n", visible.Take(8).Select(t => t.Id + "\t" + t.Name + "\t" + t.Status(DateTime.UtcNow))) };
            var command = new ToolsCommand { Size = 260, Text = "" };
            bool available = exchange(ref state, ref command) == 1;
            if (command.Size == 260 && !string.IsNullOrWhiteSpace(command.Text)) {
                try { Execute(command.Text); error = null; } catch (Exception ex) { error = ex.Message; }
            }
            return available;
        }
        void Refresh() => visible = clocks.Read().Timers.Where(t => t.Scope == scope && (t.Character == 0 || t.Character == Game.ClientInst)).OrderBy(t => t.DueUtcTicks == 0 ? long.MaxValue : t.DueUtcTicks).ToList();
        static float Number(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        void Execute(string text)
        {
            var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            if (text == "classic") { useClassic = true; hud = false; Save(); Send(4); OpenClassic(); return; }
            if (text == "modern") { useClassic = false; Save(); Open(); return; }
            if (text == "hud") { hud = !hud; Save(); return; }
            if (text == "sync") { OpenSync(); return; }
            if (parts[0] == "formation") {
                if (parts.Length == 5 && parts[1] == "team") { syncControls.Configure(parts[4] == "1", int.Parse(parts[2]), Number(parts[3])); return; }
                if (parts.Length < 2) { Open(); return; }
                if (parts[1] == "stop") { panelFormation.Stop("Stopped by user"); formation.Stop("Stopped by user"); return; }
                if (panelFormation.Active) throw new InvalidOperationException("Stop the Control Panel formation first.");
                if (parts[1] == "lead" || parts[1] == "join") { formation.Start(parts[1] == "lead"); Save(); return; }
                if (parts[1] == "set" && parts.Length == 10) {
                    var o = new FormationOptions { Shape = (FormationShape)int.Parse(parts[2]), Side = Number(parts[3]), Row = Number(parts[4]), Behind = Number(parts[5]),
                        Leader = int.Parse(parts[6]), Slot = int.Parse(parts[7]), Walk = parts[8] == "1", Hold = parts[9] == "1" };
                    o.Validate(); formation.Stop("Settings changed; start when ready"); formation.Options = o; Save(); return;
                }
                throw new ArgumentException("Use formation lead/join/stop or set <shape 0-5> <side m> <row m> <behind m> <leader ID> <slot> <walk 0/1> <hold 0/1>.");
            }
            if (parts[0] == "timer") {
                if (parts.Length >= 5 && parts[1] == "add") {
                    TimerKind kind;
                    if (!Enum.TryParse(parts[2], true, out kind)) throw new ArgumentException("Timer kind: Fixed, ChanceWindow, Personal or Reset.");
                    var timer = new BossClock { Name = string.Join(" ", parts.Skip(4)), Scope = scope, Kind = kind, Seconds = Number(parts[3]) * 60,
                        Character = kind == TimerKind.Personal ? Game.ClientInst : 0 };
                    timer.WarningSeconds = Math.Min(600, timer.Seconds / 2); timer.Validate(); timer.Start(DateTime.UtcNow, "Started manually by " + DynelManager.LocalPlayer.Name);
                    clocks.Edit(data => data.Timers.Add(timer));
                } else if (parts.Length == 3 && new[] { "start", "seen", "stop", "observe" }.Contains(parts[1])) {
                    clocks.Edit(data => {
                        var timer = data.Timers.Single(t => t.Id == parts[2] && t.Scope == scope && (t.Character == 0 || t.Character == Game.ClientInst));
                        if (parts[1] == "start") timer.Start(DateTime.UtcNow, "Kill/start entered manually by " + DynelManager.LocalPlayer.Name);
                        else if (parts[1] == "stop") { timer.DueUtcTicks = 0; timer.Evidence = "Stopped manually"; }
                        else if (parts[1] == "observe") timer.ObserveDeaths = !timer.ObserveDeaths;
                        else { timer.Evidence = "Seen alive (manual) by " + DynelManager.LocalPlayer.Name + " at " + DateTime.UtcNow.ToString("u"); timer.DueUtcTicks = 0; }
                    });
                } else throw new ArgumentException("Use timer add <Fixed/ChanceWindow/Personal/Reset> <minutes> <name>, or start/seen/stop/observe <timer ID>.");
                Refresh(); UpdateClassic(); return;
            }
            throw new ArgumentException("Unknown Nexus tools action.");
        }
        void Observe()
        {
            // Exact-name opt-in; disappearance never counts as a kill. We must see
            // the same identity alive, then present with health <= 0 in this playfield.
            var candidates = visible.Where(t => t.ObserveDeaths && t.Kind != TimerKind.Personal && t.Kind != TimerKind.Reset).ToArray();
            if (candidates.Length == 0) { seenAlive.Clear(); return; }
            var npcs = DynelManager.NPCs.Where(n => candidates.Any(t => t.Name == n.Name)).ToArray();
            var present = new HashSet<int>();
            foreach (var npc in npcs) {
                int id = npc.Identity.Instance; present.Add(id);
                if (npc.IsAlive) { seenAlive[id] = npc.Name; continue; }
                if (npc.Health > 0 || !seenAlive.TryGetValue(id, out var name) || name != npc.Name) continue;
                seenAlive.Remove(id);
                string observation = Playfield.Identity.Instance + ":" + id + ":" + name;
                clocks.Edit(data => {
                    foreach (var timer in data.Timers.Where(t => t.Scope == scope && t.ObserveDeaths && t.Name == name)) {
                        if (timer.LastObservation == observation && DateTime.UtcNow.Ticks - timer.ObservedUtcTicks < TimeSpan.TicksPerMinute * 2) continue;
                        timer.LastObservation = observation; timer.ObservedUtcTicks = DateTime.UtcNow.Ticks;
                        timer.Start(DateTime.UtcNow, "Death observed by " + DynelManager.LocalPlayer.Name + " (kill ownership unknown)");
                    }
                });
            }
            foreach (int id in seenAlive.Keys.Where(id => !present.Contains(id)).ToArray()) seenAlive.Remove(id);
        }
        void OpenClassic()
        {
            if (classic != null && classic.IsValid) { classic.Show(true); return; }
            string file = Path.Combine(Path.GetDirectoryName(pref), "NexusTools.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string xml = "<root><View view_layout=\"vertical\" layout_borders=\"Rect(8,8,8,8)\">" +
                "<Button name=\"Sync\" label=\"SyncManager - Movement / Formation\"/>" +
                "<TextView value=\"Timers: timer add Fixed 60 Boss name (minutes)\"/><TextInputView name=\"Command\" min_size=\"Point(400,25)\"/>" +
                "<Button name=\"Apply\" label=\"Run timer command\"/><TextView name=\"Timers\" value=\"No timers\"/>" +
                "<Button name=\"Modern\" label=\"Use AO-VK tools if available\"/></View></root>";
            File.WriteAllText(file, xml);
            classic = Window.CreateFromXml("Nexus timers", file);
            if (classic == null) throw new IOException("Could not create classic Nexus tools window.");
            Bind("Sync", OpenSync);
            Bind("Modern", () => { classic.Close(); Execute("modern"); });
            Bind("Apply", () => { if (classic.FindView("Command", out TextInputView input)) Execute(input.Text); });
            UpdateClassic();
        }
        void Bind(string name, Action action)
        {
            if (classic.FindView(name, out Button button)) button.Clicked = (s, e) => { try { action(); error = null; UpdateClassic(); } catch (Exception ex) { Chat.WriteLine("Nexus tools: " + ex.Message); } };
        }
        void UpdateClassic()
        {
            if (classic == null || !classic.IsValid) return;
            if (classic.FindView("Status", out TextView status)) status.Text = error ?? formation.Status;
            if (classic.FindView("Timers", out TextView timers)) timers.Text = string.Join("\n", visible.Select(t => t.Name + " - " + t.Status(DateTime.UtcNow) + "\n" + t.Id));
        }
        public void Dispose()
        {
            syncRefresh.Dispose(); panelFormation.Dispose(); syncControls.Dispose(); formation.Dispose(); Send(4); if (classic != null && classic.IsValid) classic.Close();
            foreach (var entry in registered) if (commands.TryGetValue(entry.Key, out var current) && current == entry.Value) commands.Remove(entry.Key);
        }
    }
}
