using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AOQuickLauncher
{
    // Program's inert spawn stub is supplied by AccountLimits.Tests.cs. This binary
    // cannot start games. Session registry methods are never invoked by these tests.
    internal static class CreationMonitorTests
    {
        private static int checks;
        private static void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); checks++; }
        private static byte[] Frame(string text) => Encoding.UTF8.GetBytes(text);
        private static bool Parse(string text) { byte[] bytes = Frame(text); return CreationMonitor.TryParse(bytes, bytes.Length, out _); }

        public static async Task<int> Main(string[] args)
        {
            byte[] valid = Frame("AO_NEXUS_CHAR_V1\n174\nFixture\n");
            Check(CreationMonitor.TryParse(valid, valid.Length, out var result) && result.Id == 174 && result.Name == "Fixture", "Valid identity must parse.");
            Check(Parse("AO_NEXUS_CHAR_V1\n2147483647\nFixture\n"), "Maximum positive signed ID must parse.");
            foreach (string invalid in new[] {
                "", "AO_NEXUS_CHAR_V1\n0\nFixture\n", "AO_NEXUS_CHAR_V1\n-4\nFixture\n", "AO_NEXUS_CHAR_V1\n+4\nFixture\n",
                "AO_NEXUS_CHAR_V1\n2147483648\nFixture\n", "AO_NEXUS_CHAR_V1\n4x\nFixture\n", "AO_NEXUS_CHAR_V1\n4\n\n",
                "AO_NEXUS_CHAR_V1\n4\n Fixture\n", "AO_NEXUS_CHAR_V1\n4\nFixture \n", "AO_NEXUS_CHAR_V1\n4\nFix\0ture\n",
                "AO_NEXUS_CHAR_V1\n4\nFix\ture\n", "AO_NEXUS_CHAR_V1\n4\nFixture", "AO_NEXUS_CHAR_V1\n4\nFixture\nextra",
                "AO_NEXUS_CHAR_V2\n4\nFixture\n", "AO_NEXUS_CHAR_V1\r\n4\r\nFixture\r\n", "AO_NEXUS_CHAR_V1\n4\n" + new string('a',65) + "\n"
            }) Check(!Parse(invalid), "Malformed frame must be rejected.");
            Check(!CreationMonitor.TryParse(new byte[] { 0xC0,0xAF },2,out _), "Malformed UTF8 must be rejected.");
            Check(!CreationMonitor.TryParse(valid,valid.Length+1,out _), "Length outside buffer must be rejected.");
            Check(!CreationMonitor.TryParse(new byte[513],513,out _), "Oversized frame must be rejected.");
            Check(!CreationMonitor.TryParse(null,1,out _), "Null frame must be rejected.");

            string game = Path.Combine(Path.GetTempPath(), "SyntheticCreationGame");
            string executable = Path.Combine(game,"AnarchyOnline.exe");
            Check(CreationMonitor.MatchesObservation(12,123,game,result,12,123,executable,"Anarchy Online - Fixture"), "Exact observed client identity must match.");
            Check(!CreationMonitor.MatchesObservation(12,123,game,result,13,123,executable,"Anarchy Online - Fixture"), "Other PID must be rejected.");
            Check(!CreationMonitor.MatchesObservation(12,123,game,result,12,124,executable,"Anarchy Online - Fixture"), "Reused PID with different start time must be rejected.");
            Check(!CreationMonitor.MatchesObservation(12,123,game,result,12,123,Path.Combine(game,"Other.exe"),"Anarchy Online - Fixture"), "Different executable must be rejected.");
            Check(!CreationMonitor.MatchesObservation(12,123,game,result,12,123,executable,"Anarchy Online - Another"), "Different game-window name must be rejected.");
            Check(!CreationMonitor.MatchesObservation(12,123,game,result,12,123,executable,"Anarchy Online"), "Login window must be rejected.");
            var original = new CharacterLaunch { AccountId="fixture-account",Account="fixture",Password="never-copy-this",CreateCharacter=true,CharacterId=0,
                GamePath=game,Server="192.0.2.5",Port=7500,ServerName="Fixture server",MaxAccounts=2,MaxCharactersPerAccount=1 };
            var promoted = CreationMonitor.PromoteEntry(original,result);
            Check(!promoted.CreateCharacter && promoted.CharacterId==174 && promoted.CharacterName=="Fixture", "Promotion must convert creation to a normal real char.");
            Check(promoted.AccountId==original.AccountId && promoted.Account==original.Account && promoted.Server==original.Server && promoted.Port==original.Port && promoted.GamePath==original.GamePath, "Promotion must preserve account/endpoint/game provenance.");
            Check(promoted.Password==null && original.CreateCharacter && original.CharacterId==0, "Promotion must omit credentials and preserve the original request mode.");
            Check(promoted.MaxAccounts==2 && promoted.MaxCharactersPerAccount==1 && promoted.ServerName==original.ServerName, "Promotion must preserve configured limits and server label.");

            string mutexName=@"Local\AONexus.CreationFixture."+Guid.NewGuid().ToString("N");
            var waiting=new TaskCompletionSource<int>();
            Task<int> background=LaunchQueue.WithLaunchLock(() => {
                Check(!CanAcquireElsewhere(mutexName),"Reservation work must own the queue lock against competing threads.");
                return waiting.Task;
            },mutexName);
            Check(!background.IsCompleted && CanAcquireElsewhere(mutexName),"Queue lock must be released while creation continues in the background.");
            bool reservationFailed=false;
            try { LaunchQueue.WithLaunchLock<int>(() => throw new InvalidOperationException("Fixture reservation failure"),mutexName); }
            catch(InvalidOperationException) { reservationFailed=true; }
            Check(reservationFailed && CanAcquireElsewhere(mutexName),"Failed reservations must release the queue lock.");
            waiting.SetResult(0);
            using(var exited=new CancellationTokenSource())
            {
                await CreationMonitor.WatchGameExitAsync(() => false,exited);
                Check(exited.IsCancellationRequested,"Game exit must stop background detection immediately.");
            }
            using(var timeout=new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            {
                var watch=Stopwatch.StartNew();
                await CreationMonitor.WatchGameExitAsync(() => true,timeout);
                Check(timeout.IsCancellationRequested && watch.Elapsed<TimeSpan.FromSeconds(2),"Timeout must end monitoring even while the game remains open.");
            }

            // Real loopback pipes test CurrentUserOnly, PID verification, fragmented
            // reads, disconnect/reuse and cancellation without accessing real clients.
            await PipeRoundTrip(valid, Environment.ProcessId, true, true);
            await PipeRoundTrip(valid, Environment.ProcessId+100000, false, false);
            await PipeRoundTrip(Frame("AO_NEXUS_CHAR_V1\n4\nName\nextra"), Environment.ProcessId, false, false);
            await PipeRoundTrip(new byte[513], Environment.ProcessId, false, false);
            string name = CreationMonitor.PipePrefix+Guid.NewGuid().ToString("N");
            using (var server=CreationMonitor.NewPipe(name))
            {
                using var canceled=new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                bool stopped=false;
                try { await CreationMonitor.ReceiveAsync(server,Environment.ProcessId,canceled.Token); }
                catch(OperationCanceledException) { stopped=true; }
                Check(stopped,"Waiting without a reporter must cancel promptly.");
            }
            if(args.Length==1)
            {
                name=CreationMonitor.PipePrefix+Guid.NewGuid().ToString("N");
                using var server=CreationMonitor.NewPipe(name);
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var native=Process.Start(new ProcessStartInfo {
                    FileName=Path.GetFullPath(args[0]),Arguments=name,UseShellExecute=false,CreateNoWindow=true
                });
                CreatedCharacter nativeResult=await CreationMonitor.ReceiveAsync(server,native.Id,deadline.Token);
                await native.WaitForExitAsync(deadline.Token);
                Check(native.ExitCode==0,"Native reporter must receive ACK and exit successfully.");
                Check(nativeResult!=null && nativeResult.Id==123456 && nativeResult.Name=="NativeFixture",
                    "Production native reporter must interoperate with CurrentUserOnly managed pipe and exact child PID validation.");
            }
            Console.WriteLine("PASS: "+checks+" creation reporter checks. Synthetic protocol, own-process pipes and inert observations only; no games, credentials or session registry accessed.");
            return 0;
        }

        private static async Task PipeRoundTrip(byte[] bytes,int expectedPid,bool accepted,bool fragmented)
        {
            string name=CreationMonitor.PipePrefix+Guid.NewGuid().ToString("N");
            using var server=CreationMonitor.NewPipe(name);
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            Task<CreatedCharacter> receive=CreationMonitor.ReceiveAsync(server,expectedPid,deadline.Token);
            using(var writer=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous))
            {
                await writer.ConnectAsync(deadline.Token);
                try
                {
                    if(fragmented)
                    {
                        foreach(byte value in bytes) await writer.WriteAsync(new byte[]{value},deadline.Token);
                    }
                    else await writer.WriteAsync(bytes,deadline.Token);
                    if (accepted) await Ack(writer,deadline.Token);
                }
                catch(IOException) when(expectedPid!=Environment.ProcessId) { }
            }
            CreatedCharacter result=await receive;
            Check((result!=null)==accepted,"Pipe sender/frame acceptance did not match expectations.");
            // Reuse the same listener after either success or rejection.
            receive=CreationMonitor.ReceiveAsync(server,Environment.ProcessId,deadline.Token);
            using(var writer=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous))
            {
                await writer.ConnectAsync(deadline.Token);
                await writer.WriteAsync(Frame("AO_NEXUS_CHAR_V1\n5\nRetry\n"),deadline.Token);
                await Ack(writer,deadline.Token);
            }
            result=await receive;
            Check(result!=null && result.Id==5 && result.Name=="Retry","Listener must accept a valid retry after disconnect/rejection.");
        }

        private static async Task Ack(NamedPipeClientStream writer,CancellationToken cancellation)
        {
            var ack=new byte[3];
            int count=0;
            while(count<ack.Length)
            {
                int read=await writer.ReadAsync(ack.AsMemory(count),cancellation);
                if(read==0) break;
                count+=read;
            }
            Check(count==3 && Encoding.ASCII.GetString(ack)=="OK\n","Validated report must receive exact bounded acknowledgement before close.");
        }

        private static bool CanAcquireElsewhere(string name)
        {
            bool acquired=false;
            var thread=new Thread(() => {
                using var mutex=new Mutex(false,name);
                acquired=mutex.WaitOne(0);
                if(acquired) mutex.ReleaseMutex();
            });
            thread.Start();
            if(!thread.Join(2000)) throw new Exception("Fixture lock probe timed out.");
            return acquired;
        }
    }
}
