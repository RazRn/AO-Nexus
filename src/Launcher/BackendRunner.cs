using AOLauncher.Localization;
namespace AOLauncher
{
    // UI composition only; process ownership and cancellation live in Nexus.Core.
    internal sealed class BackendRunner : Nexus.Core.BackendRunner
    {
        internal BackendRunner() : base(Texts.T, (operation, error) =>
            FailureDiagnostics.Launch.Report(operation, "Backend operation failed", error)) { }
    }
}
