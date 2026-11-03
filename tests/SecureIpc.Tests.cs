using System;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using AOSharp.Core.IPC;

internal static class SecureIpcTests
{
    static int checks;
    static void Check(bool value, string text) { if (!value) throw new Exception(text); checks++; }
    static Process Peer(string scope)
    {
        return Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--key " + scope) {
            UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true, RedirectStandardOutput=true });
    }
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--key")
        {
            using (var hash = SHA256.Create()) Console.WriteLine(Convert.ToBase64String(hash.ComputeHash(LocalIpcKey.OpenSessionKey(args[1]))));
            Console.ReadLine(); return 0;
        }
        string scope = Guid.NewGuid().ToString("N"), oldKey;
        using (var one = Peer(scope)) using (var two = Peer(scope))
        {
            oldKey = one.StandardOutput.ReadLine(); string peerKey = two.StandardOutput.ReadLine();
            Check(oldKey != null && oldKey == peerKey, "Concurrent processes share the protected session key");
            one.StandardInput.WriteLine("close"); two.StandardInput.WriteLine("close");
            Check(one.WaitForExit(5000) && two.WaitForExit(5000), "Last participants close");
        }
        using (var nextPeer = Peer(scope))
        {
            string nextKey = nextPeer.StandardOutput.ReadLine();
            Check(nextKey != null && nextKey != oldKey, "New participant group rotates key after last exit (also prevents previous-boot replay)");
            nextPeer.StandardInput.WriteLine("close"); Check(nextPeer.WaitForExit(5000), "Key worker exits");
        }
        long clock = 100; var key = Enumerable.Range(0,32).Select(i=>(byte)i).ToArray();
        var sender = new AuthenticatedPacket(key, () => clock, 1000);
        var receiver = new AuthenticatedPacket(key, () => clock, 1000);
        byte[] payload = {255,255,11,0,77,42,0,0,0,1,0}; // Old unauthenticated packet, valid legacy header.
        clock++;
        Check(!receiver.TryUnprotect(payload, out _), "Original unsigned trigger rejected before dispatch");
        var valid = sender.Protect(payload);
        Check(receiver.TryUnprotect(valid, out var decoded) && decoded.SequenceEqual(payload), "Legitimate plugin payload unchanged");
        Check(!receiver.TryUnprotect(valid, out _), "Duplicate rejected");
        var wrong = new AuthenticatedPacket(new byte[32], () => 100, 1000);
        Check(!wrong.TryUnprotect(valid, out _), "Wrong-user key rejected");
        for (int i=0; i<valid.Length; i++)
        {
            var edited=(byte[])valid.Clone(); edited[i]^=1;
            Check(!new AuthenticatedPacket(key, () => clock,1000).TryUnprotect(edited,out _), "Altered byte " + i + " rejected");
        }
        for (int i=0;i<valid.Length;i++) Check(!receiver.TryUnprotect(valid.Take(i).ToArray(),out _),"Truncation rejected");
        Check(!receiver.TryUnprotect(valid.Concat(new byte[]{0}).ToArray(),out _),"Trailing data rejected");
        clock++;
        var restarted=new AuthenticatedPacket(key,()=>clock,1000);
        Check(!restarted.TryUnprotect(valid,out _),"Capture predating receiver restart rejected");
        var newBootKey = key.Select(b => (byte)(b ^ 0x55)).ToArray();
        var newBoot = new AuthenticatedPacket(newBootKey, () => 100, 1000);
        Check(!newBoot.TryUnprotect(valid,out _),"Previous-boot capture rejected even when uptime repeats");
        var sibling=new AuthenticatedPacket(key,()=>clock,1000);
        clock++;
        var next=sender.Protect(payload);
        Check(restarted.TryUnprotect(next,out _) && sibling.TryUnprotect(next,out _),"Independent channel objects both receive legitimate packets");
        var first=sender.Protect(payload); var second=sender.Protect(payload);
        Check(receiver.TryUnprotect(second,out _) && receiver.TryUnprotect(first,out _),"Out of order delivery supported");
        Check(!receiver.TryUnprotect(first,out _),"Out of order replay rejected");
        var stale=sender.Protect(payload); clock+=5001;
        Check(!receiver.TryUnprotect(stale,out _),"Expired packet rejected");
        var future=sender.Protect(payload); clock--;
        Check(!receiver.TryUnprotect(future,out _),"Future timestamp rejected"); clock++;
        var busy=new AuthenticatedPacket(key,()=>clock,1000); clock++;
        byte[] original=null;
        for(int i=0;i<1024;i++)
        {
            var peer=new AuthenticatedPacket(key,()=>clock,1000); var packet=peer.Protect(payload);
            if(i==0)original=packet;
            Check(busy.TryUnprotect(packet,out _),"Known finite peer capacity");
        }
        var extra=new AuthenticatedPacket(key,()=>clock,1000);
        Check(!busy.TryUnprotect(extra.Protect(payload),out _),"Fresh entries not evicted at capacity");
        Check(!busy.TryUnprotect(original,out _),"Capacity cannot resurrect replay");
        clock+=5001; Check(busy.TryUnprotect(extra.Protect(payload),out _),"Expired peer slots reclaimed");
        Console.WriteLine("PASS: " + checks + " secure IPC envelope checks; unsigned trigger, mutation, replay and legitimate delivery.");
        return 0;
    }
}
