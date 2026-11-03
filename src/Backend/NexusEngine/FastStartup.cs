using System;
using System.IO;
using System.Security.Cryptography;

namespace NexusEngine
{
    internal enum StartupProfile { None, Stock, AOVK, AOVKVerified }

    // These private client routines are supported ONLY for the measured classic build.
    // Hash before spawning, so disk I/O cannot delay the client's initialization hook.
    internal static class FastStartup
    {
        // The same classic executable with only IMAGE_FILE_LARGE_ADDRESS_AWARE set.
        internal const string LargeAddressClientHash = "60647245852D50F6EEA322F2CB49971C73646E2B6EFA0AAE9F7385CE85AC8424";
        internal const string StockRendererHash = "9D9E0DA25AC6F8C7ECDCE7A47A2409575945966A29E3B77850F7C62033A04BE3";
        // Upstream v0.7.1 release DLL, from the SHA-256 verified release archive.
        internal const string AOVKRendererHash = "60759C61C7383EFA39AE4F09981ED3E42D7A25CE7C9D093E6791A5C4A98040C6";

        internal const string AOVKUiRendererHash = "BAF83111E172FA8BB833F2615B02D1162C0F6125CF6836421F22C714445825EE";
        internal const string VerifiedAovkRendererHash = "4BAED22CA29039224AD0167F74E0A9793AF0A396CF3FE868DC99B66992E88151";
        internal const string VerifiedAovkRepairHash = "BB7581048F77D3BD89D95F983BD472B5ABB7FDA8D0B920D2825E499998A25B2A";
        internal const string Aovk081RendererHash = "191B55E33790ECA78FEC2926D12A95344734BB3FE7969C4FA70879E17E497E15";
        internal const string Aovk081RepairHash = "8691D59A0F4C13859FAA094432156E7461E40F7F58A3A3A9E931B7DE675425F6";

        // Ordinary launches admit only stock or the measured AO-VK + repaired startup pair.
        // Other pinned AO-VK builds still require the explicit development opt-in.
        internal static StartupProfile GetAllowedProfile(string directory, bool allowAovk)
            => AllowedProfile(GetProfile(directory, false), allowAovk);
        internal static StartupProfile AllowedProfile(StartupProfile profile, bool allowAovk)
            => profile == StartupProfile.Stock || (allowAovk && profile == StartupProfile.AOVKVerified) ? profile : StartupProfile.None;

        internal static bool IsSupported(string directory) => GetProfile(directory, false) != StartupProfile.None;

        internal static StartupProfile GetProfile(string directory, bool allowAOVK)
            => GetProfile(directory, allowAOVK, file => {
                using (var stream = File.OpenRead(file))
                using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            });

        // Hash-reader injection lets tests exercise every rejection without real game binaries.
        internal static StartupProfile GetProfile(string directory, bool allowAOVK, Func<string, string> readHash)
        {
            string[,] files = {
                { "AnarchyOnline.exe", "370C0670CC9CB46626EF24692376AAF492BB1787BAD8A1125365A6BE4F663862" },
                { "MessageProtocol.dll", "05F05A764E7BDAC208AC240A4AC1A2B781424C8139DF3A76CBF2270376CAD81D" },
                { "GUI.dll", "E485384721E2FE13972E840DFB6A9FE29B1BA4EB71B42CC049E5097A570B6DE1" },
                { "Interfaces.dll", "3AA79A44E76C3413543404058C5D07323BD3B69F4C3493C7B136BEFA1A55B0A7" },
                { "Utils.dll", "2733690715E4133E6DDC50B7D423EA451B6A8C0C6C3B16CAEDBF7608A0A00B83" }
            };
            try
            {
                for (int i = 0; i < files.GetLength(0); i++)
                {
                    string hash = readHash(Path.Combine(directory, files[i, 0]));
                    if (hash != files[i, 1] && !(files[i, 0] == "AnarchyOnline.exe" && hash == LargeAddressClientHash))
                        return StartupProfile.None;
                }
                string renderer = readHash(Path.Combine(directory, "randy31.dll"));
                if (renderer == StockRendererHash) return StartupProfile.Stock;
                if (renderer == Aovk081RendererHash
                    && readHash(Path.Combine(directory,"randy31_orig.dll")) == StockRendererHash
                    && readHash(Path.Combine(directory,"version.dll")) == Aovk081RepairHash)
                    return StartupProfile.AOVKVerified;
                if(renderer==VerifiedAovkRendererHash && readHash(Path.Combine(directory,"randy31_orig.dll"))==StockRendererHash)
                {
                    string repair=null;
                    try {repair=readHash(Path.Combine(directory,"version.dll"));}
                    catch(IOException) { } catch(UnauthorizedAccessException) { }
                    if(repair==VerifiedAovkRepairHash)return StartupProfile.AOVKVerified;
                }
                if (allowAOVK && (renderer == AOVKRendererHash || renderer == AOVKUiRendererHash
                    || renderer == "FAF0E6811B6766FEB5B1EFBED65EE41DB7DB523E89428645205446C358851BD8"
                    || renderer == "4BAED22CA29039224AD0167F74E0A9793AF0A396CF3FE868DC99B66992E88151"
                    || renderer == "430BF85A8B23049C63A4FF0CD152FCF134860AA3D080967BDAF2EF3BB86B8A61"
                    || renderer == "8E76F6F587C7258CDD30F6C481FCC68503094F1AA3B83D854004CEDDA3D05D74") // UI tests 11-15, before optional upgrade
                    && readHash(Path.Combine(directory, "randy31_orig.dll")) == StockRendererHash)
                    return StartupProfile.AOVK;
                return StartupProfile.None;
            }
            catch (IOException) { return StartupProfile.None; }
            catch (UnauthorizedAccessException) { return StartupProfile.None; }
        }
    }
}
