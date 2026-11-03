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
        // The kind (rebirth, live, prk or custom) comes from the server list. The status-page
        // URLs above stay in code because each one has its own verified parser.
        internal static string Kind(LoginServerProfile server) => ServerRules.Kind(server.Server, server.Port);
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
            if (endpoint == null || !endpoint.Enabled) return Task.FromResult(Unknown(key, AOLauncher.Localization.Texts.Get("Text_Check_disabled_or_not_configured_74349205A58C"), DateTime.UtcNow));
            if (!endpoint.Valid) return Task.FromResult(Unknown(key, AOLauncher.Localization.Texts.Get("Text_Invalid_endpoint_Edit_it_in_Settings_C7704B69DEFF"), DateTime.UtcNow));
            return TcpAsync(key, endpoint.Host, endpoint.Port, cancellation);
        }
        internal static async Task<HealthResult> TcpAsync(string key, string host, int port, CancellationToken cancellation)
        {
            var watch = Stopwatch.StartNew();
            var result = new HealthResult { Key = key, Detail = host + ":" + port + " · TCP", CheckedUtc = DateTime.UtcNow };
            try
            {
                if (port < 1 || port > 65535 || string.IsNullOrWhiteSpace(host)) return Unknown(key, AOLauncher.Localization.Texts.Get("Text_Invalid_endpoint_6E121BD35A06"), DateTime.UtcNow);
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
            if (kind != "rebirth" && kind != "prk") return Unknown("report", AOLauncher.Localization.Texts.Get("Text_No_verified_public_service_report_1DBBAF184B50"), DateTime.UtcNow);
            try
            {
                string body = await DownloadAsync(kind == "rebirth" ? RebirthStatusUrl : PrkStatusUrl, cancellation).ConfigureAwait(false);
                return ParseReport(kind, body, server, DateTime.UtcNow);
            }
            catch (Exception error) when (error is HttpRequestException || error is IOException || error is OperationCanceledException || error is ArgumentException || error is InvalidOperationException)
            {
                cancellation.ThrowIfCancellationRequested();
                return Unknown("report", AOLauncher.Localization.Texts.Get("Text_Official_status_unavailable_this_does_not_13A85829560B"), DateTime.UtcNow);
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
            var unknown = Unknown("report", AOLauncher.Localization.Texts.Get("Text_Status_missing_outdated_or_unrecognized_003D0EB6AD0B"), now);
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
                    var names = new[] { AOLauncher.Localization.Texts.Get("Text_Login_D54208190619"), AOLauncher.Localization.Texts.Get("Text_Zones_305990ABA4B8"), AOLauncher.Localization.Texts.Get("Text_Chat_0565C77EEB6E"), AOLauncher.Localization.Texts.Get("Text_Accounts_3EF79D11C9C7") };
                    if (keys.Any(key => !data.services.ContainsKey(key) || !data.services[key].HasValue)) return unknown;
                    return new HealthResult { Key = "report", CheckedUtc = now,
                        State = keys.All(key => data.services[key] == true) ? HealthState.ReportedOnline : HealthState.ReportedIssue,
                        Detail = string.Join(" · ", keys.Select((key, i) => names[i] + ": " + (data.services[key] == true ? AOLauncher.Localization.Texts.Get("Text_online_A877620DE0B5") : AOLauncher.Localization.Texts.Get("Text_offline_20CC487851BC")))) };
                }
                if (kind == "prk")
                {
                    var data = json.Deserialize<PrkReport>(body);
                    if (data?.data == null) return unknown;
                    var match = data.data.FirstOrDefault(item => item != null && item.loginIp == server.Server && item.loginPort == server.Port);
                    return new HealthResult { Key = "report", CheckedUtc = now, State = match == null ? HealthState.Unknown : HealthState.ReportedOnline,
                        Detail = match == null ? AOLauncher.Localization.Texts.Get("Text_This_server_is_not_reporting_on_A823748C4B1F")
                            : AOLauncher.Localization.Texts.Get("Text_Listed_by_PRK_No_per_service_124D6C17AB74") };
                }
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is FormatException || error is OverflowException) { }
            return unknown;
        }
    }
}
