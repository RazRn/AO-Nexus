namespace NexusTelemetry
{
    // Only game-thread observations; no remote memory or chat capture.
    public sealed class MovementSnapshot
    {
        public long Qpc { get; set; }
        public int CharacterId { get; set; }
        public int Playfield { get; set; }
        public string ZoneName { get; set; }
        public string GuardStatus { get; set; }
        public bool AutoRegroupSupported { get; set; }
        public string GuardEvent { get; set; }
        public long GuardEventQpc { get; set; }
        public double GuardDistance { get; set; }
        public int GuardLeader { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public int RunSpeed { get; set; }
        public string Adapter { get; set; }
        public string Error { get; set; }
        public bool Enabled { get; set; }
        public bool SendMovement { get; set; }
        public int Channel { get; set; } = -1;
        public int LastAcceptedSender { get; set; }
        public long LastMoveSentQpc { get; set; }
        public int[] VisiblePlayers { get; set; }
    }
}
