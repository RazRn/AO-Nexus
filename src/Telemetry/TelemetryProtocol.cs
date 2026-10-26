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
