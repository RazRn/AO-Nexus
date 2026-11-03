using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AOLauncher
{
    internal static class CoreBootstrap
    {
        // The public download remains one EXE. Resolve only our exact managed library,
        // from the build's own resource; never search a user or plugin directory.
        [STAThread]
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            Run(args);
        }

        internal static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            if (new AssemblyName(args.Name).Name != "Nexus.Core") return null;
            using (var source = typeof(CoreBootstrap).Assembly.GetManifestResourceStream("AOLauncher.Nexus.Core.dll"))
            {
                if (source == null) return null;
                using (var bytes = new MemoryStream())
                {
                    source.CopyTo(bytes);
                    return Assembly.Load(bytes.ToArray());
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Run(string[] args) => Program.Run(args);
    }
}
