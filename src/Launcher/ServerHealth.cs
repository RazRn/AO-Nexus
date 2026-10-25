using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    internal static class SidebarText
    {
        internal static string L(string english, string danish) => Localization.Texts.CurrentLanguage == "da" ? danish : english;
    }

    internal enum HealthState { Unknown, Reachable, Unreachable, ReportedOnline, ReportedIssue }
    internal sealed class HealthResult
    {
        public string Key { get; set; }
        public string Detail { get; set; }
        public HealthState State { get; set; }
        public long? Milliseconds { get; set; }
        public DateTime CheckedUtc { get; set; }
    }

    // No authentication, protocol packets, port scans, admin endpoints or player data.
    internal static class ServerHealth
    {
        internal const string RebirthStatusUrl = "https://www.ao-rebirth.com/server-status.php";
        internal const string PrkStatusUrl = "https://project-rk.com/api/status";
        internal const int MaximumResponseBytes = 65536;
        internal static string Kind(LoginServerProfile server)
        {
            if (server.Server == "2.24.96.30" && server.Port == 7500) return "rebirth";
            if (server.Server == "5.161.243.214" && server.Port == 7000) return "prk";
            if (server.Server == "37.18.192.41" && (server.Port == 7505 || server.Port == 7506)) return "live";
            return "custom";
        }
        internal static string EndpointKey(LoginServerProfile server) => server.Server + ":" + server.Port;
        internal static HealthResult Unknown(string key, string detail, DateTime now) => new HealthResult { Key = key, Detail = detail, State = HealthState.Unknown, CheckedUtc = now };
        internal static Task<HealthResult[]> CheckAsync(LoginServerProfile server, CancellationToken cancellation)
            => CheckConfiguredAsync(server, HealthEndpoints.Defaults(server), cancellation);
        internal static async Task<HealthResult[]> CheckConfiguredAsync(LoginServerProfile server, HealthEndpoints endpoints, CancellationToken cancellation)
        {
            var tasks = new List<Task<HealthResult>> { ProbeAsync("login", endpoints.Login, cancellation), ProbeAsync("zone", endpoints.Zone, cancellation), ProbeAsync("chat", endpoints.Chat, cancellation) };
            tasks.Add(ReportAsync(server, cancellation));
            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        private static Task<HealthResult> ProbeAsync(string key, HealthEndpoint endpoint, CancellationToken cancellation)
        {
            if (endpoint == null || !endpoint.Enabled) return Task.FromResult(Unknown(key, SidebarText.L("Check disabled or not configured.", "Tjek slået fra eller ikke konfigureret."), DateTime.UtcNow));
            if (!endpoint.Valid) return Task.FromResult(Unknown(key, SidebarText.L("Invalid endpoint. Edit it in Settings.", "Ugyldig adresse. Ret den i Indstillinger."), DateTime.UtcNow));
            return TcpAsync(key, endpoint.Host, endpoint.Port, cancellation);
        }
        internal static async Task<HealthResult> TcpAsync(string key, string host, int port, CancellationToken cancellation)
        {
            var watch = Stopwatch.StartNew();
            var result = new HealthResult { Key = key, Detail = host + ":" + port + " · TCP", CheckedUtc = DateTime.UtcNow };
            try
            {
                if (port < 1 || port > 65535 || string.IsNullOrWhiteSpace(host)) return Unknown(key, SidebarText.L("Invalid endpoint.", "Ugyldig adresse."), DateTime.UtcNow);
                using (var socket = new TcpClient())
                using (cancellation.Register(() => socket.Close()))
                {
                    Task connect = socket.ConnectAsync(host, port);
                    // Observe a late failure after timeout/disposal without retaining another probe.
                    _ = connect.ContinueWith(task => { var ignored = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    if (await Task.WhenAny(connect, Task.Delay(3000, cancellation)).ConfigureAwait(false) != connect) throw new TimeoutException();
                    await connect.ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    result.State = HealthState.Reachable; result.Milliseconds = watch.ElapsedMilliseconds;
                }
            }
            catch (Exception error) when (error is SocketException || error is TimeoutException || error is ObjectDisposedException || error is OperationCanceledException || error is ArgumentException)
            {
                cancellation.ThrowIfCancellationRequested();
                result.State = HealthState.Unreachable;
            }
            result.CheckedUtc = DateTime.UtcNow;
            return result;
        }
        private static async Task<HealthResult> ReportAsync(LoginServerProfile server, CancellationToken cancellation)
        {
            string kind = Kind(server);
            if (kind != "rebirth" && kind != "prk") return Unknown("report", SidebarText.L("No verified public service report.", "Ingen verificeret offentlig servicestatus."), DateTime.UtcNow);
            try
            {
                string body = await DownloadAsync(kind == "rebirth" ? RebirthStatusUrl : PrkStatusUrl, cancellation).ConfigureAwait(false);
                return ParseReport(kind, body, server, DateTime.UtcNow);
            }
            catch (Exception error) when (error is HttpRequestException || error is IOException || error is OperationCanceledException || error is ArgumentException || error is InvalidOperationException)
            {
                cancellation.ThrowIfCancellationRequested();
                return Unknown("report", SidebarText.L("Official status unavailable; this does not prove an outage.", "Officiel status utilgængelig; det beviser ikke et nedbrud."), DateTime.UtcNow);
            }
        }
        internal static async Task<string> DownloadAsync(string address, CancellationToken cancellation)
        {
            if (address != RebirthStatusUrl && address != PrkStatusUrl) throw new ArgumentException("Unknown status source.");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false }))
            {
                timeout.CancelAfter(5000);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AONexus/" + AppUpdates.CurrentVersion.ToString(3));
                client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                using (var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new InvalidDataException("Status response too large.");
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        byte[] bytes = new byte[4096]; int read;
                        while ((read = await input.ReadAsync(bytes, 0, bytes.Length, timeout.Token).ConfigureAwait(false)) > 0)
                        {
                            if (output.Length + read > MaximumResponseBytes) throw new InvalidDataException("Status response too large.");
                            output.Write(bytes, 0, read);
                        }
                        return Encoding.UTF8.GetString(output.ToArray());
                    }
                }
            }
        }
        // Nullable fields distinguish explicit failures from a missing/changed response schema.
        public sealed class RebirthReport { public bool? ok { get; set; } public bool? stale { get; set; } public int? ageSeconds { get; set; } public string generatedAt { get; set; } public Dictionary<string, bool?> services { get; set; } }
        public sealed class PrkReport { public PrkServer[] data { get; set; } }
        public sealed class PrkServer { public string loginIp { get; set; } public int loginPort { get; set; } public int? count { get; set; } }
        internal static HealthResult ParseReport(string kind, string body, LoginServerProfile server, DateTime now)
        {
            var unknown = Unknown("report", SidebarText.L("Status missing, outdated or unrecognized.", "Status mangler, er forældet eller ukendt."), now);
            if (body == null || Encoding.UTF8.GetByteCount(body) > MaximumResponseBytes) return unknown;
            try
            {
                var json = new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes, RecursionLimit = 12 };
                if (kind == "rebirth")
                {
                    var data = json.Deserialize<RebirthReport>(body); DateTimeOffset generated;
                    if (data?.ok != true || data.stale != false || !data.ageSeconds.HasValue || data.ageSeconds < 0 || data.ageSeconds > 180
                        || !DateTimeOffset.TryParse(data.generatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out generated)
                        || (now - generated.UtcDateTime).TotalSeconds > 180 || (now - generated.UtcDateTime).TotalSeconds < -30 || data.services == null) return unknown;
                    var keys = new[] { "loginEngine", "zoneEngine", "chatEngine", "accountBroker" };
                    var names = new[] { SidebarText.L("Login", "Login"), SidebarText.L("Zones", "Zoner"), SidebarText.L("Chat", "Chat"), SidebarText.L("Accounts", "Konti") };
                    if (keys.Any(key => !data.services.ContainsKey(key) || !data.services[key].HasValue)) return unknown;
                    return new HealthResult { Key = "report", CheckedUtc = now,
                        State = keys.All(key => data.services[key] == true) ? HealthState.ReportedOnline : HealthState.ReportedIssue,
                        Detail = string.Join(" · ", keys.Select((key, i) => names[i] + ": " + (data.services[key] == true ? SidebarText.L("online", "online") : SidebarText.L("offline", "offline")))) };
                }
                if (kind == "prk")
                {
                    var data = json.Deserialize<PrkReport>(body);
                    if (data?.data == null) return unknown;
                    var match = data.data.FirstOrDefault(item => item != null && item.loginIp == server.Server && item.loginPort == server.Port);
                    return new HealthResult { Key = "report", CheckedUtc = now, State = match == null ? HealthState.Unknown : HealthState.ReportedOnline,
                        Detail = match == null ? SidebarText.L("This server is not reporting on PRK's public API.", "Denne server rapporterer ikke på PRK's offentlige API.")
                            : SidebarText.L("Listed by PRK. No per-service status or source timestamp supplied.", "Rapporteret af PRK. Ingen særskilt servicestatus eller tidsstempel fra kilden.") };
                }
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is FormatException || error is OverflowException) { }
            return unknown;
        }
    }
}
