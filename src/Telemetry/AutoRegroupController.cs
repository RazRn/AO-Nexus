using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Movement;
using NexusShortcuts;

namespace NexusTelemetry
{
    internal sealed partial class SyncMovementGuard
    {
        AutoRegroupPermission regroupPermission;
        readonly AutoRegroupProgress regroupProgress = new AutoRegroupProgress();
        readonly long processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        FormationMovementLease regroupLease;
        internal Func<AutoRegroupPermission, bool> LeaderAliveOverride;
        double lastRegroupAttempt = -100;
        bool regroupBlocked;
        string blockedMessage;

        // Called by the existing telemetry I/O worker; game APIs stay on Game.OnUpdate.
        internal void SetRegroupPermission(AutoRegroupPermission permission)
            => Volatile.Write(ref regroupPermission, permission);

        AutoRegroupPermission Permission()
        {
            var permission = Volatile.Read(ref regroupPermission);
            if (permission == null || !permission.Fresh(Stopwatch.GetTimestamp(), pid, processStart)
                || permission.Character != Game.ClientInst || permission.Leader != owner
                || permission.Area != area || permission.Channel != ownerChannel) return null;
            return permission;
        }

        bool TryRegroup(double now, double distance)
        {
            if (Permission() == null || now - lastRegroupAttempt < 30) return false;
            lastRegroupAttempt = now;
            try {
                regroupLease = new FormationMovementLease();
                regroupProgress.Start(now, distance);
                regroupBlocked = false;
                pendingBeacon = false;
                blockedMessage = null;
                SetRegroupStatus("Regrouping", distance);
                return true;
            } catch (Exception ex) {
                EndRegroup();
                Trace.TraceWarning("Automatic regroup could not acquire movement: {0}", ex.Message);
                return false;
            }
        }

        bool UpdateRegroup(double now)
        {
            if (regroupLease == null) return false;
            var player = DynelManager.LocalPlayer;
            if (Game.IsZoning || player == null || !player.IsAlive || Foreground
                || Permission() == null || !regroupLease.Intact || settings["IPCChannel"].AsInt32() != ownerChannel
                || Playfield.Identity.Instance != area || MovementController.Instance.IsNavigating
                || player.IsAttacking || Spell.HasPendingCast
                || DynelManager.Characters.Any(c => c != null && c.IsAlive && c.IsAttacking && c.FightingTarget?.Identity == player.Identity)) {
                EndRegroup();
                owner = 0;
                policy.Reset();
                resumeAfter = now + 3;
                SetRegroupStatus("Regroup stopped: control or client state changed", eventDistance);
                pendingBeacon = false;
                blockedMessage = null;
                return true;
            }

            var permission = Permission();
            if (permission == null || !LeaderAlive(permission)) {
                EndRegroup();SetRegroupStatus("Regroup stopped: leader closed", eventDistance);owner = 0;return true;
            }
            var leader = DynelManager.Players.FirstOrDefault(p => p.Identity.Instance == owner);
            if (leader == null) { BlockRegroup("Leader no longer visible", eventDistance);return true; }
            double distance = player.Position.Distance2DFrom(leader.Position);
            double limit = 2 * (1 + Math.Max(0, player.GetStat(Stat.RunSpeed)) / 100.0);
            double arrival = Math.Max(.75, Math.Min(2, limit - .5));
            if (regroupBlocked) {
                // Keep the receiver suspended after a failed return. Manual focus, turning
                // the option off, or arriving back together releases this hold.
                if (distance <= arrival) CompleteRegroup(now, distance);
                return true;
            }
            bool clear = Math.Abs(player.Position.Y - leader.Position.Y) <= 5
                && Playfield.LineOfSight(player.Position + new Vector3(0, 1, 0), leader.Position + new Vector3(0, 1, 0));
            string result = regroupProgress.Observe(now, distance, arrival, clear);
            if (result == "Regrouped") { CompleteRegroup(now, distance);return true; }
            if (result != "Regrouping") { BlockRegroup(result, distance);return true; }
            int pace = (int)player.MovementState;
            if (pace != 2 && pace != 3) { BlockRegroup("Cannot walk or run", distance);return true; }
            MovementController.Instance.LookAt(leader.Position);
            MovementController.Instance.SetMovement(MovementAction.ForwardStart);
            MovementController.Instance.SetMovement(MovementAction.Update);
            status = "Regrouping";
            eventDistance = distance;
            return true;
        }

        bool LeaderAlive(AutoRegroupPermission permission)
        {
            if (LeaderAliveOverride != null) return LeaderAliveOverride(permission);
            try {
                using (var process = Process.GetProcessById(permission.LeaderPid))
                    return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == permission.LeaderStartTicks;
            } catch (ArgumentException) { return false; }
        }

        void BlockRegroup(string reason, double distance)
        {
            if (regroupBlocked) return;
            regroupBlocked = true;
            MovementController.Instance.SetMovement(MovementAction.FullStop);
            SetRegroupStatus("Regroup blocked: " + reason, distance);
            blockedMessage = reason == "No progress toward leader" || reason == "Path obstructed"
                ? "Stuck while regrouping" : "Regroup stopped: " + reason;
            pendingBeacon = true;
        }

        void CompleteRegroup(double now, double distance)
        {
            EndRegroup();
            pendingBeacon=false;blockedMessage=null;
            policy.Reset();resumeAfter = now + 3;
            SetRegroupStatus("Regrouped", distance);
        }

        void SetRegroupStatus(string value, double distance)
        {
            status = eventKind = value;eventQpc = Stopwatch.GetTimestamp();eventDistance = distance;eventLeader = owner;
        }

        void EndRegroup()
        {
            if (regroupLease == null) return;
            try { MovementController.Instance.SetMovement(MovementAction.FullStop); }
            finally { var lease = regroupLease;regroupLease = null;lease.Dispose(); }
        }
    }
}
