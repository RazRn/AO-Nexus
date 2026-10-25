# Build and test AO Nexus

These instructions are for contributors building the Windows application from source. Players should download the runnable ZIP from [Releases](https://github.com/RazRn/AO-Nexus/releases); see the [player guide](Player-Guide.md).

## Requirements

Build on Windows with:

- PowerShell and Git.
- Visual Studio / Build Tools with desktop C++ tools, a Windows SDK and the **.NET Framework 4.8 targeting pack**.
- The **.NET 10 SDK** for the managed login backend.
- Internet access for dependency restore from NuGet.

The interface targets .NET Framework 4.8 and x86. The managed backend targets .NET 10 and is published self-contained for Windows x86. The native LoginHandler builds as Win32.

## Build a clean checkout

```powershell
git clone https://github.com/RazRn/AO-Nexus.git
Set-Location AO-Nexus
.\build\Build.ps1 -RebuildBackend -Package
```

The default native toolset is **v145**. To use an installed compatible toolset such as Visual Studio 2022's **v143**:

```powershell
.\build\Build.ps1 -RebuildBackend -Package -PlatformToolset v143
```

The build restores Detours 4.0.1 from a fixed NuGet URL and verifies its pinned SHA-256 before extracting it. It builds LoginHandler, publishes the backend and its runtime, compresses and verifies the payload, then embeds it in the interface.

| Output | Purpose |
| --- | --- |
| `dist/AONexus.exe` | Runnable application. |
| `AO-Nexus-Luckyspawn.zip` | Verified release package when `-Package` is supplied. |
| `runtime/AOQuickLauncher` | Generated backend used for embedding. |
| `src/Launcher/bin/Release` | Interface build output and debugging symbols. |

A clean checkout needs **-RebuildBackend** because binaries and restored packages are excluded from Git. For subsequent interface-only changes, reuse the generated runtime with:

```powershell
.\build\Build.ps1 -Package
```

Close a running copy of `dist/AONexus.exe` before building over it. The large executable includes the self-contained backend runtime; most of its size is not interface code.

## Run verification

After building:

```powershell
.\build\Verify.ps1
```

To include live checks of the official news endpoints:

```powershell
.\build\Verify.ps1 -LiveNews
```

The fixtures cover settings and migrations, server limits, presets, password preferences and updates, Windows encryption, character creation and discovery, launch requests and cancellation, runtime integrity, news parsing, diagnostics anonymisation and crash reports, and English/Danish UI layout. Reports are written to `logs/`.

Tests use synthetic accounts, isolated stores and inert game fixtures. They do not perform real game logins or injection. UI tests require an interactive Windows desktop. Passing them verifies the launcher behavior covered by those fixtures; it does not establish compatibility with every server or game client.

## Package an existing build

```powershell
.\build\Package.ps1
```

The packager verifies the embedded payload and uses an allowlist of release files, checking the resulting ZIP against its SHA-256 manifest. Generated executables, ZIPs, runtime files, restored packages and test logs stay out of Git. Publish the ZIP as a release asset, alongside the source version used to build it.

The inherited backend `Login.bat` is an upstream demonstration, not the Nexus startup or build entry point. Start `AONexus.exe` for normal use; the interface passes login requests to its backend over private standard input.

## Source map

| Directory or file | Contents |
| --- | --- |
| [`src/Launcher`](../src/Launcher) | WinForms interface, settings, passwords, news, Prefs discovery, runtime loader, and anonymised diagnostics and problem reports (`Diagnostics.cs`, `ReportProblemDialog.cs`). |
| [`src/Common`](../src/Common) | Shared localization and server-account policy. |
| [`src/Backend/AOQuickLauncher`](../src/Backend/AOQuickLauncher) | Modified managed login backend, account queues and client tracking. |
| [`src/Backend/LoginHandler`](../src/Backend/LoginHandler) | Native client login handler. |
| [`build`](../build) | Build, pinned dependency restore, payload verification and packaging scripts. |
| [`tests`](../tests) | Regression fixtures and synthetic UI checks. |
| [`.github/ISSUE_TEMPLATE`](../.github/ISSUE_TEMPLATE) | Bug report form that **Report a problem** fills in. |
| [`docs/images`](images) | Example-account screenshots, captured by [`Capture-GuideScreenshots.ps1`](../tests/Capture-GuideScreenshots.ps1). |
| [`assets`](../assets) | Artwork provenance, editable icon and icon build script. |
| [`dist/README.txt`](../dist/README.txt) | Offline quick guide included in releases. |
| [`dist/CREDITS.txt`](../dist/CREDITS.txt) and [`dist/Licenses`](../dist/Licenses) | Upstream attribution and dependency notices. |
| [`NEWS-SOURCES.md`](../NEWS-SOURCES.md) | News endpoints, behavior and verification notes. |
| [`PROJECT.txt`](../PROJECT.txt) | Additional project and architecture notes. |

The earlier AO Launcher and AOSharp projects are separate from this repository. Internal `AOLauncher` namespaces and resource identifiers remain for compatibility; the application, executable and local profile are named AO Nexus / AONexus.

See the [source and distribution notice](../LICENSE-NOTICE.txt) before reusing or redistributing source or binaries.
