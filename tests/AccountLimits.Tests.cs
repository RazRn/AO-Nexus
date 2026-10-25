using AOLauncher.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AOQuickLauncher
{
    // Compiled with the production policy, queue and session sources. The spawn
    // stub makes accidentally starting a real game impossible in this test binary.
    internal static class Program
    {
        internal sealed class Options
        {
            public string Username { get; set; }
            public string Password { get; set; }
            public int CharacterId { get; set; }
            public bool CreateCharacter { get; set; }
            internal string CharacterPipe { get; set; }
            internal string CampToken { get; set; }
            internal string StatusPipe { get; set; }
            internal bool Quiet { get; set; }
        }
        internal static Process SpawnAOProcess(Options options, LaunchTarget target)
        {
            throw new InvalidOperationException("Game process startup is forbidden in offline tests.");
        }
    }

    internal static class AccountLimitsTests
    {
        private static string gamePath;
        private static int checks;

        public static int Main(string[] args)
        {
            gamePath = args.Single();
            Texts.SetLanguage("en");
            Check("PRK active accounts block a third account", new[] { Char("gamma", 3) },
                new[] { Record("alpha", 1), Record("beta", 2) }, "3 accounts");
            Check("Selected running account is counted once", new[] { Char(" ALPHA ", 1), Char("beta", 2) },
                new[] { Record("alpha", 1) });
            Check("A second char on a tracked account is rejected", new[] { Char("alpha", 2) },
                new[] { Record("ALPHA", 1) }, "2 characters");
            Check("Repeated records for the same char are counted once", new[] { Char("alpha", 1) },
                new[] { Record("alpha", 1), Record("ALPHA", 1) });
            Check("Pending clients reserve account slots", new[] { Char("gamma", 3) },
                new[] { Record("alpha", 1, pending: true), Record("beta", 2, pending: true) }, "3 accounts");
            Check("Pending clients reserve per-account char slots", new[] { Char("alpha", 2) },
                new[] { Record("alpha", 1, pending: true) }, "2 characters");
            Check("Other endpoints do not consume PRK slots", new[] { Char("alpha", 1), Char("beta", 2) },
                new[] { Record("gamma", 3, "192.0.2.1", 7000), Record("delta", 4, "5.161.243.214", 7001) });
            Check("Live permits different account names", new[] { Char("alpha", 1, "37.18.192.41", 7505), Char("beta", 2, "37.18.192.41", 7505) },
                new[] { Record("gamma", 3, "37.18.192.41", 7505) });
            Check("Live char cap includes tracked other dimension", new[] { Char("alpha", 1, "37.18.192.41", 7505) },
                new[] { Record("alpha", 2, "37.18.192.41", 7506) }, "2 characters");
            Check("Identical IDs on different Live dimensions are distinct chars", new[] { Char("alpha", 1, "37.18.192.41", 7505) },
                new[] { Record("alpha", 1, "37.18.192.41", 7506) }, "2 characters");
            Check("Explicit unlimited selected Live char policy is honored", new[] { Char("alpha", 1, "37.18.192.41", 7505, chars: 0) },
                new[] { Record("alpha", 2, "37.18.192.41", 7506) });
            Check("Custom two-char cap rejects tracked third char", new[] { Char("alpha", 1, "192.0.2.1", 7000, chars: 2), Char("alpha", 2, "192.0.2.1", 7000, chars: 2) },
                new[] { Record("alpha", 3, "192.0.2.1", 7000) }, "3 characters");
            Check("Unlimited chars are independent of account cap", new[] { Char("alpha", 1, "192.0.2.1", 7000, accounts: 1), Char("alpha", 2, "192.0.2.1", 7000, accounts: 1) },
                new[] { Record("alpha", 3, "192.0.2.1", 7000) });
            Check("Unlimited endpoints ignore existing client count", new[] { Char("alpha", 1, accounts: 0, chars: 0) },
                new[] { Record("alpha", 2), Record("beta", 3), Record("gamma", 4) });
            var legacy = Record(null, 1);
            legacy.AccountIdentity = "id:fixture-alpha";
            Check("Old saved account ID resolves to selected username", new[] { Char("alpha", 1), Char("beta", 2) }, new[] { legacy });
            legacy = Record(null, 1);
            legacy.AccountIdentity = "username: AlPhA ";
            Check("Old username identity resolves case-insensitively", new[] { Char("alpha", 1), Char("beta", 2) }, new[] { legacy });
            var oldA = Record(null, 8); oldA.AccountIdentity = "id:old-a";
            var oldB = Record(null, 9); oldB.AccountIdentity = "id:old-b";
            Check("Unresolvable old IDs reserve separate accounts", new[] { Char("alpha", 1) }, new[] { oldA, oldB }, "3 accounts");
            var targetless = Record(null, 8); targetless.Server = null; targetless.Port = 0;
            Check("Old targetless records are not guessed onto a server", new[] { Char("alpha", 1), Char("beta", 2) }, new[] { targetless });

            Check("Renamed server appears in tracked account-limit error", new[] { Char("gamma", 3, name: "  Our server  ") },
                new[] { Record("alpha", 1), Record("beta", 2) }, "Server Our server (5.161.243.214:7000) allows");
            Check("Renamed server appears in tracked char-limit error", new[] { Char("alpha", 2, name: "Our server") },
                new[] { Record("alpha", 1) }, "Account on server Our server (5.161.243.214:7000) allows");
            Check("Named cross-dimension selection lists both actual endpoints", new[] {
                Char("alpha", 1, "37.18.192.41", 7505, name: "Original realm"),
                Char("alpha", 2, "37.18.192.41", 7506, name: "New realm") }, Array.Empty<ClientSessions.ClientRecord>(),
                "Account on server Original realm (37.18.192.41:7505), New realm (37.18.192.41:7506) allows");
            Check("Tracked other dimension falls back to actual endpoint", new[] { Char("alpha", 1, "37.18.192.41", 7505, name: "Original realm") },
                new[] { Record("alpha", 2, "37.18.192.41", 7506, pending: true) },
                "Account on server Original realm (37.18.192.41:7505), 37.18.192.41:7506 allows");
            Check("Unrelated selected dimension is omitted from char-limit error", new[] {
                Char("alpha", 1, "37.18.192.41", 7505, name: "Original realm"),
                Char("beta", 2, "37.18.192.41", 7506, name: "Unrelated realm") },
                new[] { Record("alpha", 3, "37.18.192.41", 7505) }, "Account on server Original realm (37.18.192.41:7505) allows");
            Check("Legacy missing names use actual endpoints", new[] { Char("alpha", 1, "37.18.192.41", 7505) },
                new[] { Record("alpha", 2, "37.18.192.41", 7506) },
                "Account on server 37.18.192.41:7505, 37.18.192.41:7506 allows");
            Check("Blank server names use endpoint fallback", new[] { Char("alpha", 2, name: "   ") },
                new[] { Record("alpha", 1) }, "Account on server 5.161.243.214:7000 allows");
            Check("Control characters in display names cannot enter errors", new[] { Char("alpha", 2, name: "Untrusted\r\nname") },
                new[] { Record("alpha", 1) }, "Account on server 5.161.243.214:7000 allows");
            Check("Danish errors preserve configured names", new[] { Char("alpha", 2, name: "Vores server") },
                new[] { Record("alpha", 1) }, "En konto på server Vores server (5.161.243.214:7000) tillader", "da");

            Check("Creation request uses explicit mode and ID zero", new[] { Creation("alpha") }, Array.Empty<ClientSessions.ClientRecord>());
            Check("Normal login still rejects ID zero", new[] { Char("alpha", 0) }, Array.Empty<ClientSessions.ClientRecord>(), "positive character ID");
            var invalidCreation = Creation("alpha"); invalidCreation.CharacterId = 9;
            Check("Creation cannot select an existing positive ID", new[] { invalidCreation }, Array.Empty<ClientSessions.ClientRecord>(), "character ID 0");
            Check("Creation cannot be mixed with normal logins", new[] { Creation("alpha"), Char("beta", 2) }, Array.Empty<ClientSessions.ClientRecord>(), "one account at a time");
            Check("Creation cannot be batched for two accounts", new[] { Creation("alpha"), Creation("beta") }, Array.Empty<ClientSessions.ClientRecord>(), "one account at a time");
            Check("Creation respects active server account limit", new[] { Creation("gamma") },
                new[] { Record("alpha", 1), Record("beta", 2) }, "3 accounts");
            Check("Creation reserves a simultaneous char slot", new[] { Creation("alpha") },
                new[] { Record("alpha", 1) }, "2 characters");
            Check("Unlimited policy permits creation beside an existing char", new[] { Creation("alpha", chars: 0) },
                new[] { Record("alpha", 1) });
            Check("Open creation blocks a duplicate even on unlimited servers", new[] { Creation(" ALPHA ", accounts: 0, chars: 0, name: "Our server") },
                new[] { CreationRecord("alpha") }, "already open for this account on Our server (5.161.243.214:7000)");
            var aliasCreation = Creation("alpha", accounts: 0, chars: 0); aliasCreation.AccountId = "different-saved-account";
            Check("Saved aliases cannot launch duplicate creation clients", new[] { aliasCreation },
                new[] { CreationRecord("alpha") }, "already open");
            Check("Other accounts may create characters independently", new[] { Creation("beta") }, new[] { CreationRecord("alpha") });
            Check("Other endpoints do not block creation", new[] { Creation("alpha") }, new[] { CreationRecord("alpha", "192.0.2.2") });
            Check("Normal login counts an active creation reservation", new[] { Char("alpha", 1) },
                new[] { CreationRecord("alpha") }, "Character creation is still open");
            Check("Unlimited normal login waits for same-account creation", new[] { Char(" ALPHA ", 1, accounts: 0, chars: 0) },
                new[] { CreationRecord("alpha") }, "Character creation is still open");
            Check("Unlimited normal login on another account may continue", new[] { Char("beta", 1, accounts: 0, chars: 0) },
                new[] { CreationRecord("alpha") });
            Check("Same username on another server may log in", new[] { Char("alpha", 1, "192.0.2.2", accounts: 0, chars: 0) },
                new[] { CreationRecord("alpha") });
            Check("Creation reservations count toward server account limits", new[] { Char("gamma", 3) },
                new[] { CreationRecord("alpha"), CreationRecord("beta") }, "3 accounts");
            var secondCreation = CreationRecord("alpha"); secondCreation.ProcessId = 124;
            Check("Distinct unknown creation clients are not collapsed", new[] { Char("alpha", 1, chars: 2) },
                new[] { CreationRecord("alpha"), secondCreation }, "Character creation is still open");

            var valid = Record("alpha", 1);
            Observed("Matching PID, creation time, executable and title accepted", valid, true);
            Observed("PID mismatch rejected", valid, false, pid: 124);
            Observed("Reused PID with different start time rejected", valid, false, start: 457);
            Observed("Different executable rejected", valid, false, executable: Path.Combine(gamePath, "Other.exe"));
            Observed("Different game installation rejected", valid, false, executable: Path.Combine(gamePath, "other", "AnarchyOnline.exe"));
            Observed("Wrong character window rejected", valid, false, title: "Anarchy Online - Other");
            Observed("Login window is not an active named record", valid, false, title: "Anarchy Online");
            var pending = Record("alpha", 1, pending: true);
            Observed("Pending login window reserves a slot", pending, true, title: "Anarchy Online");
            Observed("Pending wrong executable rejected", pending, false, executable: Path.Combine(gamePath, "Other.exe"));
            Observed("Pending reused PID rejected", pending, false, start: 457);
            Observed("Pending client is not reused before character appears", pending, false, title: "Anarchy Online", includePending: false);
            Observed("Pending client can be reused once its char appears", pending, true, includePending: false);
            var creation = CreationRecord("alpha");
            Observed("Creation screen reserves its PID and executable", creation, true, title: "Anarchy Online");
            Observed("Creation reservation survives a later character login", creation, true);
            Observed("Creation cannot be reused as a known character", creation, false, includePending: false);
            Observed("Creation PID reuse is rejected", creation, false, start: 457);
            Observed("Creation wrong executable is rejected", creation, false, executable: Path.Combine(gamePath, "Other.exe"));
            creation.Pending = false;
            Observed("Creation must remain an unknown pending identity", creation, false);
            creation = CreationRecord("alpha"); creation.CharacterId = 1;
            Observed("Creation flag with a positive ID is rejected", creation, false);
            creation = CreationRecord("alpha"); creation.CreateCharacter = false;
            Observed("Legacy zero-ID records are not accepted", creation, false);
            LoginStatusChecks();
            Console.WriteLine("PASS: " + checks + " tracked-session and two-cap policy checks using synthetic records only.");
            return 0;
        }

        private static CharacterLaunch Char(string account, int id, string server = "5.161.243.214", int port = 7000,
            int? accounts = null, int? chars = null, string name = null)
        {
            return new CharacterLaunch { Account = account, AccountId = "fixture-" + account.Trim().ToLowerInvariant(), Password = "OFFLINE_FIXTURE",
                CharacterId = id, CharacterName = "Fixture", GamePath = gamePath, Server = server, Port = port,
                MaxAccounts = accounts, MaxCharactersPerAccount = chars, ServerName = name };
        }

        private static ClientSessions.ClientRecord Record(string username, int id, string server = "5.161.243.214", int port = 7000, bool pending = false)
        {
            return new ClientSessions.ClientRecord { Username = username, AccountIdentity = "id:fixture-" + username,
                CharacterId = id, CharacterName = pending ? null : "Fixture", Pending = pending, Server = server, Port = port,
                GamePath = gamePath, ProcessId = 123, StartTimeUtcTicks = 456 };
        }

        private static CharacterLaunch Creation(string account, int? accounts = null, int? chars = null, string name = null)
        {
            var entry = Char(account, 0, accounts: accounts, chars: chars, name: name);
            entry.CreateCharacter = true;
            return entry;
        }

        private static ClientSessions.ClientRecord CreationRecord(string username, string server = "5.161.243.214")
        {
            var record = Record(username, 0, server, pending: true);
            record.CreateCharacter = true;
            return record;
        }

        private static void Pass(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + name);
            checks++;
            Console.WriteLine("PASS: " + name);
        }

        private static void LoginStatusChecks()
        {
            Pass(LoginStatusChannel.TryParse("AO_NEXUS_STATUS_V1 1234 connect rc=1", out string stage) && stage == "connect rc=1", "Status line is parsed");
            Pass(LoginStatusChannel.TryParse("AO_NEXUS_STATUS_V1 5 screen -1", out stage) && stage == "screen -1", "Negative screen states are accepted");
            Pass(!LoginStatusChannel.TryParse("AO_NEXUS_STATUS_V1 5 user=Alice", out _), "Uppercase text (names) is rejected");
            Pass(!LoginStatusChannel.TryParse("AO_NEXUS_STATUS_V1 5 pass:secret!", out _), "Unexpected punctuation is rejected");
            Pass(!LoginStatusChannel.TryParse("AO_NEXUS_STATUS_V2 5 screen 3", out _) && !LoginStatusChannel.TryParse("AO_NEXUS_STATUS_V1 x screen 3", out _), "Unknown versions and timestamps are rejected");
            Pass(!LoginStatusChannel.TryParse("AO_NEXUS_STATUS_V1 5 " + new string('a', 200), out _), "Oversized reports are rejected");
            Pass(ClientDialogs.Clean("No write access\r\nto   the main AO folder") == "No write access to the main AO folder", "Dialog text is flattened");
            Pass(ClientDialogs.Clean(new string('x', 300)).Length == 201, "Dialog text is bounded");
            var request = new LaunchRequest { Characters = new List<CharacterLaunch> { Char("alpha", 1) }, ReadyTimeoutSeconds = 10, StartSpacingMs = 20000 };
            bool rejected = false;
            try { LaunchQueue.ValidateRequest(request); } catch (ArgumentException) { rejected = true; }
            Pass(rejected && new LaunchRequest().StartSpacingMs == 1500 && !new LaunchRequest().QuietLaunch, "Start spacing defaults to 1.5 s and is bounded; quiet launch is opt-in");
            StartSpacing.Reset();
            var watch = Stopwatch.StartNew();
            Pass(StartSpacing.WaitTurnAsync("C:\\A", 300).GetAwaiter().GetResult() == 0, "First start in a folder does not wait");
            var other = StartSpacing.WaitTurnAsync("C:\\B", 300).GetAwaiter().GetResult();
            var second = StartSpacing.WaitTurnAsync("c:\\a", 300).GetAwaiter().GetResult();
            Pass(other == 0 && second >= 250 && watch.ElapsedMilliseconds >= 250, "Starts in the same folder are spaced; other folders are not");
            var parallel = Task.WhenAll(Enumerable.Range(0, 3).Select(_ => StartSpacing.WaitTurnAsync("C:\\C", 200))).GetAwaiter().GetResult().OrderBy(value => value).ToArray();
            Pass(parallel[0] < 100 && parallel[1] >= 150 && parallel[2] >= 350, "Parallel queues get distinct start slots");
            StartSpacing.Reset();
            var clock = Stopwatch.StartNew();
            var stages = new List<string>();
            using (var channel = LoginStatusChannel.TryStart(clock, (stage, ms) => { lock (stages) stages.Add(stage); }))
            {
                Pass(channel != null && channel.PipeName.StartsWith(LoginStatusChannel.PipePrefix, StringComparison.Ordinal) && !channel.Requested.IsCompleted, "Status channel starts without a request");
                using (var writer = new System.IO.Pipes.NamedPipeClientStream(".", channel.PipeName, System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.CurrentUserOnly))
                {
                    writer.Connect(2000);
                    var bytes = System.Text.Encoding.ASCII.GetBytes("AO_NEXUS_STATUS_V1 10 login-screen\nAO_NEXUS_STATUS_V1 20 user=Secret\nAO_NEXUS_STATUS_V1 30 character-list\nAO_NEXUS_STATUS_V1 31 charac");
                    writer.Write(bytes, 0, bytes.Length); writer.Flush();
                    Thread.Sleep(200);
                    Pass(!channel.Requested.IsCompleted, "A partial line does not count as requested");
                    bytes = System.Text.Encoding.ASCII.GetBytes("ter-requested\n");
                    writer.Write(bytes, 0, bytes.Length); writer.Flush();
                    Pass(channel.Requested.Wait(2000), "Character requested completes the overlap signal");
                }
                string summary = channel.Summary();
                Pass(summary.Contains("s login-screen") && summary.Contains("s character-requested") && !summary.Contains("Secret") && !summary.Contains("@"), "Timeline keeps valid stages only, without '@'");
                lock (stages) Pass(stages.SequenceEqual(new[] { "login-screen", "character-list", "character-requested" }), "Stages are reported in order");
            }
            Pass(new LaunchRequest().OverlapLogins == false, "Overlapping logins are opt-in");
        }

        private static void Check(string name, CharacterLaunch[] selected, ClientSessions.ClientRecord[] records, string error = null, string language = "en")
        {
            Texts.SetLanguage(language);
            try
            {
                LaunchQueue.ValidateRequest(new LaunchRequest { Characters = selected.ToList(), ReadyTimeoutSeconds = 10 });
                LaunchQueue.ValidateAccountLimits(selected, records);
                if (error != null) throw new Exception("Expected rejection: " + name);
            }
            catch (ArgumentException ex)
            {
                if (error == null || !ex.Message.Contains(error)) throw new Exception("Incorrect result: " + name, ex);
            }
            checks++;
            Console.WriteLine("PASS: " + name);
        }

        private static void Observed(string name, ClientSessions.ClientRecord record, bool expected, int pid = 123, long start = 456,
            string executable = null, string title = "Anarchy Online - Fixture", bool includePending = true)
        {
            bool result = ClientSessions.MatchesObservedProcess(record, pid, start, executable ?? Path.Combine(gamePath, "AnarchyOnline.exe"), title, includePending, out _);
            if (result != expected) throw new Exception("Incorrect process validation: " + name);
            checks++;
            Console.WriteLine("PASS: " + name);
        }
    }
}
