using AOLauncher.Localization;
using AOLauncher;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AOQuickLauncher
{
    internal sealed class LaunchTarget
    {
        public string GamePath { get; private set; }
        public string Server { get; private set; }
        public int Port { get; private set; }
        public string Arguments { get; private set; }
        public string EndpointKey => Server + ":" + Port;

        public static LaunchTarget Create(string gamePath, string server, int port)
        {
            gamePath = Path.GetFullPath(gamePath ?? Environment.GetEnvironmentVariable("AOPath") ?? AppContext.BaseDirectory);
            if (!File.Exists(Path.Combine(gamePath, "AnarchyOnline.exe")))
                throw new ArgumentException(Texts.T("AnarchyOnline.exe was not found in the selected game folder."));
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "LoginHandler.dll")))
                throw new FileNotFoundException(Texts.T("LoginHandler.dll is missing beside AOQuickLauncher.exe."));
            server = server ?? "37.18.192.41";
            if (!IPAddress.TryParse(server, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException(Texts.T("The login server must be an IPv4 address."));
            if (port < 1 || port > 65535)
                throw new ArgumentException(Texts.T("The login port must be between 1 and 65535."));
            var bytes = address.GetAddressBytes();
            uint encoded = (uint)bytes[0] | ((uint)bytes[1] << 8) | ((uint)bytes[2] << 16) | ((uint)bytes[3] << 24);
            return new LaunchTarget { GamePath = gamePath, Server = address.ToString(), Port = port,
                Arguments = $"IA{encoded} IP{port} DU" };
        }
    }

    public sealed class CharacterLaunch
    {
        public string AccountId { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public int CharacterId { get; set; }
        public bool CreateCharacter { get; set; }
        public string CharacterName { get; set; }
        public string GamePath { get; set; }
        public string Server { get; set; }
        public string ServerName { get; set; }
        public int? Port { get; set; }
        public int? MaxAccounts { get; set; }
        public int? MaxCharactersPerAccount { get; set; }

        internal bool HasExplicitTarget => GamePath != null || Server != null || Port.HasValue;
        internal LaunchTarget ResolvedTarget { get; set; }
    }

    public sealed class LaunchRequest
    {
        public string Language { get; set; } = "auto";
        public string GamePath { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public List<CharacterLaunch> Characters { get; set; }
        public int ReadyTimeoutSeconds { get; set; } = 120;
        // Minimum gap between client starts in the same game folder (AO locks files while starting).
        public int StartSpacingMs { get; set; } = 1500;
        // Experimental: LoginHandler skips drawing the login and character screens.
        public bool QuietLaunch { get; set; }
        // The next character on the same account starts once the previous one has been handed over to the
        // world (zone and chat connections), not when it is fully loaded. Starting earlier, right after the
        // character is selected, disconnected the loading character ("stale_login") in testing.
        public bool StartAfterHandoff { get; set; }
    }

    internal static class LaunchQueue
    {
        public static int FromStandardInput(bool validateOnly)
        {
            if (!Console.IsInputRedirected)
                throw new ArgumentException(Texts.T("Start this function from the launcher window."));
            string json = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(json) || json.Length > 1024 * 1024)
                throw new ArgumentException(Texts.T("The launch request is missing or too large."));
            LaunchRequest request;
            try { request = JsonSerializer.Deserialize<LaunchRequest>(json); }
            catch (JsonException) { throw new ArgumentException(Texts.T("The launch request could not be read.")); }
            json = null;
            Texts.SetLanguage(request?.Language);
            ValidateRequest(request);
            if (validateOnly)
            {
                ReportTargets(request, true);
                foreach (var entry in request.Characters) entry.Password = null;
                Console.WriteLine(Texts.T("Validation OK. No game clients were started."));
                return 0;
            }

            CreationMonitor creation = null;
            try
            {
                int result = WithLaunchLock(() =>
                {
                    // The queue mutex makes checking existing reservations and starting
                    // a new batch one operation across independent launcher windows.
                    ValidateAccountLimits(request.Characters, ClientSessions.ReadValidatedRecords());
                    ReportTargets(request, false);
                    if (request.Characters.Count == 1 && request.Characters[0].CreateCharacter)
                    {
                        creation = CreationMonitor.Start(request.Characters[0]);
                        return 0;
                    }
                    else return LaunchSelectedAsync(request).GetAwaiter().GetResult();
                });
                // Creation can take a long time. The process is already reserved, so
                // release the global launch lock before waiting for the native report.
                return creation == null ? result : creation.WaitAsync(TimeSpan.FromMinutes(60)).GetAwaiter().GetResult();
            }
            finally
            {
                creation?.Dispose();
                foreach (var entry in request.Characters) entry.Password = null;
            }
        }

        // The operation ends once a creation client is reserved, not when the player
        // finishes creating their char. The optional name isolates concurrency tests.
        internal static T WithLaunchLock<T>(Func<T> operation, string mutexName = @"Local\AOQuickLauncher.LoginQueue")
        {
            using var queueLock = new Mutex(false, mutexName);
            bool ownsLock;
            try { ownsLock = queueLock.WaitOne(0); }
            catch (AbandonedMutexException) { ownsLock = true; }
            if (!ownsLock)
                throw new InvalidOperationException(Texts.T("A login queue is already running. Wait for it to finish."));
            try { return operation(); }
            finally { queueLock.ReleaseMutex(); }
        }

        private static void ReportTargets(LaunchRequest request, bool validateOnly)
        {
            foreach (var targetGroup in request.Characters.GroupBy(entry => entry.ResolvedTarget.EndpointKey))
            {
                var target = targetGroup.First().ResolvedTarget;
                Console.WriteLine(targetGroup.Any(entry => entry.CreateCharacter)
                    ? Texts.F("Target: {0}:{1} | character creation.", target.Server, target.Port)
                    : Texts.F("Target: {0}:{1} | {2} character(s).", target.Server, target.Port, targetGroup.Count()));
                if (validateOnly) Console.WriteLine(Texts.F("Arguments: {0}", target.Arguments));
            }
        }

        internal static void ValidateRequest(LaunchRequest request)
        {
            if (request == null || request.Characters == null || request.Characters.Count == 0)
                throw new ArgumentException(Texts.T("Select at least one character."));
            if (request.Characters.Count > 100)
                throw new ArgumentException(Texts.T("Select no more than 100 characters at a time."));
            if (request.ReadyTimeoutSeconds < 10 || request.ReadyTimeoutSeconds > 600)
                throw new ArgumentException(Texts.T("The timeout must be between 10 and 600 seconds."));
            if (request.StartSpacingMs < 0 || request.StartSpacingMs > 10000)
                throw new ArgumentException(Texts.T("The start spacing must be between 0 and 10000 milliseconds."));
            if (request.Characters.Any(entry => entry != null && entry.CreateCharacter) && request.Characters.Count != 1)
                throw new ArgumentException(Texts.T("Open character creation for one account at a time, separately from character login."));
            var ids = new HashSet<(string Endpoint, int CharacterId)>();
            foreach (var entry in request.Characters)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Account) || string.IsNullOrEmpty(entry.Password))
                    throw new ArgumentException(Texts.T("Enter a username and password for every selected account."));
                entry.Account = entry.Account.Trim();
                if (entry.AccountId != null && (entry.AccountId.Length > 128 || entry.AccountId.Any(char.IsControl)))
                    throw new ArgumentException(Texts.T("The account identifier is too long or contains invalid control characters."));
                entry.AccountId = string.IsNullOrWhiteSpace(entry.AccountId) ? null : entry.AccountId.Trim();
                if (entry.HasExplicitTarget && (string.IsNullOrWhiteSpace(entry.GamePath)
                    || string.IsNullOrWhiteSpace(entry.Server) || !entry.Port.HasValue || entry.Port == 0))
                    throw new ArgumentException(Texts.T("An explicit character target requires a game folder, IPv4 server and port."));
                // Resolve every selected target before any process can be started. Explicit
                // targets never borrow a missing field from the legacy request defaults.
                var target = entry.HasExplicitTarget
                    ? LaunchTarget.Create(entry.GamePath, entry.Server, entry.Port.Value)
                    : LaunchTarget.Create(request.GamePath, request.Server, request.Port);
                entry.ResolvedTarget = target;
                ServerAccountPolicy.ValidateLimit(AccountLimit(entry));
                ServerAccountPolicy.ValidateLimit(CharacterLimit(entry));
                if (entry.CreateCharacter && entry.CharacterId != 0)
                    throw new ArgumentException(Texts.T("A character-creation request must use character ID 0."));
                if (!entry.CreateCharacter && (entry.CharacterId <= 0 || !ids.Add((target.EndpointKey, entry.CharacterId))))
                    throw new ArgumentException(Texts.T("Each character must have a unique, positive character ID."));
                if (entry.CharacterName != null && entry.CharacterName.Any(char.IsControl))
                    throw new ArgumentException(Texts.T("The character name contains invalid control characters."));
                entry.CharacterName = entry.CreateCharacter ? Texts.T("Create new char")
                    : string.IsNullOrWhiteSpace(entry.CharacterName) ? Texts.F("Character {0}", entry.CharacterId) : entry.CharacterName.Trim();
            }
            ValidateAccountLimits(request.Characters, Array.Empty<ClientSessions.ClientRecord>());
        }

        private static int AccountLimit(CharacterLaunch entry)
        {
            return entry.MaxAccounts ?? ServerAccountPolicy.DefaultLimit(entry.ResolvedTarget.Server, entry.ResolvedTarget.Port);
        }

        private static int CharacterLimit(CharacterLaunch entry)
        {
            return entry.MaxCharactersPerAccount ?? ServerAccountPolicy.DefaultCharacterLimit(entry.ResolvedTarget.Server, entry.ResolvedTarget.Port);
        }

        private static string CharacterScope(string server, int port)
        {
            // This key combines the two known dimensions for enforcement only.
            // User-facing descriptions always come from the configured names and endpoints.
            return server == "37.18.192.41" && (port == 7505 || port == 7506) ? "official-live" : server + ":" + port;
        }

        private static string CharacterScope(CharacterLaunch entry)
        {
            return CharacterScope(entry.ResolvedTarget.Server, entry.ResolvedTarget.Port);
        }

        private static string DescribeServers(IEnumerable<CharacterLaunch> selected, IEnumerable<string> trackedEndpoints = null)
        {
            var endpoints = selected.GroupBy(entry => entry.ResolvedTarget.EndpointKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(entry => entry.ServerName)
                    .Where(name => !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsControl))
                    .Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);
            foreach (string endpoint in trackedEndpoints ?? Enumerable.Empty<string>())
                if (!endpoints.ContainsKey(endpoint)) endpoints.Add(endpoint, Array.Empty<string>());
            return string.Join(", ", endpoints.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Value.Length == 0 ? pair.Key : string.Join(" / ", pair.Value) + " (" + pair.Key + ")"));
        }

        internal static void ValidateAccountLimits(IReadOnlyList<CharacterLaunch> characters,
            IEnumerable<ClientSessions.ClientRecord> activeRecords)
        {
            var records = activeRecords.ToArray();
            foreach (var entry in characters)
            {
                var target = entry.ResolvedTarget;
                if (records.Any(record => record != null && record.CreateCharacter
                    && record.Port == target.Port && IPAddress.TryParse(record.Server, out var address)
                    && address.ToString() == target.Server
                    && string.Equals(TrackedAccountKey(record, characters), "username:" + entry.Account.Trim(), StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException(Texts.F(entry.CreateCharacter
                        ? "A character-creation client is already open for this account on {0}. Continue in that game window or close it before trying again."
                        : "Character creation is still open for this account on {0}. Finish it or close its game window before logging in another character.", DescribeServers(new[] { entry })));
            }
            foreach (var group in characters.GroupBy(entry => entry.ResolvedTarget.EndpointKey, StringComparer.OrdinalIgnoreCase))
            {
                int limit = ServerAccountPolicy.EffectiveLimit(group.Select(AccountLimit));
                if (limit == 0) continue;
                var accounts = new HashSet<string>(group.Select(entry => "username:" + entry.Account.Trim()), StringComparer.OrdinalIgnoreCase);
                var target = group.First().ResolvedTarget;
                foreach (var record in records)
                {
                    if (record == null || record.Port != target.Port
                        || !IPAddress.TryParse(record.Server, out var address)
                        || !string.Equals(address.ToString(), target.Server, StringComparison.Ordinal)) continue;
                    accounts.Add(TrackedAccountKey(record, group));
                }
                if (accounts.Count > limit)
                    throw new ArgumentException(Texts.F("Server {0} allows at most {1} simultaneous account(s). The selection and active tracked clients total {2} accounts.",
                        DescribeServers(group), limit, accounts.Count));
            }
            foreach (var group in characters.GroupBy(CharacterScope, StringComparer.OrdinalIgnoreCase))
            {
                int limit = ServerAccountPolicy.EffectiveLimit(group.Select(CharacterLimit));
                if (limit == 0) continue;
                var accountCharacters = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in group)
                    AddCharacter(accountCharacters, "username:" + entry.Account.Trim(), entry.ResolvedTarget.EndpointKey, entry.CharacterId.ToString());
                foreach (var record in records)
                {
                    if (record == null || !IPAddress.TryParse(record.Server, out var address)
                        || !string.Equals(CharacterScope(address.ToString(), record.Port), group.Key, StringComparison.OrdinalIgnoreCase)) continue;
                    AddCharacter(accountCharacters, TrackedAccountKey(record, group), address + ":" + record.Port,
                        record.CreateCharacter ? "creation-" + record.ProcessId + "-" + record.StartTimeUtcTicks : record.CharacterId.ToString());
                }
                foreach (var account in accountCharacters)
                    if (account.Value.Count > limit)
                    {
                        var endpoints = new HashSet<string>(account.Value.Select(character => character.Substring(0, character.LastIndexOf(':'))), StringComparer.OrdinalIgnoreCase);
                        throw new ArgumentException(Texts.F("Account on server {0} allows at most {1} simultaneous character(s). The selection and active tracked clients total {2} characters.",
                            DescribeServers(group.Where(entry => endpoints.Contains(entry.ResolvedTarget.EndpointKey)), endpoints), limit, account.Value.Count));
                    }
            }
        }

        private static void AddCharacter(Dictionary<string, HashSet<string>> accounts, string account, string endpoint, string characterId)
        {
            if (!accounts.TryGetValue(account, out var ids)) accounts.Add(account, ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            ids.Add(endpoint + ":" + characterId);
        }

        private static string TrackedAccountKey(ClientSessions.ClientRecord record, IEnumerable<CharacterLaunch> selected)
        {
            if (!string.IsNullOrWhiteSpace(record.Username)) return "username:" + record.Username.Trim();
            string identity = record.AccountIdentity?.Trim();
            if (identity != null && identity.StartsWith("username:", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(identity.Substring(9))) return "username:" + identity.Substring(9).Trim();
            // Previous builds stored only a saved account ID. Resolve it against this
            // request when possible; otherwise reserve one distinct account conservatively.
            if (identity != null && identity.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
            {
                var match = selected.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.AccountId)
                    && string.Equals(entry.AccountId.Trim(), identity.Substring(3).Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null) return "username:" + match.Account.Trim();
            }
            return !string.IsNullOrWhiteSpace(identity) ? "legacy:" + identity
                : "process:" + record.ProcessId + ":" + record.StartTimeUtcTicks;
        }

        private static async Task<int> LaunchSelectedAsync(LaunchRequest request)
        {
            if (!request.Characters.Any(entry => entry.CreateCharacter))
                Console.WriteLine(Texts.T("Accounts start in parallel; characters on the same account log in one at a time."));
            var queueSizes = request.Characters.GroupBy(AccountQueueKey, StringComparer.OrdinalIgnoreCase).Select(group => group.Count()).ToArray();
            Diagnostic("queues", null, new { ParallelQueues = queueSizes.Length, CharactersPerQueue = queueSizes, request.ReadyTimeoutSeconds, request.StartSpacingMs, request.QuietLaunch, request.StartAfterHandoff });
            // Confirmations that continue after the next character on the account has started.
            var confirmations = new System.Collections.Concurrent.ConcurrentBag<Task<bool>>();
            int result = await RunAccountQueuesAsync(request.Characters, async entry =>
            {
                var target = entry.ResolvedTarget;
                if (entry.CreateCharacter)
                {
                    throw new InvalidOperationException("Character creation requires its own reserved monitor.");
                }
                string runningName = ClientSessions.TryGetRunningName(entry, target);
                if (runningName != null)
                {
                    ReportCharacter(entry, runningName);
                    Console.WriteLine(Texts.F("{0} (ID {1}): already running; skipped.", runningName, entry.CharacterId));
                    return true;
                }
                if (ClientSessions.HasPending(entry, target))
                {
                    Console.WriteLine(Texts.F("ID {0}: a tracked game client is still logging in; skipped. Check its game window.", entry.CharacterId));
                    return true;
                }
                long spacingWaitMs = await StartSpacing.WaitTurnAsync(target.GamePath, request.StartSpacingMs);
                Console.WriteLine(Texts.F("Starting {0} (ID {1})...", entry.CharacterName, entry.CharacterId));
                var spawnTimer = Stopwatch.StartNew();
                var status = LoginStatusChannel.TryStart(spawnTimer,
                    (stage, ms) => Diagnostic("stage", entry, new { Stage = stage, Ms = ms }));
                Process spawned;
                try
                {
                    spawned = Program.SpawnAOProcess(new Program.Options {
                        Username = entry.Account, Password = entry.Password, CharacterId = entry.CharacterId,
                        CampToken = Guid.NewGuid().ToString("N"), StatusPipe = status?.PipeName, Quiet = request.QuietLaunch
                    }, target);
                }
                catch (Exception error)
                {
                    // Type and system error codes only: exception text may contain request data.
                    Diagnostic("start-failed", entry, new { Stage = "start or LoginHandler injection", ElapsedMs = spawnTimer.ElapsedMilliseconds,
                        Type = error.GetType().FullName, HResult = "0x" + error.HResult.ToString("X8"),
                        Win32Error = (error as System.ComponentModel.Win32Exception)?.NativeErrorCode,
                        Inner = error.InnerException?.GetType().FullName, GameExecutableExists = File.Exists(Path.Combine(target.GamePath, "AnarchyOnline.exe")) });
                    status?.Dispose();
                    throw;
                }
                var client = spawned;
                Task<bool> confirmation;
                try
                {
                    Diagnostic("started", entry, new { ElapsedMs = spawnTimer.ElapsedMilliseconds, SpacingWaitMs = spacingWaitMs, ProcessId = SafeProcessId(client) });
                    bool trackingRequired = request.Characters.Any(selected =>
                        (selected.ResolvedTarget.EndpointKey == target.EndpointKey && AccountLimit(selected) > 0)
                        || (CharacterScope(selected) == CharacterScope(entry) && CharacterLimit(selected) > 0));
                    ClientSessions.RecordPending(entry, target, client, trackingRequired);
                    Console.WriteLine(Texts.F("Waiting for the game window for ID {0}...", entry.CharacterId));
                }
                catch { client.Dispose(); status?.Dispose(); throw; }
                int processId = client.Id;
                var released = new System.Runtime.CompilerServices.StrongBox<bool>();
                confirmation = ConfirmCharacterAsync(entry, target, client, status, request.ReadyTimeoutSeconds, released);
                if (request.StartAfterHandoff)
                {
                    var handoff = ZoneHandoff.WaitAsync(processId, target.Server + ":" + target.Port, confirmation, request.ReadyTimeoutSeconds * 1000);
                    string ports = await handoff.ConfigureAwait(false);
                    if (ports != null && !confirmation.IsCompleted)
                    {
                        long handoffMs = spawnTimer.ElapsedMilliseconds;
                        // A short margin after the chat connection; the claim itself happened before it.
                        await Task.WhenAny(confirmation, Task.Delay(500)).ConfigureAwait(false);
                        if (!confirmation.IsCompleted)
                        {
                            Diagnostic("handoff", entry, new { HandoffMs = handoffMs, Ports = ports, NextStartsAfterMs = spawnTimer.ElapsedMilliseconds });
                            released.Value = true;
                            confirmations.Add(confirmation);
                            return true;
                        }
                    }
                }
                bool confirmed = await confirmation.ConfigureAwait(false);
                if (confirmed) await Task.Delay(250).ConfigureAwait(false);
                return confirmed;
            });
            // Characters released early still have to reach the world before the run reports success.
            bool[] background = await Task.WhenAll(confirmations).ConfigureAwait(false);
            if (background.Any(ok => !ok)) result = 2;
            Console.WriteLine(Texts.T(result == 0 ? "All login queues finished. Game clients remain open." :
                "Login queues finished with an error on at least one account. Game clients remain open."));
            return result;
        }

        /// <summary>Waits for the character to appear in the world, then records and reports it. Owns the client and status channel.</summary>
        private static async Task<bool> ConfirmCharacterAsync(CharacterLaunch entry, LaunchTarget target, Process client,
            LoginStatusChannel status, int timeoutSeconds, System.Runtime.CompilerServices.StrongBox<bool> released)
        {
            try
            {
                var wait = new CharacterWait();
                string actualName = await WaitForCharacterAsync(client, timeoutSeconds, wait).ConfigureAwait(false);
                Diagnostic(actualName != null ? "in-game" : "not-detected", entry, new { wait.Outcome, wait.Seconds, wait.NameFirstSeenSeconds, wait.ExitCode,
                    wait.WindowState, wait.TitleChanges, Stages = status?.Summary() ?? "unavailable", Dialogs = wait.Dialogs, RunningClients = SafeRunningClients() });
                if (actualName == null)
                {
                    // A character released early may already have later characters running behind it.
                    Console.Error.WriteLine(released.Value
                        ? Texts.F("ID {0} was not detected in game. Check the game window, account details and character ID. Characters started after it continue; existing game windows stay open.", entry.CharacterId)
                        : Texts.F("This account's queue stopped: ID {0} was not detected in game. Check the game window, account details and character ID. Other accounts continue; existing game windows stay open.", entry.CharacterId));
                    return false;
                }
                ClientSessions.Record(entry, target, client, actualName);
                ReportCharacter(entry, actualName);
                Console.WriteLine(Texts.F("ID {0}: detected as {1} in the game window.", entry.CharacterId, actualName));
                return true;
            }
            catch (Exception)
            {
                Console.Error.WriteLine(Texts.F("Could not start ID {0}. The remaining characters on this account were stopped; other accounts continue.", entry.CharacterId));
                return false;
            }
            finally
            {
                client.Dispose();
                status?.Dispose();
            }
        }

        internal static async Task<int> RunAccountQueuesAsync(IReadOnlyList<CharacterLaunch> characters,
            Func<CharacterLaunch, Task<bool>> launch)
        {
            // Pending login generations are scoped to an endpoint and username.
            // Separate installations or saved IDs for the same login must still serialize.
            var groups = characters.GroupBy(AccountQueueKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.ToArray()).ToArray();
            var queues = groups.Select(group => Task.Run(async () =>
            {
                try
                {
                    foreach (var entry in group)
                    {
                        try
                        {
                            if (!await launch(entry)) return 2;
                        }
                        catch (Exception)
                        {
                            // Never expose account credentials or callback exception data.
                            Console.Error.WriteLine(entry.CreateCharacter
                                ? Texts.T("Could not start character creation. Check the game window; an already opened client stays open.")
                                : Texts.F("Could not start ID {0}. The remaining characters on this account were stopped; other accounts continue.", entry.CharacterId));
                            return 2;
                        }
                        finally { entry.Password = null; }
                    }
                    return 0;
                }
                finally
                {
                    foreach (var entry in group) entry.Password = null;
                }
            })).ToArray();
            int[] results = await Task.WhenAll(queues);
            return results.Any(result => result != 0) ? 2 : 0;
        }

        private static string AccountQueueKey(CharacterLaunch entry)
        {
            // Production requests have already been validated and resolved. The fallback
            // retains the standalone scheduler's legacy, targetless input contract.
            string endpoint = entry.ResolvedTarget?.EndpointKey;
            if (endpoint == null)
            {
                string server = IPAddress.TryParse(entry.Server, out var address) ? address.ToString() : entry.Server;
                endpoint = server + ":" + entry.Port;
            }
            return endpoint + "\0" + entry.Account.Trim();
        }

        internal static void ReportCharacter(CharacterLaunch entry, string name)
        {
            Console.WriteLine("AOQL_CHARACTER " + JsonSerializer.Serialize(new {
                AccountId = entry.AccountId, CharacterId = entry.CharacterId, CharacterName = name
            }));
        }

        internal static bool TryGetCharacterName(string title, out string name)
        {
            const string prefix = "Anarchy Online - ";
            name = null;
            if (title == null || !title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string candidate = title.Substring(prefix.Length);
            if (candidate.Any(char.IsControl)) return false;
            candidate = candidate.Trim();
            if (candidate.Length == 0) return false;
            name = candidate;
            return true;
        }

        internal static bool IsCharacterWindow(string title, string characterName)
        {
            return TryGetCharacterName(title, out string actualName) &&
                string.Equals(actualName, characterName?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>How waiting for a character ended, without the window title itself (it can contain names).</summary>
        internal sealed class CharacterWait
        {
            public string Outcome { get; set; } = "timeout";
            public double Seconds { get; set; }
            public int? ExitCode { get; set; }
            public string WindowState { get; set; } = "no window";
            public int TitleChanges { get; set; }
            public double? NameFirstSeenSeconds { get; set; }
            // Texts of Windows dialogs the client showed, e.g. "No write access to the main AO folder".
            public List<string> Dialogs { get; } = new List<string>();
        }

        private static async Task<string> WaitForCharacterAsync(Process client, int timeoutSeconds, CharacterWait wait = null)
        {
            var timer = Stopwatch.StartNew();
            int stableSamples = 0;
            string previousName = null;
            string previousTitle = null;
            wait = wait ?? new CharacterWait();
            long nextDialogCheck = 0;
            try
            {
                while (timer.Elapsed.TotalSeconds < timeoutSeconds)
                {
                    client.Refresh();
                    if (timer.ElapsedMilliseconds >= nextDialogCheck && wait.Dialogs.Count < 3)
                    {
                        nextDialogCheck = timer.ElapsedMilliseconds + 1000;
                        // Dialog lookups enumerate windows; once a second is plenty.
                        foreach (string text in ClientDialogs.Read(client.Id))
                            if (!wait.Dialogs.Contains(text) && wait.Dialogs.Count < 3) wait.Dialogs.Add(text);
                    }
                    if (client.HasExited)
                    {
                        wait.Outcome = "client exited";
                        try { wait.ExitCode = client.ExitCode; } catch (InvalidOperationException) { }
                        return null;
                    }
                    string title = client.MainWindowTitle;
                    if (!string.Equals(title, previousTitle, StringComparison.Ordinal)) { wait.TitleChanges++; previousTitle = title; }
                    wait.WindowState = DescribeWindow(client, title);
                    if (TryGetCharacterName(title, out string actualName))
                    {
                        if (wait.NameFirstSeenSeconds == null) wait.NameFirstSeenSeconds = Math.Round(timer.Elapsed.TotalSeconds, 1);
                        stableSamples = string.Equals(actualName, previousName, StringComparison.Ordinal) ? stableSamples + 1 : 1;
                        previousName = actualName;
                        if (stableSamples >= 3) { wait.Outcome = "in game"; return actualName; }
                    }
                    else { stableSamples = 0; previousName = null; }
                    await Task.Delay(150);
                }
                return null;
            }
            finally { wait.Seconds = Math.Round(timer.Elapsed.TotalSeconds, 1); }
        }

        private static string DescribeWindow(Process client, string title)
        {
            if (client.MainWindowHandle == IntPtr.Zero) return "no window";
            if (string.IsNullOrEmpty(title)) return "window without title";
            if (TryGetCharacterName(title, out _)) return "character window";
            if (title.StartsWith("Anarchy Online", StringComparison.OrdinalIgnoreCase)) return "game window (login, character selection or loading)";
            return "other window title";
        }

        internal static void Diagnostic(string name, CharacterLaunch entry, object details)
        {
            // Read by AO Nexus into its anonymised diagnostics log; never shown in the activity log.
            try { Console.WriteLine("AOQL_DIAG " + name + (entry != null ? " ID " + entry.CharacterId : "") + " " + JsonSerializer.Serialize(details)); }
            catch (Exception) { }
        }

        private static int? SafeProcessId(Process client)
        {
            try { return client.Id; }
            catch (InvalidOperationException) { return null; }
        }

        private static int SafeRunningClients()
        {
            try
            {
                var clients = Process.GetProcessesByName("AnarchyOnline");
                foreach (var item in clients) item.Dispose();
                return clients.Length;
            }
            catch (Exception) { return -1; }
        }
    }
}
