using System;
using NexusPlayTools;

static class SyncRefreshTests
{
    static int count;
    static void Check(bool result,string label){if(!result)throw new Exception(label);count++;}
    static SyncRefreshFrame Frame()=>new SyncRefreshFrame {Character=173,Pid=123,StartTicks=100,Sequence=1,Qpc=9800,Area=655,Channel=4,Moving=true,Qw=1};
    static SyncRefreshDecision Decide(SyncRefreshFrame f,double distance=1,bool moving=true,long previous=0,long armed=9000,int owner=173,int area=655,int channel=4)
        =>SyncRefreshPolicy.Decide(f,10000,1000,armed,previous,owner,area,channel,distance,0,4,moving);
    static void Main()
    {
        var f=Frame();Check(Decide(f)==SyncRefreshDecision.Correct,"Fresh nearby moving follower receives correction");
        Check(Decide(f,5)==SyncRefreshDecision.Ignore,"Moving frame cannot expand correction distance");
        Check(Decide(f,1,false)==SyncRefreshDecision.Ignore,"Refresh cannot restart stopped follower or undo guard stop");
        f.Moving=false;Check(Decide(f,60)==SyncRefreshDecision.Stop,"Fresh stop works outside correction range");
        Check(Decide(f,60,false)==SyncRefreshDecision.Ignore,"Already stopped follower stays stopped");
        Check(Decide(f,60,owner:170)==SyncRefreshDecision.Ignore,"Another sender cannot stop follower");
        Check(Decide(f,60,area:656)==SyncRefreshDecision.Ignore,"Stop from another zone rejected");
        Check(Decide(f,60,channel:5)==SyncRefreshDecision.Ignore,"Stop from another channel rejected");
        Check(Decide(f,60,armed:9900)==SyncRefreshDecision.Ignore,"Previous ownership frame rejected");
        Check(Decide(f,60,previous:9800)==SyncRefreshDecision.Ignore,"Duplicate frame rejected");
        Check(Decide(f,60,previous:9900)==SyncRefreshDecision.Ignore,"Out of order frame rejected");
        f.Qpc=10001;Check(Decide(f,60)==SyncRefreshDecision.Ignore,"Future frame rejected");
        f.Qpc=9200;Check(Decide(f,60)==SyncRefreshDecision.Ignore,"Stale stop rejected after stall");
        f=Frame();f.X=float.NaN;Check(Decide(f)==SyncRefreshDecision.Ignore,"NaN coordinate rejected");
        f=Frame();f.Qw=float.PositiveInfinity;Check(Decide(f)==SyncRefreshDecision.Ignore,"Infinite rotation rejected");
        f=Frame();f.Pid=0;Check(Decide(f)==SyncRefreshDecision.Ignore,"Missing publisher process rejected");
        f=Frame();f.Sequence=0;Check(Decide(f)==SyncRefreshDecision.Ignore,"Uninitialised mailbox rejected");
        Check(Decide(null)==SyncRefreshDecision.Ignore,"Missing snapshot ignored");
        Check(SyncRefreshPolicy.Decide(Frame(),10000,1000,9000,0,173,655,4,1,6,4,true)==SyncRefreshDecision.Ignore,"Different floor cannot be corrected");
        Check(Decide(Frame(),double.NaN)==SyncRefreshDecision.Ignore,"Invalid distance rejected");
        Check(SyncRefreshPolicy.ValidRate(0)&&SyncRefreshPolicy.ValidRate(5)&&SyncRefreshPolicy.ValidRate(10),"Only trial rates supported");
        Check(!SyncRefreshPolicy.ValidRate(-1)&&!SyncRefreshPolicy.ValidRate(100),"Invalid rates rejected");
        Console.WriteLine("PASS: "+count+" sync refresh freshness, ownership, range and stop checks.");
    }
}
