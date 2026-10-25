using AOLauncher.Localization;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using Reloaded.Injector;
using CommandLine.Text;
using CommandLine;
using System.Runtime.InteropServices;
using System.Reflection.Metadata;
using System.Drawing;

namespace AOQuickLauncher
{
    public class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, int uFlags);
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_SHOWWINDOW = 0x0040;

        public class Options
        {
            [Value(0, MetaName = "Username", Required = true)]
            public string Username { get; set; }

            [Value(1, MetaName = "Password", Required = true)]
            public string Password { get; set; }

            [Value(2, MetaName="Character ID", Required = true)]
            public int CharacterId { get; set; }

            public bool CreateCharacter { get; set; }
            internal string CharacterPipe { get; set; }
            internal string CampToken { get; set; }

            [Option('q', "quiet", Required = false, HelpText = "Quiet Mode")]
            public bool Quiet { get; set; }

            [Option('x', "nochat", Required = false, HelpText = "Client will not connect to chat server")]
            public bool NoChat { get; set; }

            // Set by the login queue only; LoginHandler reports login stages to this pipe.
            public string StatusPipe { get; set; }

            [Option('s', "rk19", Required = false, HelpText = "Connect to RK19")]
            public bool Rubika19 { get; set; }

            [Option("server", HelpText = "Login server IPv4 address.")]
            public string Server { get; set; }

            [Option("port", HelpText = "Login server port.")]
            public int? Port { get; set; }

            [Option("game-path", HelpText = "Folder containing AnarchyOnline.exe.")]
            public string GamePath { get; set; }

            [Option("check", HelpText = "Validate and display the target without starting the game.")]
            public bool CheckOnly { get; set; }

            [Option('X', "posx", Required = false, HelpText = "X position to move window to")]
            public int PosX { get; set; } = int.MaxValue;

            [Option('Y', "posy", Required = false, HelpText = "Y position to move window to")]
            public int PosY { get; set; } = int.MaxValue;
        }

        private static int Main(string[] args)
        {
            Console.InputEncoding = new System.Text.UTF8Encoding(false);
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            try
            {
                if (args.Length == 1 && (args[0] == "--launch-stdin" || args[0] == "--validate-stdin"))
                    return LaunchQueue.FromStandardInput(args[0] == "--validate-stdin");

                return Parser.Default.ParseArguments<Options>(args).MapResult(options =>
                {
                    if (options.CharacterId <= 0)
                        throw new ArgumentException(Texts.T("Character ID must be a positive number."));
                    if (options.Rubika19 && (options.Server != null || options.Port.HasValue))
                        throw new ArgumentException(Texts.T("Use either --rk19 or a custom --server/--port."));
                    var target = LaunchTarget.Create(options.GamePath, options.Server,
                        options.Port ?? (options.Rubika19 ? 7506 : 7505));
                    if (options.CheckOnly)
                    {
                        Console.WriteLine(Texts.F("Game: {0}", target.GamePath));
                        Console.WriteLine(Texts.F("Endpoint: {0}:{1}", target.Server, target.Port));
                        Console.WriteLine(Texts.F("Arguments: {0}", target.Arguments));
                        Console.WriteLine(Texts.T("Validation only; no game was started."));
                        return 0;
                    }
                    using (var client = SpawnAOProcess(options, target)) { }
                    return 0;
                }, errors => errors.All(error => error is HelpRequestedError || error is VersionRequestedError) ? 0 : 1);
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
            client.StartInfo.Environment["NoChat"] = Convert.ToString(options.NoChat);
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

            if (options.PosX != int.MaxValue && options.PosY != int.MaxValue) {
                Stopwatch moveTimeout = Stopwatch.StartNew();
                while (moveTimeout.Elapsed.TotalSeconds < 10)
                {
                    IntPtr pWindow = client.MainWindowHandle;
                    if (pWindow != IntPtr.Zero)
                    {
                        SetWindowPos(pWindow, IntPtr.Zero, options.PosX, options.PosY, 0, 0, SWP_NOZORDER | SWP_NOSIZE | SWP_SHOWWINDOW);
                        break;
                    }
                    Thread.Sleep(50);
                }
            }
            return client;
        }
    }
}
