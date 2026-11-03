using System;

namespace Nexus.Core
{
    public static class PluginDispatch
    {
        // Exchange may throw TimeoutException ONLY before dispatch. A reply timeout must
        // return a failed response: retrying an already sent load can initialize it twice.
        public static T Send<T>(Func<int, T> exchange, Action startHost, bool mayStart, bool status,
            Func<T, bool> succeeded, Func<T, string> error, Func<string, string, T> failure,
            Action<string, Exception, string> report, Action recovered) where T : class
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var response = exchange(attempt == 0 ? 400 : 5000)
                        ?? failure("The plugin host returned an empty response.", null);
                    if (succeeded(response)) recovered();
                    else report("Request failed; no automatic resend after dispatch", null, error(response));
                    return response;
                }
                catch (TimeoutException ex)
                {
                    if (!mayStart && status) return null;
                    if (!mayStart || attempt > 0)
                    {
                        report("Host connection unavailable", ex, null);
                        return null;
                    }
                    if (startHost == null)
                    {
                        report("Host not started", null, "No AO# pack is available.");
                        return failure("No AO# pack is available.", null);
                    }
                    try { startHost(); }
                    catch (Exception launchError)
                    {
                        report("Host launch failed; no injection dispatched", launchError, null);
                        return failure("The plugin host could not start: " + launchError.Message, "Failed");
                    }
                }
                catch (Exception ex)
                {
                    report("Host communication failed; request outcome may be unknown", ex, null);
                    return failure(ex.Message, null);
                }
            }
            return null;
        }
    }
}
