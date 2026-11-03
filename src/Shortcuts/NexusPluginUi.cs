using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Common.Unmanaged.Interfaces;

namespace NexusShortcuts
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct UiState
    {
        public uint Size, Version, Flags, Values, Channel;
        public float Tick;
        public uint Profile;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Character;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct UiCommand { public uint Size, Kind, Index, Value; public float Scalar; }
    internal interface ISyncUiModel
    {
        uint Values { get; }
        int Channel { get; set; }
        float Tick { get; set; }
        string Character { get; }
        uint Profile { get; }
        void Set(uint index, bool value);
        void Save();
        void CloseClassic();
    }
    internal interface IUiTransport { bool Exchange(ref UiState state, ref UiCommand command); }
    internal sealed class NativeUiTransport : IUiTransport
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ExchangeFn(ref UiState state, ref UiCommand command);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        readonly ExchangeFn exchange;
        internal bool Available => exchange != null;
        internal NativeUiTransport()
        {
            var module = GetModuleHandle("randy31.dll");
            var address = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "NexusUi_Exchange");
            if (address != IntPtr.Zero) exchange = (ExchangeFn)Marshal.GetDelegateForFunctionPointer(address, typeof(ExchangeFn));
        }
        public bool Exchange(ref UiState state, ref UiCommand command) => exchange != null && exchange(ref state, ref command) == 1;
    }
    // Kept independent of AOSharp/native calls for fixtures. Changes run only on AOSharp's update thread.
    internal sealed class SyncUiSession
    {
        readonly ISyncUiModel model;
        readonly IUiTransport transport;
        internal bool Modern = true;
        internal Action Classic, PersistPreference, OpenTools;
        internal SyncUiSession(ISyncUiModel model, IUiTransport transport) { this.model = model; this.transport = transport; }
        UiState Snapshot(uint flags) => new UiState { Size = 92, Version = 1, Flags = flags, Values = model.Values,
            Channel = (uint)model.Channel, Tick = model.Tick, Profile = model.Profile, Character = model.Character };
        internal bool Open()
        {
            if (!Modern) return false;
            var state = Snapshot(3); var command = new UiCommand { Size = 20 };
            if (!transport.Exchange(ref state, ref command)) { Close(); return false; }
            model.CloseClassic();
            Apply(command);
            return true;
        }
        internal void Close()
        {
            var state = new UiState { Size = 92, Version = 1, Flags = 4, Character = "" };
            var command = new UiCommand { Size = 20 }; transport.Exchange(ref state, ref command);
        }
        internal void Update()
        {
            if (!Modern) return;
            for (int i = 0; i < 16; i++)
            {
                var state = Snapshot(1); var command = new UiCommand { Size = 20 };
                if (!transport.Exchange(ref state, ref command) || command.Kind == 0) break;
                Apply(command);
                if (!Modern) break;
            }
        }
        void Apply(UiCommand command)
        {
            if (command.Size != 20) return;
            switch (command.Kind)
            {
                case 1: if (command.Index < 12 && command.Value <= 1) model.Set(command.Index, command.Value != 0); break;
                case 2: if (command.Value <= 255) model.Channel = (int)command.Value; break;
                case 3: if (!float.IsNaN(command.Scalar) && command.Scalar >= .02f && command.Scalar <= 1f) model.Tick = command.Scalar; break;
                case 4: model.Save(); ChatMessage?.Invoke("SyncManager settings saved."); break;
                case 6: OpenTools?.Invoke(); break;
                case 5: Modern = false; Close(); PersistPreference?.Invoke(); Classic?.Invoke(); break;
            }
        }
        internal Action<string> ChatMessage;
    }
    internal sealed class SyncUiModel : ISyncUiModel
    {
        internal static readonly string[] Keys = { "SyncTarget", "SyncCombat", "SyncMove", "SyncSitting", "OpenBags", "SyncUse",
            "SyncEquip", "SyncNPCChat", "SyncNPCTrade", "SyncYalm", "SyncNanos", "SyncEmote" };
        readonly Settings settings;
        readonly Type plugin;
        readonly object config, characterConfig;
        internal readonly string PreferencePath;
        internal SyncUiModel(object instance)
        {
            plugin = instance.GetType();
            settings = (Settings)plugin.GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance);
            config = plugin.GetProperty("Config").GetValue(null, null);
            var characters = (IDictionary)config.GetType().GetProperty("CharSettings").GetValue(config, null);
            characterConfig = characters[Game.ClientInst];
            PreferencePath = Path.Combine(Preferences.GetCharacterPath(), "AOSharp", "NexusUI.mode");
            uint hash = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(PreferencePath.ToUpperInvariant())) hash = (hash ^ b) * 16777619;
            Profile = hash;
            if (settings == null || characterConfig == null) throw new InvalidOperationException("SyncManager is not ready.");
        }
        public uint Profile { get; }
        public string Character => DynelManager.LocalPlayer?.Name ?? "Character";
        public uint Values
        {
            get { uint bits = 0; for (int i = 0; i < Keys.Length; i++) if (settings[Keys[i]].AsBool()) bits |= 1u << i; return bits; }
        }
        public int Channel
        {
            get => (int)characterConfig.GetType().GetProperty("IPCChannel").GetValue(characterConfig, null);
            set
            {
                characterConfig.GetType().GetProperty("IPCChannel").SetValue(characterConfig, value, null);
                plugin.GetMethod("IPCChannel_Changed").Invoke(null, new object[] { null, value });
                RefreshClassicText("ChannelBox", value.ToString());
            }
        }
        public float Tick
        {
            get => (float)characterConfig.GetType().GetProperty("Tick").GetValue(characterConfig, null);
            set
            {
                characterConfig.GetType().GetProperty("Tick").SetValue(characterConfig, value, null);
                plugin.GetMethod("Tick_Changed").Invoke(null, new object[] { null, value });
                RefreshClassicText("TickBox", value.ToString());
            }
        }
        Window ClassicWindow => (Window)plugin.Assembly.GetType("SyncManager.SettingsController").GetField("settingsWindow").GetValue(null);
        void RefreshClassicText(string name, string value)
        {
            var window = ClassicWindow;
            if (window != null && window.IsValid && window.FindView(name, out TextInputView input) && input != null) input.Text = value;
        }
        public void CloseClassic() { var window = ClassicWindow; if (window != null && window.IsValid) window.Close(); }
        public void Set(uint index, bool value) { settings[Keys[index]] = value; }
        public void Save() { settings.Save(); config.GetType().GetMethod("Save").Invoke(config, null); }
    }
    internal sealed class NexusPluginUi : IDisposable
    {
        readonly Dictionary<string, Action<string, string[], ChatWindow>> commands;
        readonly NativeUiTransport transport = new NativeUiTransport();
        Action<string, string[], ChatWindow> original, wrapper, modeCommand;
        SyncUiSession session;
        string preferencePath;
        bool disabled;
        internal Action OpenTools;
        float discover, refresh;
        internal NexusPluginUi(Dictionary<string, Action<string, string[], ChatWindow>> commands)
        {
            this.commands = commands;
            if (!transport.Available) { disabled = true; return; }
            modeCommand = Mode;
            if (!commands.ContainsKey("nexusui")) Chat.RegisterCommand("nexusui", modeCommand);
        }
        void Persist()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(preferencePath));
            File.WriteAllText(preferencePath, session.Modern ? "modern" : "classic");
        }
        void Mode(string command, string[] args, ChatWindow output)
        {
            if (session == null) { Chat.WriteLine("Nexus UI: load the Z SyncManager plugin first."); return; }
            if (args.Length != 1 || (args[0] != "modern" && args[0] != "classic")) {
                Chat.WriteLine("Use /nexusui modern or /nexusui classic. /syncmanager opens settings."); return;
            }
            session.Modern = args[0] == "modern";
            try { Persist(); if (!session.Modern) session.Close(); wrapper("syncmanager", new string[0], output); }
            catch (Exception ex) { Fail(ex); }
        }
        internal void Update(float delta)
        {
            if (disabled) return;
            try
            {
                if (Game.IsZoning) { session?.Close(); return; }
                refresh += delta;
                if (refresh < .1f) return;
                float elapsed = refresh; refresh = 0;
                if (session != null && (!commands.TryGetValue("syncmanager", out var current) || current != wrapper)) {
                    session.Close(); session = null; original = null; wrapper = null;
                }
                discover += elapsed;
                if (session == null && discover >= 1) { discover = 0; Discover(); }
                session?.Update();
            }
            catch (Exception ex) { Fail(ex); }
        }
        void Discover()
        {
            if (!commands.TryGetValue("syncmanager", out original)) return;
            object instance = Game.OnUpdate?.GetInvocationList().Select(d => d.Target).FirstOrDefault(o =>
                o != null && o.GetType().FullName == "SyncManager.SyncManager" && o.GetType().Assembly.GetName().Name == "SyncManager");
            if (instance == null) return;
            var model = new SyncUiModel(instance);
            preferencePath = model.PreferencePath;
            session = new SyncUiSession(model, transport) {
                Modern = !File.Exists(preferencePath) || File.ReadAllText(preferencePath).Trim() != "classic",
                OpenTools = () => OpenTools?.Invoke(), PersistPreference = Persist, ChatMessage = text => Chat.WriteLine(text),
                Classic = () => original("syncmanager", new string[0], null)
            };
            wrapper = (command, args, output) => {
                if (Game.IsZoning) { Chat.WriteLine("Open SyncManager after zoning finishes."); return; }
                try { if (args.Length == 0 && session.Open()) return; }
                catch (Exception ex) { Fail(ex); }
                original(command, args, output);
            };
            commands["syncmanager"] = wrapper;
        }
        void Fail(Exception error)
        {
            disabled = true;
            try { session?.Close(); } catch (Exception) { }
            if (wrapper != null && commands.TryGetValue("syncmanager", out var current) && current == wrapper) commands["syncmanager"] = original;
            Chat.WriteLine("Nexus UI unavailable; use /syncmanager for the classic window. " + error.Message);
        }
        public void Dispose()
        {
            try { session?.Close(); } catch (Exception) { }
            if (wrapper != null && commands.TryGetValue("syncmanager", out var current) && current == wrapper) commands["syncmanager"] = original;
            if (commands.TryGetValue("nexusui", out var mode) && mode == modeCommand) commands.Remove("nexusui");
        }
    }
}
