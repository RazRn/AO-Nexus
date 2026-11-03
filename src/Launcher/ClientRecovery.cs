using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AOLauncher
{
    internal static class ClientRecovery
    {
        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("kernel32.dll")] static extern void SetLastError(uint error);
        internal static bool? WindowResponding(IntPtr window,int pid)
        {
            if(window==IntPtr.Zero) return null;
            GetWindowThreadProcessId(window,out var owner);if(owner!=(uint)pid)return null;
            // A bounded WM_NULL probe, not a game command. Access denied is unknown, not a freeze.
            SetLastError(0);
            var result=SendMessageTimeout(window,0,IntPtr.Zero,IntPtr.Zero,2|32,80,out var ignored);
            if(result!=IntPtr.Zero)return true;
            int error=Marshal.GetLastWin32Error();return error==0 || error==1460?(bool?)false:null;
        }
        internal static bool Matches(PresetCamping.Client client,int pid,long ticks,string path)
            => client!=null && pid==client.ProcessId && ticks==client.StartTimeUtcTicks
                && PresetCamping.SamePath(path,Path.Combine(client.GamePath,"AnarchyOnline.exe"));
        internal static bool ForceClose(PresetCamping.Client client,Process process)
        {
            // The caller holds a confirmed process handle across the confirmation and operation.
            // Never kill by process name, a newly looked-up PID, or a process tree.
            if(process.HasExited)return true;
            if(!Matches(client,process.Id,process.StartTime.ToUniversalTime().Ticks,process.MainModule.FileName))
                throw new InvalidOperationException("The client identity changed; force close was cancelled.");
            DevelopmentRecorder.Action("force-close",client,"terminating confirmed process");
            process.Kill();
            bool exited=process.WaitForExit(5000);
            DevelopmentRecorder.Action("force-close",client,exited?"exit confirmed":"exit not confirmed");
            return exited;
        }
    }
}
