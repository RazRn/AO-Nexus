using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AOLauncher
{
    // Detects AOSharp that another program (usually AOSharp.exe) has put into a game client
    // Nexus logged in. Two copies of AOSharp hook the same game functions twice, which can
    // freeze or crash the client, and their plugins fight over the character.
    //
    // Signals, strongest first:
    //  1. Modules: the client has EasyHook32.dll or AOSharp.Bootstrap.dll / AOSharp.Core.dll
    //     loaded from a folder that is not Nexus's AOSharp pack. Works whether or not Nexus
    //     loaded plugins too, so it also catches AOSharp.exe attaching afterwards.
    //  2. Pipe: AOSharp's bootstrapper opens a pipe named after the client's process ID. If it
    //     exists while Nexus's plugin host does not manage that client, someone else owns it.
    // Limitation: when Nexus uses the same AOSharp folder as AOSharp.exe (Settings → AOSharp
    // folder) and has already loaded plugins, a second attach from that folder loads the same
    // files and cannot be told apart; Nexus then only notes that AOSharp.exe is running.
    internal static class ForeignAOSharp
    {
        internal sealed class Finding
        {
            internal string Folder;      // where the other AOSharp was loaded from, if known
            internal string Controller;  // e.g. "AOSharp.exe (PID 1234)", if one is running
            internal bool FromPipe;
            internal string Describe()
            {
                string who = Controller ?? AOLauncher.Localization.Texts.Get("Text_another_program_4121FA7F62EC");
                return Folder == null ? who : who + " · " + Folder;
            }
        }

        static readonly string[] Markers = { "easyhook32.dll", "aosharp.bootstrap.dll", "aosharp.core.dll" };

        internal static Finding Check(int pid, long startTicks, IEnumerable<string> ownFolders, bool managedByNexus)
        {
            var own = (ownFolders ?? Enumerable.Empty<string>()).Where(folder => !string.IsNullOrWhiteSpace(folder)).Select(Normalize).ToList();
            string foreignFolder = null;
            try
            {
                using (var process = Process.GetProcessById(pid))
                {
                    if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != startTicks) return null;
                    foreach (ProcessModule module in process.Modules)
                    {
                        string path = module.FileName;
                        if (string.IsNullOrEmpty(path) || !Markers.Contains(Path.GetFileName(path).ToLowerInvariant())) continue;
                        string folder = Path.GetDirectoryName(path);
                        if (!IsOwn(folder, own)) { foreignFolder = folder; break; }
                    }
                }
            }
            catch (ArgumentException) { return null; } // closed
            catch { /* modules unreadable: fall back to the pipe signal */ }

            bool pipe = !managedByNexus && PipeExists(pid.ToString());
            if (foreignFolder == null && !pipe) return null;
            return new Finding { Folder = foreignFolder, FromPipe = foreignFolder == null, Controller = RunningAOSharp().FirstOrDefault() };
        }

        // Running AOSharp.exe instances, as "AOSharp.exe (PID n)".
        internal static List<string> RunningAOSharp()
        {
            var result = new List<string>();
            try
            {
                foreach (var process in Process.GetProcessesByName("AOSharp"))
                    using (process) result.Add("AOSharp.exe (PID " + process.Id + ")");
            }
            catch { }
            return result;
        }

        internal static bool IsOwn(string folder, IEnumerable<string> ownFolders)
        {
            string normalized = Normalize(folder);
            return ownFolders.Where(own => !string.IsNullOrWhiteSpace(own)).Select(Normalize).Any(own => normalized.Equals(own, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(own + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }

        static string Normalize(string folder)
        {
            try { folder = Path.GetFullPath(folder); } catch { }
            return folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        static bool PipeExists(string name)
        {
            try { return Directory.GetFiles(@"\\.\pipe\").Any(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)); }
            catch { return false; }
        }
    }
}
