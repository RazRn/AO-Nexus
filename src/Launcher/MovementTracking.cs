using System;
using System.Diagnostics;
using System.Linq;
using NexusTelemetry;
using AOLauncher.Localization;

namespace AOLauncher
{
    internal sealed class MovementAssessment
    {
        internal string Code;
        internal int Severity;
        internal double? Distance,Limit;
        internal bool CanRegroup;
        internal string Text => Texts.Get("Movement."+Code)
            +(Distance.HasValue?" · "+Distance.Value.ToString("0.0")+" m":"");
    }
    internal static class MovementTracking
    {
        internal static bool Fresh(ClientObservation row,long now)
        {
            var m=row?.Pulse?.Movement;
            return m!=null && !row.ExitedUtc.HasValue && row.Pulse.Active
                && m.Qpc>0 && now>=m.Qpc && now-m.Qpc<=3*Stopwatch.Frequency
                && m.CharacterId>0 && (row.Client==null || row.Client.CharacterId<=0 || row.Client.CharacterId==m.CharacterId)
                && Finite(m.X) && Finite(m.Y) && Finite(m.Z);
        }
        static bool Finite(float n)=>!float.IsNaN(n) && !float.IsInfinity(n);
        internal static MovementAssessment Assess(ClientObservation row,ClientObservation driver,long now)
        {
            var result=new MovementAssessment {Code="Unavailable"};
            if(!Fresh(row,now))return result;
            var m=row.Pulse.Movement;
            if(row.Pulse.Zoning){result.Code="Zoning";return result;}
            if(m.Adapter!="AOSP"){result.Code="Unsupported";return result;}
            if(!m.Enabled){result.Code="Disabled";return result;}
            if(driver==null || !Fresh(driver,now)){result.Code="ChooseDriver";return result;}
            if(row.Pid==driver.Pid && row.StartTicks==driver.StartTicks) {
                result.Code=m.SendMovement?"Driver":"MoveOff";return result;
            }
            if(row.Client==null || driver.Client==null || row.Client.Server!=driver.Client.Server || row.Client.Port!=driver.Client.Port) {
                result.Code="OtherServer";return result;
            }
            var lead=driver.Pulse.Movement;
            if(lead.Adapter!="AOSP" || !lead.Enabled || !lead.SendMovement){result.Code="DriverOff";result.Severity=1;return result;}
            if(m.Channel<0 || m.Channel!=lead.Channel){result.Code="OtherChannel";result.Severity=2;return result;}
            if(driver.Pulse.Zoning || m.Playfield!=lead.Playfield){result.Code="OtherArea";result.Severity=2;return result;}
            double dx=(double)m.X-lead.X,dz=(double)m.Z-lead.Z;
            result.Distance=Math.Sqrt(dx*dx+dz*dz);
            result.Limit=2*(1+Math.Max(0,m.RunSpeed)/100.0);
            if(m.VisiblePlayers==null || !m.VisiblePlayers.Contains(lead.CharacterId)) {
                result.Code="LeaderNotVisible";result.Severity=2;return result;
            }
            result.CanRegroup=result.Distance<=60 && Math.Abs((double)m.Y-lead.Y)<=5 && !row.Pulse.Attacking;
            if(result.Distance>result.Limit){result.Code="BeyondRange";result.Severity=2;}
            else if(result.Distance>result.Limit*.75){result.Code="NearLimit";result.Severity=1;}
            else result.Code="InRange";
            return result;
        }
    }
}
