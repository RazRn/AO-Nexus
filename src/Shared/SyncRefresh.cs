using System;

namespace NexusPlayTools
{
    public sealed class SyncRefreshFrame
    {
        public int Version=1, Character, Pid, Area, Channel;
        public long StartTicks, Qpc, Sequence;
        public bool Moving;
        public float X,Y,Z,Qx,Qy,Qz,Qw;
    }
    public sealed class SyncRefreshReport
    {
        public int Rate;
        public string Status="Off";
        public long NativeSent,NativeReceived,Published,Applied,Stops,Rejected,IoErrors;
        public double SendGapMs,ReceiveGapMs,MaxSendGapMs,MaxReceiveGapMs,SendAgeMs,ReceiveAgeMs,FrameAgeMs,Distance;
    }
    public enum SyncRefreshDecision { Ignore, Correct, Stop }
    public static class SyncRefreshPolicy
    {
        public static bool ValidRate(int rate)=>rate==0 || rate==5 || rate==10;
        public static SyncRefreshDecision Decide(SyncRefreshFrame frame,long now,long frequency,
            long armedAt,long lastFrame,int owner,int area,int channel,double distance,double height,double limit,bool moving)
        {
            if(frame==null || frame.Version!=1 || frame.Pid<=0 || frame.StartTicks<=0 || frame.Sequence<=0
                || frame.Character!=owner || frame.Area!=area || frame.Channel!=channel || frequency<=0
                || frame.Qpc<armedAt || frame.Qpc<=lastFrame || frame.Qpc>now || (now-frame.Qpc)/(double)frequency>.75
                || !Finite(frame.X) || !Finite(frame.Y) || !Finite(frame.Z) || !Finite(frame.Qx)
                || !Finite(frame.Qy) || !Finite(frame.Qz) || !Finite(frame.Qw)
                || double.IsNaN(distance) || double.IsInfinity(distance) || distance<0)return SyncRefreshDecision.Ignore;
            // Stop has no coordinate write and is deliberately independent of correction range.
            if(!frame.Moving)return moving?SyncRefreshDecision.Stop:SyncRefreshDecision.Ignore;
            // Never restart a stopped client or bypass the separation guard to catch up.
            return moving && distance<=limit && Math.Abs(height)<=5 ? SyncRefreshDecision.Correct:SyncRefreshDecision.Ignore;
        }
        static bool Finite(float value)=>!float.IsNaN(value) && !float.IsInfinity(value);
    }
}
