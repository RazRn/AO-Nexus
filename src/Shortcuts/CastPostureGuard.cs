using System;
using System.Diagnostics;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Movement;
using AOSharp.Core.UI;
using NexusClientRecovery;

namespace NexusShortcuts
{
    internal sealed class CastPostureGuard : IDisposable
    {
        readonly CastPostureRecovery recovery = new CastPostureRecovery();
        bool zoning;
        bool failed;
        internal int Rejections => recovery.Rejections;
        internal int Corrections => recovery.Corrections;

        internal CastPostureGuard() { Chat.FeedbackReceived += Feedback; }

        internal void Update()
        {
            bool current = Game.IsZoning;
            if (current && !zoning) recovery.ResetZone();
            zoning = current;
        }

        void Feedback(string text)
        {
            // This callback is dispatched by AO# on the game update thread.
            try
            {
                if (Game.IsZoning || failed) return;
                var player = DynelManager.LocalPlayer;
                var movement = MovementController.Instance;
                bool standing = player != null && player.Health > 0 && player.MovementStatePermitsCasting
                    && movement != null;
                if (!recovery.Observe(text, (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency, standing)) return;
                // LeaveSit is an explicit StandUp action, not a toggle. It cannot sit the player.
                movement.SetMovement(MovementAction.LeaveSit);
                Trace.TraceWarning("Nano posture mismatch: requested explicit standing state (attempt {0}).", Corrections);
            }
            catch (Exception ex)
            {
                failed = true;
                Trace.TraceWarning("Nano posture recovery disabled: {0}", ex.GetType().Name);
            }
        }

        public void Dispose() { Chat.FeedbackReceived -= Feedback; }
    }
}
