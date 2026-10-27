using System;
using System.Diagnostics;
using System.IO;
using AOSharp.Core.IPC;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace NexusTelemetry
{
    [AoContract(32002)]
    public sealed class HealthPing : IPCMessage
    {
        public override short Opcode => 32002;
        [AoMember(0)] public int Origin { get; set; }
        [AoMember(1)] public int Epoch { get; set; }
        [AoMember(2)] public int Sequence { get; set; }
        [AoMember(3)] public int Target { get; set; }
        [AoMember(4)] public int Reply { get; set; }
        [AoMember(5)] public long StartTicks { get; set; }
    }
    public sealed class Pulse
    {
        public int Version { get; set; } = 1;
        public int Pid { get; set; }
        public long StartTicks { get; set; }
        public string Instance { get; set; }
        public long PublishedQpc { get; set; }
        public long LastUpdateQpc { get; set; }
        public long Updates { get; set; }
        public double RecentMaxMs { get; set; }
        public double MaxMs { get; set; }
        public long Over100 { get; set; }
        public bool Zoning { get; set; }
        public bool Active { get; set; }
        public int ActionIssue { get; set; }
        public long ActionIssueQpc { get; set; }
        public int ActionIssueCount { get; set; }
        public bool ActionIssueConfirmed { get; set; }
    }
    // Only structured, recognized action issues leave the game; never chat text.
    public sealed class ActionIssueTracker
    {
        readonly Pulse pulse;
        long lastCountedQpc;
        public ActionIssueTracker(Pulse pulse) { this.pulse = pulse; }
        public void ObserveCast(bool falling, bool zoning, long now)
        {
            if (falling && !zoning) Record(now, false);
        }
        public void ObserveFeedback(string text, long now)
        {
            if (string.Equals(text?.Trim(), "You can't execute nanoprograms while falling!", StringComparison.OrdinalIgnoreCase))
                Record(now, true);
        }
        void Record(long now, bool confirmed)
        {
            if (now <= 0) return;
            long previous = pulse.ActionIssueQpc;
            bool sameBurst = previous > 0 && now >= previous && now - previous < 30 * Stopwatch.Frequency;
            if (!sameBurst) { pulse.ActionIssueCount = 0; pulse.ActionIssueConfirmed = false; }
            // The cast hook, outbound packet and feedback can describe one attempt.
            if (!sameBurst || now - lastCountedQpc >= Stopwatch.Frequency / 4) {
                pulse.ActionIssueCount = Math.Min(9999, pulse.ActionIssueCount + 1);
                lastCountedQpc = now;
            }
            pulse.ActionIssue = 1;
            pulse.ActionIssueQpc = now;
            pulse.ActionIssueConfirmed |= confirmed;
        }
    }
    public sealed class Target
    {
        public int Pid { get; set; }
        public long StartTicks { get; set; }
    }
    public sealed class PingResult
    {
        public int Pid { get; set; }
        public long StartTicks { get; set; }
        public long PublishedQpc { get; set; }
        public long LastReplyQpc { get; set; }
        public int Sent { get; set; }
        public int Replies { get; set; }
        public int Timeouts { get; set; }
        public int Pending { get; set; }
        public int Late { get; set; }
        public double LastMs { get; set; }
        public double MaxMs { get; set; }
    }
}
