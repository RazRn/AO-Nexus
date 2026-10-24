using AOLauncher.Localization;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AOLauncher
{
    /// <summary>Owns one login backend, independently of game processes inheriting its output handles.</summary>
    internal sealed class BackendRunner : IDisposable
    {
        internal const int StoppedExitCode = -2;
        private readonly object sync = new object();
        private Process process;
        private bool started;
        private bool stopRequested;
        private bool disposed;
        private bool acceptOutput;

        internal Task<int> RunAsync(ProcessStartInfo startInfo, string jsonPayload,
            Action<string> stdout, Action<string> stderr)
        {
            if (startInfo == null) throw new ArgumentNullException(nameof(startInfo));
            if (jsonPayload == null) throw new ArgumentNullException(nameof(jsonPayload));
            lock (sync)
            {
                if (started) throw new InvalidOperationException(Texts.T("BackendRunner can only be used once."));
                started = true;
                if (stopRequested || disposed) return Task.FromResult(StoppedExitCode);
            }
            return Task.Run(() => RunCoreAsync(startInfo, jsonPayload, stdout, stderr));
        }

        private async Task<int> RunCoreAsync(ProcessStartInfo startInfo, string jsonPayload,
            Action<string> stdout, Action<string> stderr)
        {
            var child = new Process { StartInfo = startInfo };
            var outputEnded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var errorEnded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            DataReceivedEventHandler onOutput = (sender, e) => Receive(e.Data, stdout, outputEnded);
            DataReceivedEventHandler onError = (sender, e) => Receive(e.Data, stderr, errorEnded);
            bool outputReading = false;
            bool errorReading = false;
            try
            {
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardInput = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = new UTF8Encoding(false);
                startInfo.StandardErrorEncoding = new UTF8Encoding(false);
                child.OutputDataReceived += onOutput;
                child.ErrorDataReceived += onError;
                lock (sync)
                {
                    if (stopRequested || disposed) return StoppedExitCode;
                    if (!child.Start()) throw new InvalidOperationException(Texts.T("AOQuickLauncher could not be started."));
                    process = child;
                    acceptOutput = true;
                }

                child.BeginOutputReadLine();
                outputReading = true;
                child.BeginErrorReadLine();
                errorReading = true;

                // Only the backend's private stdin carries the request. Never put it in command-line arguments or logs.
                using (var input = new StreamWriter(child.StandardInput.BaseStream, new UTF8Encoding(false)))
                {
                    await input.WriteLineAsync(jsonPayload).ConfigureAwait(false);
                }
                jsonPayload = null;

                // The parameterless overload also waits for redirected streams to reach EOF. Game clients can
                // inherit those handles and keep them open long after this backend has finished.
                await Task.Run(() =>
                {
                    while (!child.WaitForExit(100))
                    {
                        lock (sync)
                        {
                            if (stopRequested) KillOwnedProcess(child);
                        }
                    }
                }).ConfigureAwait(false);
                int exitCode = child.ExitCode;

                // Deliver trailing status lines without waiting for descendants to close inherited pipes.
                await Task.WhenAny(Task.WhenAll(outputEnded.Task, errorEnded.Task), Task.Delay(200)).ConfigureAwait(false);
                lock (sync) return stopRequested ? StoppedExitCode : exitCode;
            }
            catch
            {
                lock (sync)
                {
                    KillOwnedProcess(child);
                    if (stopRequested) return StoppedExitCode;
                }
                throw;
            }
            finally
            {
                jsonPayload = null;
                lock (sync)
                {
                    acceptOutput = false;
                    if (ReferenceEquals(process, child)) process = null;
                }
                if (outputReading) { try { child.CancelOutputRead(); } catch { } }
                if (errorReading) { try { child.CancelErrorRead(); } catch { } }
                child.OutputDataReceived -= onOutput;
                child.ErrorDataReceived -= onError;
                child.Dispose();
            }
        }

        private void Receive(string line, Action<string> callback, TaskCompletionSource<bool> ended)
        {
            if (line == null) { ended.TrySetResult(true); return; }
            lock (sync)
            {
                if (!acceptOutput || disposed) return;
            }
            // A closing UI may no longer accept a callback; that must not fault a process output reader.
            try { if (callback != null) callback(line); }
            catch { }
        }

        internal void Stop()
        {
            lock (sync)
            {
                stopRequested = true;
                KillOwnedProcess(process);
            }
        }

        private static void KillOwnedProcess(Process child)
        {
            if (child == null) return;
            try { if (!child.HasExited) child.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        public void Dispose()
        {
            lock (sync)
            {
                disposed = true;
                stopRequested = true;
                acceptOutput = false;
                KillOwnedProcess(process);
                // RunCoreAsync owns disposal, so Stop/Dispose cannot invalidate a concurrent wait or pipe operation.
            }
        }
    }
}
