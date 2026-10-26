using System;
using System.IO;
using System.Threading;

namespace AOLauncher
{
    // Kept inside the EXE so the existing single-file updater also delivers monitoring.
    internal static class EmbeddedMonitoring
    {
        internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "Monitoring");

        internal static string EnsureAvailable() => Extract(Root);

        internal static string Extract(string cacheRoot)
        {
            var assembly = typeof(EmbeddedMonitoring).Assembly;
            using (var payload = assembly.GetManifestResourceStream("AOLauncher.Monitoring.zip"))
            using (var manifest = assembly.GetManifestResourceStream("AOLauncher.Monitoring.manifest.json")) {
                if (payload == null || manifest == null) throw new InvalidDataException("The bundled monitoring files are missing. Download AO Nexus again.");
                return Path.GetDirectoryName(EmbeddedBackend.EnsureBundleAvailable(cacheRoot, payload, manifest, "NexusHealthHost.exe", CancellationToken.None));
            }
        }
    }
}
