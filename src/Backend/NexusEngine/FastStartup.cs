using System;
using System.IO;
using System.Security.Cryptography;

namespace NexusEngine
{
    // These private client routines are supported ONLY for the measured classic build.
    // Hash before spawning, so disk I/O cannot delay the client's initialization hook.
    internal static class FastStartup
    {
        internal static bool IsSupported(string directory)
        {
            string[,] files = {
                { "AnarchyOnline.exe", "370C0670CC9CB46626EF24692376AAF492BB1787BAD8A1125365A6BE4F663862" },
                { "MessageProtocol.dll", "05F05A764E7BDAC208AC240A4AC1A2B781424C8139DF3A76CBF2270376CAD81D" },
                { "GUI.dll", "E485384721E2FE13972E840DFB6A9FE29B1BA4EB71B42CC049E5097A570B6DE1" },
                { "Interfaces.dll", "3AA79A44E76C3413543404058C5D07323BD3B69F4C3493C7B136BEFA1A55B0A7" },
                { "Utils.dll", "2733690715E4133E6DDC50B7D423EA451B6A8C0C6C3B16CAEDBF7608A0A00B83" },
                { "randy31.dll", "9D9E0DA25AC6F8C7ECDCE7A47A2409575945966A29E3B77850F7C62033A04BE3" }
            };
            try
            {
                using (var sha = SHA256.Create())
                    for (int i = 0; i < files.GetLength(0); i++)
                        using (var stream = File.OpenRead(Path.Combine(directory, files[i, 0])))
                            if (Convert.ToHexString(sha.ComputeHash(stream)) != files[i, 1]) return false;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
