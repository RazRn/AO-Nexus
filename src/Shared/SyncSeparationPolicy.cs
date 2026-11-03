using System;

namespace NexusTelemetry
{
    // Monotonic seconds, sampled on the game thread. No action from UI sample skew.
    internal sealed class SyncSeparationPolicy
    {
        double? separatedSince;
        double? lastObservation;
        bool warned, stopped;
        internal void Reset() { separatedSince=null; lastObservation=null; warned=false; stopped=false; }
        internal string Observe(double now, double distance, double syncLimit, bool moving)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || double.IsNaN(distance) || double.IsInfinity(distance) || distance<0) { Reset(); return null; }
            // A frozen game thread supplies no evidence about the intervening distance.
            // Require a fresh continuous interval after a sampling gap; keep the episode's
            // warning/stop latch so recovery cannot produce repeated alarms or stops.
            if (lastObservation.HasValue && (now-lastObservation.Value>1 || now<lastObservation.Value)) separatedSince=null;
            lastObservation=now;
            if (distance<=syncLimit+1) { Reset(); return null; }
            if (distance<Math.Max(8,syncLimit+3)) { separatedSince=null; return null; }
            if (!separatedSince.HasValue) separatedSince=now;
            double elapsed=now-separatedSince.Value;
            if (elapsed>=4 && distance>=Math.Max(15,syncLimit+8) && moving && !stopped) {
                stopped=true; warned=true; return "Stopped";
            }
            if (elapsed>=2 && !warned) { warned=true; return "Warning"; }
            return null;
        }
    }
}
