using System;
using System.Collections.Generic;
using System.Linq;

namespace NexusPlayTools
{
    public sealed class FormationRequest
    {
        public string Action, Session;
        public int Leader, Slot, Channel, Playfield, Rate;
        public int[] Members;
        public float Spacing;
        public bool Valid()
        {
            Guid token;
            return Guid.TryParseExact(Session, "N", out token) && (Action == "follow" || Action == "gather" || Action == "circle" || Action == "funny")
                && Leader > 0 && Slot >= 0 && Slot <= 5 && Channel >= 0 && Channel <= 255 && Playfield > 0
                && Members != null && Members.Length >= 2 && Members.Length <= 6 && Members.All(x => x > 0)
                && Members.Distinct().Count() == Members.Length && Members.Contains(Leader) && Slot<Members.Length
                && !float.IsNaN(Spacing) && Spacing >= 1 && Spacing <= 10;
        }
    }
    public sealed class FormationReport
    {
        public int Version = 1, Pid, Character, Channel, Playfield;
        public long StartTicks, UtcTicks;
        public string Scope, Session, Status;
        public int[] Members;
        public bool Ready, Active;
        public bool CircleSupported;
        public bool FunnySupported, PaceSupported;
        public int EmotesPerformed, EmotesAvailable, LeaderPace, ActualPace;
        public string LastEmote;
        public SyncRefreshReport SyncRefresh;
        public bool Fresh(DateTime now) => UtcTicks <= now.Ticks && now.Ticks - UtcTicks < TimeSpan.TicksPerSecond * 3;
    }
    public static class FunnyBusiness
    {
        // The shipped client's SocialMenu.xml; names only, never arbitrary chat input.
        public static readonly string[] Emotes = ("adjust angry apachi applause backflip ballet blowkiss bow bulge chicken cross curt disco drink eat fblock fishsize flamenco flip giggle gloat greet italian itch kisshigh kisslow kneel laugh-b laugh-s legshake lookout lounge moon nod nono pointba pointfor pointlef pointrig pointup pray prostrate puke pulp read rocky salute scared scratch shake shrug slap speech spit strong1 strong2 strong3 strong4 surprised surrender swroyal thinker thumbs wave ymca").Split(' ');
        public static TrailPoint Offset(Random random, float radius)
        {
            if(random==null || float.IsNaN(radius) || radius<1 || radius>10)throw new ArgumentException("Invalid wander radius.");
            double angle=random.NextDouble()*Math.PI*2;
            // Leave room to brake before the outer boundary.
            double distance=Math.Sqrt(random.NextDouble())*radius*.8;
            return new TrailPoint((float)(Math.Sin(angle)*distance),0,(float)(Math.Cos(angle)*distance));
        }
        public static bool GroundPace(int state)=>state==2 || state==3;
        public static TrailPoint TurnAway(Random random,TrailPoint centre,TrailPoint position,TrailPoint blocked,float radius,Func<TrailPoint,bool> clear)
        {
            double dx=position.X-blocked.X,dz=position.Z-blocked.Z,length=Math.Sqrt(dx*dx+dz*dz);
            for(int attempt=0;attempt<13;attempt++) {
                TrailPoint offset;
                if(attempt==0 && length>.01) {
                    double x=position.X-centre.X+dx/length*Math.Min(2,radius),z=position.Z-centre.Z+dz/length*Math.Min(2,radius);
                    double distance=Math.Sqrt(x*x+z*z),scale=distance>radius*.8?radius*.8/distance:1;
                    offset=new TrailPoint((float)(x*scale),0,(float)(z*scale));
                } else offset=Offset(random,radius);
                var point=new TrailPoint(centre.X+offset.X,centre.Y,centre.Z+offset.Z);
                if(position.Distance(point)>.65 && clear(point))return point;
            }
            return null;
        }
        public static string EmoteAlias(string enumName)
        {
            foreach(var category in new[]{"Greeting","Gestures","Approval","Dislike","Dance","Athlete","Relaxing","Directions"})
                if(enumName.StartsWith(category,StringComparison.Ordinal))return enumName.Substring(category.Length).ToLowerInvariant();
            return null;
        }
    }
    public sealed class FormationTravel
    {
        TrailPoint previous;
        public double Distance {get;private set;}
        public void Reset(TrailPoint point){previous=point;Distance=0;}
        public void Observe(TrailPoint point){if(previous!=null)Distance+=previous.Distance(point);previous=point;}
    }
    public static class CircleFormation
    {
        // World-aligned slots avoid making followers orbit whenever the leader turns.
        // Divide by actual follower count, not six: the leader occupies the centre.
        public static TrailPoint Target(TrailPoint leader,float radius,int slot,int followers)
        {
            if(leader==null || followers<1 || followers>5 || slot<1 || slot>followers
                || float.IsNaN(radius) || radius<1 || radius>10)throw new ArgumentException("Invalid circle formation.");
            double angle=(slot-1)*2*Math.PI/followers;
            return new TrailPoint(leader.X+(float)Math.Sin(angle)*radius,leader.Y,leader.Z+(float)Math.Cos(angle)*radius);
        }
    }
    public sealed class TrailPoint
    {
        public float X, Y, Z;
        public TrailPoint() { }
        public TrailPoint(float x, float y, float z) { X=x; Y=y; Z=z; }
        public double Distance(TrailPoint other) => Math.Sqrt((X-other.X)*(X-other.X)+(Z-other.Z)*(Z-other.Z));
    }
    // Retain the leader's route, rather than cutting straight across corridor corners.
    public sealed class FormationTrail
    {
        public readonly List<TrailPoint> Points = new List<TrailPoint>();
        public void Add(TrailPoint point)
        {
            if (Points.Count > 0 && Points[Points.Count-1].Distance(point) < .4) return;
            Points.Add(point);
            if (Points.Count > 256) Points.RemoveAt(0);
        }
        public static TrailPoint Behind(TrailPoint[] trail, float distance, TrailPoint fallback)
        {
            if (trail == null || trail.Length < 2) return fallback;
            for (int i=trail.Length-1;i>0;i--) {
                var a=trail[i]; var b=trail[i-1]; double length=a.Distance(b);
                if (length >= distance && length > 0) {
                    float ratio=(float)(distance/length);
                    return new TrailPoint(a.X+(b.X-a.X)*ratio,a.Y+(b.Y-a.Y)*ratio,a.Z+(b.Z-a.Z)*ratio);
                }
                distance-=(float)length;
            }
            return trail[0];
        }
    }
}
