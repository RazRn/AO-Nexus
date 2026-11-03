using System;
using System.Globalization;
using System.Linq;

namespace NexusTelemetry
{
    internal static class RallyLink
    {
        static string Safe(string text) => new string((text??"").Where(c=>!char.IsControl(c) && c!='<' && c!='>' && c!='\"' && c!='\'' && c!='&').Take(80).ToArray());
        internal static string Create(string name,string zone,double x,double z,int area,bool stopped)
            => Create(name, zone, x, z, area, stopped ? "Stopped" : "Rally");
        internal static string Create(string name,string zone,double x,double z,int area,string reason)
        {
            if(double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(z) || double.IsInfinity(z) || area<=0)return null;
            string coordinates=Math.Round(x).ToString(CultureInfo.InvariantCulture)+" "+Math.Round(z).ToString(CultureInfo.InvariantCulture);
            return "Nexus: "+Safe(reason)+" - "+"<a href=\"text://<font color='#50D5DD'>"+Safe(name)
                +"</font><br>"+Safe(zone)+"<br>"+coordinates+"<br><a href='chatcmd:///waypoint "+coordinates+" "+area
                +"'>Mark location on map</a>\">"+Safe(name)+" / "+Safe(zone)+"</a>";
        }
    }
}
