using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AOQuickLauncher
{
    /// <summary>
    /// Receives the login stages LoginHandler.dll reports from inside one game client. The channel
    /// is diagnostic only: a missing, slow or malformed report never changes the login itself.
    /// </summary>
    internal sealed class LoginStatusChannel : IDisposable
    {
        internal const string PipePrefix = "AONexus.Status.";
        internal const string LinePrefix = "AO_NEXUS_STATUS_V1 ";
        private const int MaximumBytes = 16 * 1024;
        private const int MaximumEvents = 64;
        private readonly NamedPipeServerStream pipe;
        private readonly Stopwatch clock;
        private readonly Action<string, long> onEvent;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly object sync = new object();
        private readonly List<string> timeline = new List<string>();
        private readonly TaskCompletionSource<bool> requested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when the client has requested the character from the character list.</summary>
        internal Task Requested => requested.Task;
        private Task reader = Task.CompletedTask;

        internal string PipeName { get; }

        private LoginStatusChannel(Stopwatch clock, Action<string, long> onEvent)
        {
            this.clock = clock;
            this.onEvent = onEvent;
            PipeName = PipePrefix + Guid.NewGuid().ToString("N");
            pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 0);
        }

        internal static LoginStatusChannel TryStart(Stopwatch clock, Action<string, long> onEvent)
        {
            try
            {
                var channel = new LoginStatusChannel(clock, onEvent);
                channel.reader = Task.Run(channel.ReadAsync);
                return channel;
            }
            catch (Exception) { return null; }
        }

        private async Task ReadAsync()
        {
            try
            {
                await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                var buffer = new byte[512];
                var pending = new StringBuilder();
                int total = 0;
                while (!stop.IsCancellationRequested)
                {
                    int count = await pipe.ReadAsync(buffer, 0, buffer.Length, stop.Token).ConfigureAwait(false);
                    if (count <= 0) break;
                    total += count;
                    if (total > MaximumBytes) break;
                    pending.Append(Encoding.ASCII.GetString(buffer, 0, count));
                    int newline;
                    while ((newline = pending.ToString().IndexOf('\n')) >= 0)
                    {
                        string line = pending.ToString(0, newline).TrimEnd('\r');
                        pending.Remove(0, newline + 1);
                        if (TryParse(line, out string stage)) Record(stage);
                    }
                    if (pending.Length > 512) break;
                }
            }
            catch (Exception) { } // Includes cancellation and a client that exits mid-report.
        }

        private void Record(string stage)
        {
            long at = clock.ElapsedMilliseconds;
            lock (sync)
            {
                if (timeline.Count >= MaximumEvents) return;
                // "1.9s character-list": no '@', which anonymisation would read as an e-mail address.
                timeline.Add((at / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s " + stage);
            }
            if (stage == "character-requested") requested.TrySetResult(true);
            try { onEvent?.Invoke(stage, at); } catch (Exception) { }
        }

        /// <summary>Accepts only the fixed stage vocabulary: lowercase words, numbers and '=' / '-'.</summary>
        internal static bool TryParse(string line, out string stage)
        {
            stage = null;
            if (line == null || line.Length > 220 || !line.StartsWith(LinePrefix, StringComparison.Ordinal)) return false;
            string rest = line.Substring(LinePrefix.Length);
            int space = rest.IndexOf(' ');
            if (space <= 0 || !rest.Take(space).All(char.IsDigit)) return false;
            string text = rest.Substring(space + 1).Trim();
            if (text.Length == 0 || text.Length > 160
                || text.Any(c => !((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == ' ' || c == '=' || c == '-'))) return false;
            stage = text;
            return true;
        }

        internal string Summary()
        {
            lock (sync) return timeline.Count == 0 ? "no reports" : string.Join(", ", timeline);
        }

        internal string LastStage()
        {
            lock (sync) return timeline.Count == 0 ? null : timeline[timeline.Count - 1];
        }

        public void Dispose()
        {
            stop.Cancel();
            try { reader.Wait(500); } catch (Exception) { }
            pipe.Dispose();
            stop.Dispose();
        }
    }

    /// <summary>
    /// Keeps client starts in the same game folder apart. AO briefly locks files in its folder while
    /// starting; simultaneous starts can fail with "No write access to the main AO folder".
    /// </summary>
    internal static class StartSpacing
    {
        private static readonly object Sync = new object();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly Dictionary<string, long> NextAllowed = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Waits for this folder's turn and returns how long that took, in milliseconds.</summary>
        internal static async Task<long> WaitTurnAsync(string folder, int spacingMs)
        {
            if (spacingMs <= 0 || string.IsNullOrEmpty(folder)) return 0;
            long started = Clock.ElapsedMilliseconds;
            while (true)
            {
                long wait;
                lock (Sync)
                {
                    long now = Clock.ElapsedMilliseconds;
                    NextAllowed.TryGetValue(folder, out long allowed);
                    wait = allowed - now;
                    // Reserve the slot before starting, so parallel queues cannot take it too.
                    if (wait <= 0) { NextAllowed[folder] = now + spacingMs; return now - started; }
                }
                await Task.Delay((int)Math.Min(wait, int.MaxValue)).ConfigureAwait(false);
            }
        }

        internal static void Reset() { lock (Sync) NextAllowed.Clear(); }
    }

    /// <summary>Reads the text of standard Windows dialogs owned by a game client (for example AO's startup errors).</summary>
    internal static class ClientDialogs
    {
        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr window, StringBuilder name, int capacity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, StringBuilder lParam, uint flags, uint timeout, out IntPtr result);
        private const uint WM_GETTEXT = 0x000D, SMTO_ABORTIFHUNG = 0x0002, SMTO_BLOCK = 0x0001;

        /// <summary>Returns the static texts of visible dialog boxes belonging to the process.</summary>
        internal static List<string> Read(int processId)
        {
            var texts = new List<string>();
            try
            {
                EnumWindows((window, _) =>
                {
                    GetWindowThreadProcessId(window, out uint owner);
                    if (owner != processId || !IsWindowVisible(window) || ClassOf(window) != "#32770") return true;
                    var parts = new List<string>();
                    EnumChildWindows(window, (child, __) =>
                    {
                        if (ClassOf(child) == "Static")
                        {
                            string text = TextOf(child);
                            if (!string.IsNullOrWhiteSpace(text)) parts.Add(text.Trim());
                        }
                        return parts.Count < 6;
                    }, IntPtr.Zero);
                    string joined = Clean(string.Join(" ", parts));
                    if (joined.Length > 0) texts.Add(joined);
                    return texts.Count < 3;
                }, IntPtr.Zero);
            }
            catch (Exception) { }
            return texts;
        }

        internal static string Clean(string text)
        {
            var clean = new string((text ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
            while (clean.Contains("  ")) clean = clean.Replace("  ", " ");
            return clean.Length > 200 ? clean.Substring(0, 200) + "…" : clean;
        }

        private static string ClassOf(IntPtr window)
        {
            var name = new StringBuilder(64);
            return GetClassNameW(window, name, name.Capacity) > 0 ? name.ToString() : "";
        }

        private static string TextOf(IntPtr window)
        {
            var text = new StringBuilder(512);
            return SendMessageTimeoutW(window, WM_GETTEXT, (IntPtr)text.Capacity, text, SMTO_ABORTIFHUNG | SMTO_BLOCK, 100, out _) != IntPtr.Zero
                ? text.ToString() : "";
        }
    }
}

namespace AOQuickLauncher
{
    /// <summary>
    /// Detects when a client has been handed over from the login server to the world. In AO Rebirth the zone
    /// claims the character's handoff ticket before it sends the chat-server address, so a second server
    /// connection besides the zone (normally chat) proves the claim is done. After that, a new login on the same
    /// account can no longer invalidate this character ("stale_login").
    /// </summary>
    internal static class ZoneHandoff
    {
        private const int AF_INET = 2, TCP_TABLE_OWNER_PID_CONNECTIONS = 4, MIB_TCP_STATE_ESTAB = 5;

        [System.Runtime.InteropServices.DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(System.IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);

        /// <summary>Remote endpoints ("address:port") of the process's established IPv4 TCP connections.</summary>
        internal static System.Collections.Generic.List<string> Read(int processId)
        {
            var endpoints = new System.Collections.Generic.List<string>();
            int size = 0;
            GetExtendedTcpTable(System.IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
            if (size <= 0 || size > 16 * 1024 * 1024) return endpoints;
            System.IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_CONNECTIONS, 0) != 0) return endpoints;
                int count = System.Runtime.InteropServices.Marshal.ReadInt32(buffer);
                for (int index = 0; index < count && index < 65536; index++)
                {
                    // MIB_TCPROW_OWNER_PID: state, local address, local port, remote address, remote port, owning PID.
                    System.IntPtr row = buffer + 4 + index * 24;
                    if (System.Runtime.InteropServices.Marshal.ReadInt32(row, 20) != processId
                        || System.Runtime.InteropServices.Marshal.ReadInt32(row, 0) != MIB_TCP_STATE_ESTAB) continue;
                    uint address = unchecked((uint)System.Runtime.InteropServices.Marshal.ReadInt32(row, 12));
                    uint port = unchecked((uint)System.Runtime.InteropServices.Marshal.ReadInt32(row, 16));
                    endpoints.Add(Format(address, port));
                }
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
            return endpoints;
        }

        internal static string Format(uint address, uint port)
            => (address & 0xFF) + "." + ((address >> 8) & 0xFF) + "." + ((address >> 16) & 0xFF) + "." + ((address >> 24) & 0xFF)
                + ":" + (((port & 0xFF) << 8) | ((port >> 8) & 0xFF));

        /// <summary>
        /// Records server endpoints in the order they appear, ignoring the login server. Returns true once two
        /// distinct non-login endpoints have been seen (zone, then chat).
        /// </summary>
        internal static bool Observe(System.Collections.Generic.List<string> seen, System.Collections.Generic.IEnumerable<string> current, string loginEndpoint)
        {
            foreach (string endpoint in current)
                if (!string.Equals(endpoint, loginEndpoint, StringComparison.Ordinal) && !seen.Contains(endpoint)) seen.Add(endpoint);
            return seen.Count >= 2;
        }

        /// <summary>Port numbers only: zone and chat addresses of private servers are not recorded.</summary>
        internal static string Ports(System.Collections.Generic.IEnumerable<string> endpoints)
            => string.Join(",", endpoints.Select(endpoint => endpoint.Substring(endpoint.LastIndexOf(':') + 1)));

        /// <summary>
        /// Waits for the handoff; returns the ports seen, or null when <paramref name="stop"/> finishes or time runs out.
        /// <paramref name="timeline"/> receives every connection change as "port+ or port- at seconds", for diagnostics.
        /// </summary>
        internal static async Task<string> WaitAsync(int processId, string loginEndpoint, Task stop, int timeoutMs,
            Stopwatch clock, System.Collections.Generic.List<string> timeline)
        {
            var seen = new System.Collections.Generic.List<string>();
            var open = new System.Collections.Generic.HashSet<string>();
            var timer = Stopwatch.StartNew();
            while (!stop.IsCompleted && timer.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    var current = Read(processId);
                    string at = (clock.ElapsedMilliseconds / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
                    lock (timeline)
                    {
                        foreach (string endpoint in current.Where(item => !open.Contains(item)))
                            timeline.Add(at + " " + Port(endpoint) + (endpoint == loginEndpoint ? " login" : "") + " open");
                        foreach (string endpoint in open.Where(item => !current.Contains(item)))
                            timeline.Add(at + " " + Port(endpoint) + " closed");
                    }
                    open = new System.Collections.Generic.HashSet<string>(current);
                    if (Observe(seen, current, loginEndpoint)) return Ports(seen);
                }
                catch (Exception error)
                {
                    lock (timeline) timeline.Add("connection table unavailable: " + error.GetType().Name);
                    return null; // Keep the safe, slower path.
                }
                await Task.WhenAny(stop, Task.Delay(150)).ConfigureAwait(false);
            }
            return null;
        }

        private static string Port(string endpoint) => endpoint.Substring(endpoint.LastIndexOf(':') + 1);
    }
}
