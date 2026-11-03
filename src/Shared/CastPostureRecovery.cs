using System;

namespace NexusClientRecovery
{
    // A server rejection can disagree with the rendered/local standing state.
    // Never stand a seated player or retry indefinitely. Times are monotonic seconds.
    internal sealed class CastPostureRecovery
    {
        internal const string StandingRequired = "You must be standing up to execute a nano program.";
        double first = double.NaN, last = double.NaN, nextAllowed;
        int attempts;
        internal int Rejections { get; private set; }
        internal int Corrections { get; private set; }

        internal void ResetZone()
        {
            first = last = double.NaN;
            attempts = 0;
            // Keep the cooldown across zoning; repeated zone transitions cannot bypass it.
        }

        internal bool Observe(string feedback, double now, bool standingAndStationary)
        {
            if (!string.Equals(feedback?.Trim(), StandingRequired, StringComparison.OrdinalIgnoreCase)) return false;
            Rejections++;
            if (!standingAndStationary || double.IsNaN(now) || double.IsInfinity(now))
            {
                first = last = double.NaN;
                return false;
            }
            if (double.IsNaN(first) || now < last || now - last > 3)
                first = now;
            last = now;
            if (now - first < 0.25 || now < nextAllowed || attempts >= 2) return false;
            attempts++;
            Corrections++;
            nextAllowed = now + 5;
            first = last = double.NaN;
            return true;
        }
    }
}
