using AOLauncher.Localization;
using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NexusEngine
{
    internal sealed class CreatedCharacter
    {
        internal int Id { get; set; }
        internal string Name { get; set; }
    }

    // Owns a reporter pipe and process handle, never the game's lifetime. The queue
    // lock covers Start/reservation only; WaitAsync must run after that lock is released.
    internal sealed class CreationMonitor : IDisposable
    {
        internal const int MaximumFrameBytes = 512;
        internal const string PipePrefix = "AONexus.Created.";
        private readonly CharacterLaunch entry;
        private readonly LaunchTarget target;
        private readonly NamedPipeServerStream pipe;
        private Process client;
        private int processId;
        private long startTimeUtcTicks;

        private CreationMonitor(CharacterLaunch entry)
        {
            this.entry = entry;
            target = entry.ResolvedTarget;
            string pipeName = PipePrefix + Guid.NewGuid().ToString("N");
            pipe = NewPipe(pipeName);
            var options = new Program.Options {
                Username = entry.Account, Password = entry.Password, CharacterId = 0,
                CreateCharacter = true, CharacterPipe = pipeName
            };
            try
            {
                client = Program.SpawnAOProcess(options, target);
                processId = client.Id;
                startTimeUtcTicks = client.StartTime.ToUniversalTime().Ticks;
                // ProcessStartInfo is retained for observation; child environment was
                // already copied at Start, so do not retain another password reference.
                client.StartInfo.Environment.Remove("AOPass");
                ClientSessions.RecordPending(entry, target, client, true);
                Console.WriteLine("NEXUS_CREATION_STARTED " + JsonSerializer.Serialize(new {
                    AccountId = entry.AccountId, ProcessId = processId, StartTimeUtcTicks = startTimeUtcTicks
                }));
                Console.WriteLine(Texts.T("Client started for character creation. Continue in the game window; the new character will be added automatically when it enters the game."));
            }
            catch { Dispose(); throw; }
            finally { options.Password = null; entry.Password = null; }
        }

        internal static CreationMonitor Start(CharacterLaunch entry)
        {
            if (entry == null || !entry.CreateCharacter || entry.CharacterId != 0 || entry.ResolvedTarget == null)
                throw new ArgumentException("A valid character-creation request is required.");
            return new CreationMonitor(entry);
        }

        internal static NamedPipeServerStream NewPipe(string name) => new NamedPipeServerStream(
            name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, MaximumFrameBytes, 16);

        internal async Task<int> WaitAsync(TimeSpan timeout)
        {
            using var lifetime = new CancellationTokenSource(timeout);
            Task exitWatcher = WatchGameExitAsync(IsSameProcessAlive, lifetime);
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    CreatedCharacter created = await ReceiveAsync(pipe, processId, lifetime.Token, MatchesCurrentProcess);
                    if (created == null) continue;
                    CharacterLaunch promoted = PromoteEntry(entry, created);
                    // SaveRecord replaces this PID's pending creation reservation under
                    // its own session-registry mutex. Other launches remain unblocked.
                    if (!ClientSessions.TryRecordCreated(promoted, target, client, created.Name))
                    {
                        Console.Error.WriteLine(Texts.T("The new character could not be saved to the active client list. The game remains open; add the character manually."));
                        return 1;
                    }
                    Console.WriteLine("NEXUS_CHARACTER " + JsonSerializer.Serialize(new {
                        AccountId = entry.AccountId, CharacterId = created.Id, CharacterName = created.Name, Created = true
                    }));
                    return 0;
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException)
            {
                Console.Error.WriteLine(Texts.T("Automatic character detection stopped. The game remains open; you can add the character manually."));
                return 1;
            }
            finally
            {
                lifetime.Cancel();
                await exitWatcher;
            }
            Console.WriteLine(IsSameProcessAlive()
                ? Texts.T("Automatic character detection timed out. The game remains open; you can add the character manually.")
                : Texts.T("Character detection stopped because the game closed. No character was added."));
            return 2;
        }

        internal static async Task WatchGameExitAsync(Func<bool> isSameProcessAlive, CancellationTokenSource lifetime)
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    if (!isSameProcessAlive()) { lifetime.Cancel(); return; }
                    await Task.Delay(500, lifetime.Token);
                }
            }
            catch (OperationCanceledException) { }
        }

        private bool IsSameProcessAlive()
        {
            try { client.Refresh(); return !client.HasExited && client.Id == processId && client.StartTime.ToUniversalTime().Ticks == startTimeUtcTicks; }
            catch { return false; }
        }

        private bool MatchesCurrentProcess(CreatedCharacter created)
        {
            try
            {
                if (!IsSameProcessAlive()) return false;
                return MatchesObservation(processId, startTimeUtcTicks, target.GamePath, created,
                    client.Id, client.StartTime.ToUniversalTime().Ticks, client.MainModule?.FileName, client.MainWindowTitle);
            }
            catch { return false; }
        }

        internal static bool MatchesObservation(int expectedId, long expectedStart, string gamePath, CreatedCharacter created,
            int actualId, long actualStart, string executable, string title)
        {
            if (expectedId <= 0 || expectedStart <= 0 || actualId != expectedId || actualStart != expectedStart ||
                created == null || created.Id <= 0 || !ValidName(created.Name) || string.IsNullOrWhiteSpace(gamePath) || string.IsNullOrWhiteSpace(executable)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(executable), Path.Combine(Path.GetFullPath(gamePath), "AnarchyOnline.exe"), StringComparison.OrdinalIgnoreCase)
                    && LaunchQueue.TryGetCharacterName(title, out string observedName)
                    && string.Equals(created.Name, observedName, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (IOException) { return false; }
        }

        internal static CharacterLaunch PromoteEntry(CharacterLaunch original, CreatedCharacter created) => new CharacterLaunch {
            AccountId = original.AccountId, Account = original.Account, CharacterId = created.Id,
            CharacterName = created.Name, CreateCharacter = false, GamePath = original.GamePath,
            Server = original.Server, ServerName = original.ServerName, Port = original.Port,
            MaxAccounts = original.MaxAccounts, MaxCharactersPerAccount = original.MaxCharactersPerAccount,
            ResolvedTarget = original.ResolvedTarget
        };

        // Each connection is bounded independently; a malformed or unrelated client
        // cannot occupy the receiver for the entire creation session.
        internal static async Task<CreatedCharacter> ReceiveAsync(NamedPipeServerStream server, int expectedProcessId, CancellationToken cancellation,
            Func<CreatedCharacter, bool> validate = null)
        {
            await server.WaitForConnectionAsync(cancellation);
            try
            {
                if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out uint sender) || sender != (uint)expectedProcessId) return null;
                using var readLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                readLimit.CancelAfter(TimeSpan.FromSeconds(5));
                var bytes = new byte[MaximumFrameBytes + 1];
                int length = 0;
                while (length < bytes.Length)
                {
                    int read = await server.ReadAsync(bytes.AsMemory(length, bytes.Length - length), readLimit.Token);
                    if (read == 0) break;
                    length += read;
                    if (bytes.Take(length).Count(value => value == (byte)'\n') >= 3) break;
                }
                if (!TryParse(bytes, length, out CreatedCharacter created) || validate != null && !validate(created)) return null;
                await server.WriteAsync(new byte[] { (byte)'O', (byte)'K', (byte)'\n' }, readLimit.Token);
                // The reporter closes after receiving this ACK. Waiting for EOF keeps
                // buffered acknowledgement bytes alive without an unbounded flush.
                int trailing = await server.ReadAsync(bytes.AsMemory(0, 1), readLimit.Token);
                return trailing == 0 ? created : null;
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return null; }
            catch (IOException) when (!cancellation.IsCancellationRequested) { return null; }
            finally
            {
                // A peer that closed its end can make IsConnected false while the
                // instance still needs Disconnect before its next Connect call.
                try { server.Disconnect(); }
                catch (InvalidOperationException) { }
                catch (IOException) { }
            }
        }

        internal static bool TryParse(byte[] bytes, int length, out CreatedCharacter created)
        {
            created = null;
            if (bytes == null || length <= 0 || length > MaximumFrameBytes || length > bytes.Length) return false;
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes, 0, length); }
            catch (DecoderFallbackException) { return false; }
            string[] parts = text.Split('\n');
            if (parts.Length != 4 || parts[0] != "AO_NEXUS_CHAR_V1" || parts[3].Length != 0 ||
                parts[1].Length == 0 || parts[1].Any(c => c < '0' || c > '9') ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0 || !ValidName(parts[2])) return false;
            created = new CreatedCharacter { Id = id, Name = parts[2] };
            return true;
        }

        private static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 64 && name.Trim() == name && !name.Any(char.IsControl);

        public void Dispose() { pipe?.Dispose(); client?.Dispose(); }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
    }
}
