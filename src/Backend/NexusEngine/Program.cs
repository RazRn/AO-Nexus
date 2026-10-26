using AOLauncher.Localization;
using System;
using System.Diagnostics;
using System.IO;
using Reloaded.Injector;

namespace NexusEngine
{
    // Nexus-only worker protocol. Credentials are accepted through private stdin,
    // never through the former standalone launcher's positional command line.
    public class Program
    {
        public class Options
        {
            public string Username { get; set; }
            public string Password { get; set; }
            public int CharacterId { get; set; }
            public bool CreateCharacter { get; set; }
            internal string CharacterPipe { get; set; }
            internal string CampToken { get; set; }
            public bool Quiet { get; set; }
            public bool FastStartup { get; set; }
            public string StatusPipe { get; set; }
            internal string LoginGate { get; set; }
        }

        private static int Main(string[] args)
        {
            Console.InputEncoding = new System.Text.UTF8Encoding(false);
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            try
            {
                if (args.Length == 1 && (args[0] == "--launch-stdin" || args[0] == "--validate-stdin"))
                    return LaunchQueue.FromStandardInput(args[0] == "--validate-stdin");
                Console.Error.WriteLine("NexusEngine is an internal AO Nexus component. Start AONexus.exe.");
                return 1;
            }
            catch (Exception ex)
            {
                // Never print the credential-bearing input request or command line.
                Console.Error.WriteLine(Texts.F("Error: {0}", ex.Message));
                return 1;
            }
        }
        internal static Process SpawnAOProcess(Options options, LaunchTarget target)
        {
            Process client = new Process();
            client.StartInfo.WorkingDirectory = target.GamePath;
            client.StartInfo.FileName = Path.Combine(target.GamePath, "AnarchyOnline.exe");
            client.StartInfo.Arguments = target.Arguments;
            client.StartInfo.UseShellExecute = false;
            client.StartInfo.Environment["AOUser"] = options.Username;
            client.StartInfo.Environment["AOPass"] = options.Password;
            client.StartInfo.Environment["AOCharId"] = options.CharacterId.ToString();
            client.StartInfo.Environment["AOCreateCharacter"] = Convert.ToString(options.CreateCharacter);
            client.StartInfo.Environment.Remove("AONexusCampToken");
            if (!options.CreateCharacter && options.CampToken != null)
                client.StartInfo.Environment["AONexusCampToken"] = options.CampToken;
            // This one-use pipe identifies only the client spawned for this creation.
            // Never inherit a reporter destination into a normal character login.
            if (options.CreateCharacter && !string.IsNullOrEmpty(options.CharacterPipe))
                client.StartInfo.Environment["AOCharacterPipe"] = options.CharacterPipe;
            else client.StartInfo.Environment.Remove("AOCharacterPipe");
            if (!options.CreateCharacter && options.StatusPipe != null && options.StatusPipe.StartsWith(LoginStatusChannel.PipePrefix, StringComparison.Ordinal))
                client.StartInfo.Environment["AONexusStatusPipe"] = options.StatusPipe;
            else client.StartInfo.Environment.Remove("AONexusStatusPipe");
            client.StartInfo.Environment["QuietLaunch"] = Convert.ToString(options.Quiet);
            client.StartInfo.Environment.Remove("AONexusLoginGate");
            client.StartInfo.Environment.Remove("AONexusLoginGateParent");
            if (!options.CreateCharacter && options.LoginGate != null)
            {
                client.StartInfo.Environment["AONexusLoginGate"] = options.LoginGate;
                client.StartInfo.Environment["AONexusLoginGateParent"] = Environment.ProcessId.ToString();
            }
            // Always overwrite inherited values. Creation and unknown builds retain normal loading.
            bool fast = options.FastStartup && !options.CreateCharacter && NexusEngine.FastStartup.IsSupported(target.GamePath);
            client.StartInfo.Environment["AONexusFastStartupValidated"] = Convert.ToString(fast);
            if (options.FastStartup)
                Console.WriteLine(fast ? "Fast loading: verified classic client." : "Fast loading unavailable for this client; using normal loading.");
            client.StartInfo.Environment["NoChat"] = "False";
            client.Start();

            try
            {
                using (var injector = new Injector(client))
                {
                    if (injector.Inject(Path.Combine(AppContext.BaseDirectory, "LoginHandler.dll")) == 0)
                        throw new InvalidOperationException(Texts.T("LoginHandler.dll could not be loaded into the game client. Check the game window; the login queue has stopped."));
                }
            }
            catch
            {
                client.Dispose();
                throw;
            }

            return client;
        }
    }
}
