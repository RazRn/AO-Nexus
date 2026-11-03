# Core boundaries and tests

`src/Core/Nexus.Core.csproj` is a .NET Framework 4.8 library. It references only the base framework, never WinForms or the launcher. The launcher references the library and embeds the resulting managed DLL so the download remains one EXE.

| Component | Responsibility |
| --- | --- |
| `BackendRunner` | One backend process, private stdin, bounded output draining, cancellation and cleanup. It never kills game descendants. |
| `LaunchPlan` | Validated preset ordering and backend start configuration. |
| `SettingsStorage` | Authoritative current/legacy path selection, atomic settings replacement and migration backups. |
| `PluginDispatch` | Connect/start/reconnect policy, no automatic resend after dispatch, and explicit failure outcomes. |
| `StartupPolling` | Independent plugin/team polling lanes; busy ticks are coalesced without blocking the other lane. |
| `GraphicsTransaction` | Lock and verify all destinations before writing, restore attempted writes on failure. |

The launcher supplies UI callbacks, settings schema migration, plugin transport and redacted diagnostic sinks. `src/Core/Launch/Nexus.Launch.Core.csproj` builds the .NET 10 account admission queue and native client integration as a second library. `NexusEngine.exe` is its thin process entry point. Keeping the Framework UI and .NET 10 worker separate preserves the existing process and plugin boundaries. Compatibility hash checks are unchanged. This boundary is a foundation for further extraction, not a claim that every domain operation has left `MainForm`.

## Run the game-independent suite

On Windows with the .NET 10 SDK and .NET Framework 4.8 targeting pack:

```powershell
dotnet restore tests/Nexus.UnitTests/Nexus.UnitTests.csproj --locked-mode
dotnet test tests/Nexus.UnitTests/Nexus.UnitTests.csproj -c Release --no-restore
dotnet restore tests/Nexus.Launch.UnitTests/Nexus.Launch.UnitTests.csproj --locked-mode
dotnet test tests/Nexus.Launch.UnitTests/Nexus.Launch.UnitTests.csproj -c Release --no-restore
```

MSTest uses real project references to both core libraries, temporary directories and inert transports. It exercises settings preservation, graphics rollback, backend cancellation, plugin failures, diagnostic suppression and all translation entries. Queue tests check parallel accounts, serialization within an account, ordered teams, failure isolation, credential cleanup, admission fallback and focus policy. Backend process tests launch an isolated PowerShell child with no profile or script-policy changes. No AO installation, credentials or running clients are needed.

`.github/workflows/unit-tests.yml` runs this suite on Windows for pushes and pull requests and saves TRX results. Local results do not imply that a remote CI run has occurred.

The older `Verify-*.ps1` programs remain integration and exploratory checks. Some require game assets or local binaries; they are not silently counted as portable unit tests. Use them only with their documented fixture requirements.

## Diagnostics

`FailureDiagnostics` records operation, process ID and process start ticks, outcome and exception details through the existing redactor. Repeated identical failures are suppressed for 30 seconds; recovery is recorded once. Histories are bounded. Requests and credential payloads must never be serialized to diagnostics. Parser failures report exception type/HRESULT rather than potentially quoting input.

Expected absence during startup and already-exited-process races need not become errors. Keep deliberately quiet catches documented. Diagnostic-sink failure must not recursively log or interrupt recovery.
