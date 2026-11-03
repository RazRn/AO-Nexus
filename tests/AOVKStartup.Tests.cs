using System;
using System.IO;
using System.Collections.Generic;
using NexusEngine;

internal static class StartupTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static int Main(string[] args)
    {
        var hashes = new Dictionary<string, string> {
            ["AnarchyOnline.exe"] = "370C0670CC9CB46626EF24692376AAF492BB1787BAD8A1125365A6BE4F663862",
            ["MessageProtocol.dll"] = "05F05A764E7BDAC208AC240A4AC1A2B781424C8139DF3A76CBF2270376CAD81D",
            ["GUI.dll"] = "E485384721E2FE13972E840DFB6A9FE29B1BA4EB71B42CC049E5097A570B6DE1",
            ["Interfaces.dll"] = "3AA79A44E76C3413543404058C5D07323BD3B69F4C3493C7B136BEFA1A55B0A7",
            ["Utils.dll"] = "2733690715E4133E6DDC50B7D423EA451B6A8C0C6C3B16CAEDBF7608A0A00B83",
            ["randy31.dll"] = FastStartup.StockRendererHash,
            ["randy31_orig.dll"] = FastStartup.StockRendererHash
        };
        Func<string, string> read = path => hashes[Path.GetFileName(path)];
        Check(FastStartup.GetProfile("fixture", false, read) == StartupProfile.Stock, "Stock lost compatibility");
        hashes["randy31.dll"] = FastStartup.AOVKRendererHash;
        Check(FastStartup.GetProfile("fixture", false, read) == StartupProfile.None, "AO-VK ignored opt-in");
        Check(FastStartup.GetProfile("fixture", true, read) == StartupProfile.AOVK, "Pinned AO-VK rejected");
        hashes["randy31.dll"] = FastStartup.AOVKUiRendererHash;
        Check(FastStartup.GetProfile("fixture", false, read) == StartupProfile.None, "UI renderer ignored opt-in");
        Check(FastStartup.GetProfile("fixture", true, read) == StartupProfile.AOVK, "Pinned UI renderer rejected");
        foreach (var name in new List<string>(hashes.Keys)) {
            var original = hashes[name]; hashes[name] = "foreign";
            Check(FastStartup.GetProfile("fixture", true, read) == StartupProfile.None, "Accepted unknown " + name);
            hashes[name] = original;
        }
        foreach (var name in new List<string>(hashes.Keys)) {
            Check(FastStartup.GetProfile("fixture", true, p => Path.GetFileName(p) == name ? throw new FileNotFoundException() : read(p)) == StartupProfile.None, "Accepted missing " + name);
        }
        Check(FastStartup.GetProfile("fixture", true, p => throw new UnauthorizedAccessException()) == StartupProfile.None, "Access error did not fail closed");
        Check(FastStartup.GetProfile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), true) == StartupProfile.None, "Absent game accepted");
        hashes["randy31.dll"]=FastStartup.VerifiedAovkRendererHash;
        hashes["version.dll"]=FastStartup.VerifiedAovkRepairHash;
        Check(FastStartup.GetProfile("fixture",false,read)==StartupProfile.AOVKVerified,"Repaired verified AO-VK pair requires no experimental flag");
        var stockClient = hashes["AnarchyOnline.exe"];
        hashes["AnarchyOnline.exe"] = FastStartup.LargeAddressClientHash;
        Check(FastStartup.GetProfile("fixture",false,read)==StartupProfile.AOVKVerified,"LAA must preserve the verified fast AO-VK path");
        hashes["randy31.dll"] = FastStartup.StockRendererHash;
        Check(FastStartup.GetProfile("fixture",false,read)==StartupProfile.Stock,"LAA must preserve stock startup");
        hashes["randy31.dll"] = FastStartup.VerifiedAovkRendererHash;
        hashes["AnarchyOnline.exe"] = stockClient;
        foreach(var name in new List<string>(hashes.Keys)) {
            var original=hashes[name];hashes[name]="foreign";
            Check(FastStartup.GetProfile("fixture",false,read)==StartupProfile.None,"Default AO-VK path accepted changed "+name);
            hashes[name]=original;
            Check(FastStartup.GetProfile("fixture",false,p=>Path.GetFileName(p)==name?throw new FileNotFoundException():read(p))==StartupProfile.None,"Default AO-VK path accepted missing "+name);
        }
        hashes["version.dll"]="old-startup-repair";
        Check(FastStartup.GetProfile("fixture",false,read)==StartupProfile.None,"Old repair must not automatically enable preloads");
        Check(FastStartup.GetProfile("fixture",true,read)==StartupProfile.AOVK,"Other known AO-VK combinations retain the experimental profile");
        hashes["randy31.dll"]=FastStartup.Aovk081RendererHash;
        hashes["version.dll"]=FastStartup.Aovk081RepairHash;
        Check(FastStartup.GetProfile("fixture",false,read)==StartupProfile.AOVKVerified,"Measured AO-VK 0.8.1 pair must work without experimental flags");
        hashes["AnarchyOnline.exe"]=FastStartup.LargeAddressClientHash;
        Check(FastStartup.GetProfile("fixture",false,read)==StartupProfile.AOVKVerified,"AO-VK 0.8.1 preserves LAA fast startup");
        foreach(var name in new List<string>(hashes.Keys)) {
            var original=hashes[name]; hashes[name]="foreign";
            Check(FastStartup.GetProfile("fixture",false,read)==StartupProfile.None,"AO-VK 0.8.1 accepted changed "+name);
            Check(FastStartup.GetProfile("fixture",true,read)==StartupProfile.None,"Experimental flag must not bypass changed 0.8.1 files: "+name);
            hashes[name]=original;
            Check(FastStartup.GetProfile("fixture",false,p=>Path.GetFileName(p)==name?throw new FileNotFoundException():read(p))==StartupProfile.None,"AO-VK 0.8.1 accepted missing "+name);
        }
        if (args.Length == 2) {
            Check(FastStartup.GetProfile(args[0], true) == StartupProfile.Stock, "Local stock file validation failed");
            var profile=FastStartup.GetProfile(args[1],true);
            Check(profile==StartupProfile.AOVK || profile==StartupProfile.AOVKVerified,"Local AO-VK file validation failed");
            Check(FastStartup.IsSupported(args[1])==(profile==StartupProfile.AOVKVerified),"Only the repaired verified AO-VK pair enables the normal fast path");
        }
        Console.WriteLine("PASS: managed renderer profiles, all changed/missing files, opt-in, and read-only local preflight.");
        return 0;
    }
}
