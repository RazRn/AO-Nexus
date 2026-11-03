using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    // Talks to Nexus Shortcuts (src/Shortcuts) inside a game client: "LIST" returns the plugin
    // chat commands that client knows, "RUN form raid" runs /form raid there as if typed.
    internal static class ShortcutClient
    {
        internal static string PipeName(int pid) => "AONexus.Shortcuts." + pid;
        internal static string TeamStatus(int pid) => Send(pid, "TEAMSTATUS", 800, 50);
        internal static string TeamView(int pid) => Send(pid, "TEAMVIEW", 400, 0);
        internal static string FormationStatus(int pid) => Send(pid,"FORMATIONSTATUS",400,50);
        internal static string FormationCommand(int pid,NexusPlayTools.FormationRequest request) =>
            Send(pid,"FORMATION "+new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(request),600,100);
        internal static string TeamRecoveryStatus(int pid) => Send(pid, "TEAMRECOVERYSTATUS", 400, 0);
        internal static string TeamPlan(int pid, string plan) => Send(pid, plan, 800, 50);

        internal sealed class Result
        {
            internal bool Ok;
            internal string Message;
        }

        // Commands available in the client, or null when Nexus Shortcuts is not loaded there.
        internal static HashSet<string> Commands(int pid)
        {
            string reply = Send(pid, "LIST", 1200);
            if (reply == null || !reply.StartsWith("OK", StringComparison.Ordinal)) return null;
            return new HashSet<string>(reply.Substring(2).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        }

        // The character's profession as AOSharp names it ("Engineer", "MartialArtist") and its name;
        // null while the client has no character yet or Nexus Shortcuts is not loaded.
        internal static Tuple<string, string> Who(int pid)
        {
            string reply = Send(pid, "WHO", 1200);
            if (reply == null || !reply.StartsWith("OK ", StringComparison.Ordinal)) return null;
            string[] parts = reply.Substring(3).Split(new[] { ' ' }, 2);
            return parts.Length == 2 ? Tuple.Create(parts[0], parts[1]) : null;
        }

        // What Nexus Shortcuts saw of the other plugins; null when it is not loaded or not ready.
        internal static HealthReport Health(int pid)
        {
            string reply = Send(pid, "HEALTH", 1500);
            return reply != null && reply.StartsWith("OK ", StringComparison.Ordinal) ? HealthReport.Parse(reply.Substring(3)) : null;
        }

        internal static Result Run(int pid, string commandLine)
        {
            string line = (commandLine ?? "").Trim().TrimStart('/');
            if (line.Length == 0 || line.Length > 400 || line.Any(char.IsControl)) return new Result { Message = AOLauncher.Localization.Texts.Get("Text_Invalid_command_6B2A1A6763A3") };
            string reply = Send(pid, "RUN " + line, 2000);
            if (reply == null) return new Result { Message = AOLauncher.Localization.Texts.Get("Text_Nexus_Shortcuts_is_not_loaded_in_845554D03C37") };
            if (reply == "OK") return new Result { Ok = true };
            if (reply.StartsWith("UNKNOWN", StringComparison.Ordinal))
                return new Result { Message = string.Format(AOLauncher.Localization.Texts.Get("Text_0_is_not_available_here_the_23C08E418852"), line.Split(' ')[0]) };
            if (reply == "BUSY") return new Result { Message = AOLauncher.Localization.Texts.Get("Text_The_client_is_busy_try_again_196481D018E8") };
            return new Result { Message = reply };
        }

        static string Send(int pid, string request, int timeoutMs, int connectTimeoutMs = -1)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName(pid), PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    pipe.Connect(connectTimeoutMs < 0 ? timeoutMs : connectTimeoutMs);
                    byte[] data = Encoding.UTF8.GetBytes(request + "\n");
                    pipe.Write(data, 0, data.Length);
                    pipe.Flush();
                    var bytes = new List<byte>();
                    var buffer = new byte[4096];
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    // A health report for a full pack is tens of kilobytes.
                    while (bytes.Count < 512 * 1024)
                    {
                        int remaining = timeoutMs - (int)elapsed.ElapsedMilliseconds;
                        if (remaining <= 0) return null;
                        var pending = pipe.ReadAsync(buffer, 0, buffer.Length);
                        // Checking elapsed time around a synchronous Read cannot time it out.
                        if (System.Threading.Tasks.Task.WhenAny(pending, System.Threading.Tasks.Task.Delay(remaining)).GetAwaiter().GetResult() != pending)
                        {
                            // Disposal cancels native I/O; observe its eventual cancellation/fault.
                            pending.ContinueWith(task => { var ignored = task.Exception; },
                                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                            return null;
                        }
                        int read = pending.GetAwaiter().GetResult();
                        if (read <= 0) return null;
                        bytes.AddRange(buffer.Take(read));
                        int newline = bytes.IndexOf((byte)'\n');
                        if (newline >= 0) return Encoding.UTF8.GetString(bytes.Take(newline).ToArray()).Trim();
                    }
                    return null;
                }
            }
            catch (TimeoutException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}
