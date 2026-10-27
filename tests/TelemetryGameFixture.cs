// This is an inert game-loop stand-in. It uses the REAL AOSharp IPC transport
// and the production plugin source; it never loads a game or injects anything.
using System;
using System.Diagnostics;
using System.Threading;
namespace AOSharp.Core
{
    public sealed class LocalPlayer { public bool IsFalling { get; set; } }
    public static class DynelManager { public static LocalPlayer LocalPlayer { get; } = new LocalPlayer(); }
    public static class MiscClientEvents {
        public static event EventHandler<AOSharp.Common.SharedEventArgs.AttemptingSpellCastEventArgs> AttemptingSpellCast;
        public static void Cast() { AttemptingSpellCast?.Invoke(null, null); }
    }
    public static class Network {
        public static event EventHandler<SmokeLounge.AOtomation.Messaging.Messages.N3Message> N3MessageSent;
        public static void Cast() { N3MessageSent?.Invoke(null, new SmokeLounge.AOtomation.Messaging.Messages.N3Messages.CharacterActionMessage {
            Action = SmokeLounge.AOtomation.Messaging.GameData.CharacterActionType.CastNano }); }
    }
    public abstract class AOPluginEntry { public virtual void Run() { } public virtual void Teardown() { } }
    public static class Game
    {
        public static event EventHandler<float> OnUpdate;
        public static bool IsZoning => false;
        public static void Tick() { OnUpdate?.Invoke(null, .01f); }
    }
}
namespace AOSharp.Core.UI
{
    public static class Chat {
        public static Action<string> FeedbackReceived;
        public static void Feedback() { FeedbackReceived?.Invoke("You can't execute nanoprograms while falling!"); }
    }
}
namespace AOSharp.Core.IPC
{
    public sealed class IPCChannel : IPCChannelBase
    {
        protected override int _localDynelId => Process.GetCurrentProcess().Id;
        public IPCChannel(byte channel) : base(channel) { }
        public static void Pump() => Update();
    }
}
namespace NexusTelemetryFixture
{
    static class Program
    {
        static string mode = "normal";
        static int Main()
        {
            var plugin = new NexusTelemetry.NexusHealthPlugin();
            plugin.Run();
            new Thread(() => { string line; while ((line = Console.ReadLine()) != null) Volatile.Write(ref mode, line); Volatile.Write(ref mode, "quit"); }) { IsBackground = true }.Start();
            using (var p = Process.GetCurrentProcess()) Console.WriteLine(p.Id + " " + p.StartTime.ToUniversalTime().Ticks);
            bool unloaded = false;
            while (true) {
                string state = Volatile.Read(ref mode);
                if (state == "crash") Environment.Exit(37); // Only this inert fixture exits.
                if (state == "quit") break;
                if (state == "unload" && !unloaded) { plugin.Teardown(); unloaded = true; }
                if (!unloaded) {
                    AOSharp.Core.DynelManager.LocalPlayer.IsFalling = state == "falling" || state == "falling-network";
                    if (state == "falling") AOSharp.Core.MiscClientEvents.Cast();
                    if (state == "falling-network") AOSharp.Core.Network.Cast();
                    if (state == "falling-feedback") AOSharp.Core.UI.Chat.Feedback();
                    if (state != "pause" && state != "ticks-only") AOSharp.Core.IPC.IPCChannel.Pump();
                    if (state != "pause" && state != "ipc-only") AOSharp.Core.Game.Tick();
                }
                Thread.Sleep(10);
            }
            if (!unloaded) plugin.Teardown();
            return 0;
        }
    }
}
