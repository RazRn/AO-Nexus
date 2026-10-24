using AOLauncher.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace AOQuickLauncher
{
    internal static class ClientSessions
    {
        private const int MaximumRecords = 128;
        private const long MaximumFileBytes = 1024 * 1024;
        private const string MutexName = @"Local\AOQuickLauncher.ActiveClients";
        private static readonly object Sync = new object();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { MaxDepth = 8 };

        internal static string TryGetRunningName(int characterId, string gamePath)
        {
            // Kept for the legacy internal caller contract. Without target provenance,
            // this overload can only reuse old, targetless records.
            return FindRunningName(record => record.CharacterId == characterId
                && SamePath(record.GamePath, NormalizePath(gamePath)) && IsLegacy(record));
        }

        internal static string TryGetRunningName(CharacterLaunch entry, LaunchTarget target)
        {
            return FindRunningName(record => MatchesIdentity(record, entry, target));
        }

        internal static bool HasPending(CharacterLaunch entry, LaunchTarget target)
        {
            return FindRunningName(record => record.Pending && MatchesIdentity(record, entry, target), true) != null;
        }

        internal static IReadOnlyList<ClientRecord> ReadValidatedRecords()
        {
            try
            {
                lock (Sync)
                using (var mutex = new Mutex(false, MutexName))
                {
                    if (!TryAcquire(mutex)) throw new IOException();
                    try
                    {
                        var active = new List<ClientRecord>();
                        foreach (var record in ReadRecords(true))
                            if (record != null && TryValidate(record, out _, true)) active.Add(record);
                        return active;
                    }
                    finally { Release(mutex); }
                }
            }
            catch
            {
                throw new InvalidOperationException(Texts.T("The active client list could not be checked. Try again before starting more accounts."));
            }
        }

        private static string FindRunningName(Func<ClientRecord, bool> matches, bool includePending = false)
        {
            try
            {
                lock (Sync)
                {
                    using (var mutex = new Mutex(false, MutexName))
                    {
                        if (!TryAcquire(mutex)) return null;
                        try
                        {
                            foreach (var record in ReadRecords())
                            {
                                if (record != null && matches(record)
                                    && TryValidate(record, out string name, includePending))
                                    return name ?? string.Empty;
                            }
                        }
                        finally { Release(mutex); }
                    }
                }
            }
            catch { } // Reuse is best-effort; limited queues perform a separate strict preflight.
            return null;
        }

        internal static void Record(CharacterLaunch entry, LaunchTarget target, Process client, string actualName)
        {
            SaveRecord(entry, target, client, actualName, false);
        }

        internal static bool TryRecordCreated(CharacterLaunch entry, LaunchTarget target, Process client, string actualName)
        {
            return SaveRecord(entry, target, client, actualName, false);
        }

        internal static void RecordPending(CharacterLaunch entry, LaunchTarget target, Process client, bool required)
        {
            // Reserve immediately so a canceled/timed-out login cannot disappear from
            // the next queue's account count while its game process is still open.
            if (!SaveRecord(entry, target, client, null, true) && required)
            {
                ReportWriteFailure();
                throw new InvalidOperationException(Texts.T("The active game client could not be saved to the session registry."));
            }
        }

        private static bool SaveRecord(CharacterLaunch entry, LaunchTarget target, Process client, string actualName, bool pending)
        {
            string temporaryPath = null;
            try
            {
                if (entry == null || (entry.CreateCharacter ? entry.CharacterId != 0 || !pending : entry.CharacterId <= 0) || target == null || client == null
                    || (!pending && string.IsNullOrWhiteSpace(actualName))) return false;
                string normalizedPath = NormalizePath(target.GamePath);
                client.Refresh();
                if (client.HasExited || !SameExecutable(client, normalizedPath)) return false;
                string observedName = null;
                if (!pending && (!LaunchQueue.TryGetCharacterName(client.MainWindowTitle, out observedName)
                    || !string.Equals(observedName, actualName.Trim(), StringComparison.OrdinalIgnoreCase))) return false;

                var current = new ClientRecord
                {
                    AccountIdentity = AccountIdentity(entry),
                    Username = entry.Account.Trim().ToUpperInvariant(),
                    Pending = pending,
                    CreateCharacter = entry.CreateCharacter,
                    Server = target.Server,
                    Port = target.Port,
                    CharacterId = entry.CharacterId,
                    GamePath = normalizedPath,
                    ProcessId = client.Id,
                    StartTimeUtcTicks = client.StartTime.ToUniversalTime().Ticks,
                    CharacterName = observedName
                };

                lock (Sync)
                {
                    using (var mutex = new Mutex(false, MutexName))
                    {
                        if (!TryAcquire(mutex))
                        {
                            ReportWriteFailure();
                            return false;
                        }
                        try
                        {
                            var records = new List<ClientRecord>();
                            foreach (var record in ReadRecords(true))
                            {
                                if (record == null || MatchesIdentity(record, entry, target)
                                    || record.ProcessId == current.ProcessId) continue;
                                if (TryValidate(record, out _, true)) records.Add(record);
                            }
                            if (records.Count >= MaximumRecords) throw new IOException();
                            records.Add(current);

                            string destination = CachePath();
                            Directory.CreateDirectory(Path.GetDirectoryName(destination));
                            temporaryPath = Path.Combine(Path.GetDirectoryName(destination),
                                "ActiveClients." + Guid.NewGuid().ToString("N") + ".tmp");
                            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(records, JsonOptions), new UTF8Encoding(false));
                            // Same-directory rename publishes one complete snapshot; the mutex covers the read/modify/write.
                            File.Move(temporaryPath, destination, true);
                            temporaryPath = null;
                            return true;
                        }
                        finally { Release(mutex); }
                    }
                }
            }
            catch { ReportWriteFailure(); return false; }
            finally
            {
                if (temporaryPath != null)
                {
                    try { File.Delete(temporaryPath); }
                    catch { }
                }
            }
        }

        internal static bool MatchesIdentity(ClientRecord record, CharacterLaunch entry, LaunchTarget target)
        {
            if (record == null || entry == null || target == null
                || (entry.CreateCharacter ? entry.CharacterId != 0 : entry.CharacterId <= 0)
                || record.CreateCharacter != entry.CreateCharacter
                || record.CharacterId != entry.CharacterId || !SamePath(record.GamePath, NormalizePath(target.GamePath))) return false;
            if (IsLegacy(record))
                return !entry.HasExplicitTarget && string.IsNullOrWhiteSpace(entry.AccountId);
            return string.Equals(record.AccountIdentity, AccountIdentity(entry), StringComparison.OrdinalIgnoreCase)
                && string.Equals(record.Server, target.Server, StringComparison.Ordinal)
                && record.Port == target.Port;
        }

        private static string AccountIdentity(CharacterLaunch entry)
        {
            return !string.IsNullOrWhiteSpace(entry.AccountId) ? "id:" + entry.AccountId.Trim()
                : "username:" + entry.Account.Trim();
        }

        private static bool IsLegacy(ClientRecord record)
        {
            return record.AccountIdentity == null && record.Server == null && record.Port == 0;
        }

        private static List<ClientRecord> ReadRecords(bool strict = false)
        {
            try
            {
                string path = CachePath();
                using (var stream = File.OpenRead(path))
                {
                    if (stream.Length > MaximumFileBytes) throw new IOException();
                    var records = JsonSerializer.Deserialize<List<ClientRecord>>(stream, JsonOptions);
                    if (records == null || records.Count > MaximumRecords) throw new IOException();
                    return records;
                }
            }
            catch (FileNotFoundException) { return new List<ClientRecord>(); }
            catch (DirectoryNotFoundException) { return new List<ClientRecord>(); }
            catch { if (strict) throw; return new List<ClientRecord>(); }
        }

        private static bool TryValidate(ClientRecord record, out string name, bool includePending = false)
        {
            name = null;
            try
            {
                if (!HasValidCharacterIdentity(record) || record.ProcessId <= 0 || record.StartTimeUtcTicks <= 0
                    || string.IsNullOrWhiteSpace(record.GamePath)) return false;
                using (var client = Process.GetProcessById(record.ProcessId))
                {
                    if (client.HasExited) return false;
                    return MatchesObservedProcess(record, client.Id, client.StartTime.ToUniversalTime().Ticks,
                        client.MainModule?.FileName, client.MainWindowTitle, includePending, out name);
                }
            }
            catch { return false; }
        }

        internal static bool MatchesObservedProcess(ClientRecord record, int processId, long startTimeUtcTicks,
            string executable, string title, bool includePending, out string name)
        {
            name = null;
            if (!HasValidCharacterIdentity(record) || record.ProcessId <= 0 || record.StartTimeUtcTicks <= 0
                || record.ProcessId != processId || record.StartTimeUtcTicks != startTimeUtcTicks
                || string.IsNullOrWhiteSpace(record.GamePath) || string.IsNullOrWhiteSpace(executable)) return false;
            try
            {
                if (!string.Equals(Path.GetFullPath(executable), Path.Combine(NormalizePath(record.GamePath), "AnarchyOnline.exe"),
                    StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch { return false; }
            bool hasName = LaunchQueue.TryGetCharacterName(title, out string observedName);
            if (record.CreateCharacter)
            {
                // A newly created character's ID is unknown. Reserve its process,
                // but never report it as the existing character requested by a queue.
                if (includePending && hasName) name = observedName;
                return includePending;
            }
            if (record.Pending)
            {
                if (hasName) name = observedName;
                return includePending || hasName;
            }
            if (!hasName || !string.Equals(observedName, record.CharacterName, StringComparison.OrdinalIgnoreCase)) return false;
            name = observedName;
            return true;
        }

        private static bool HasValidCharacterIdentity(ClientRecord record)
        {
            return record != null && (record.CreateCharacter
                ? record.CharacterId == 0 && record.Pending
                    && !string.IsNullOrWhiteSpace(record.AccountIdentity)
                    && !string.IsNullOrWhiteSpace(record.Server) && record.Port > 0
                : record.CharacterId > 0);
        }

        private static bool SameExecutable(Process client, string gamePath)
        {
            string actual = client.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(actual) && string.Equals(Path.GetFullPath(actual),
                Path.Combine(gamePath, "AnarchyOnline.exe"), StringComparison.OrdinalIgnoreCase);
        }

        private static bool SamePath(string left, string right)
        {
            try { return string.Equals(NormalizePath(left), right, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(Texts.T("Missing game path."));
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }

        private static string CachePath()
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(folder)) throw new IOException(Texts.T("Local application data is unavailable."));
            return Path.Combine(folder, "AOQuickLauncher", "ActiveClients.json");
        }

        private static bool TryAcquire(Mutex mutex)
        {
            // Keep independent launcher processes from losing each other's entries, without an unbounded wait.
            try { return mutex.WaitOne(250); }
            catch (AbandonedMutexException) { return true; }
        }

        private static void Release(Mutex mutex)
        {
            try { mutex.ReleaseMutex(); }
            catch { }
        }

        private static void ReportWriteFailure()
        {
            try { Console.Error.WriteLine(Texts.T("The active game client could not be saved to the session registry.")); }
            catch { }
        }

        internal sealed class ClientRecord
        {
            public ClientRecord() { }
            public string AccountIdentity { get; set; }
            public string Username { get; set; }
            public bool Pending { get; set; }
            public bool CreateCharacter { get; set; }
            public string Server { get; set; }
            public int Port { get; set; }
            public int CharacterId { get; set; }
            public string GamePath { get; set; }
            public int ProcessId { get; set; }
            public long StartTimeUtcTicks { get; set; }
            public string CharacterName { get; set; }
        }
    }
}
