# Core, diagnostics, resources and documentation refactor

The first core boundary is implemented in `src/Core/Nexus.Core.csproj` and `src/Core/Launch/Nexus.Launch.Core.csproj`. The Framework library owns backend process lifetime, preset ordering, atomic settings storage, plugin dispatch and graphics transactions. The .NET 10 library owns the existing account queue and client integration; the worker EXE delegates to it. Native compatibility gates and launch timing policy were retained.

MainForm now calls these services but still contains UI orchestration and some settings/request adapters. This is not a claim that the whole application has been decomposed. The remaining native/platform integration is still specific to Windows and the supported game builds.

Plugin startup, polling, monitoring and install/settings failures now have bounded, redacted diagnostics. Expected absent-host status polls remain quiet. Existing catches outside these paths have not all been converted.

The resource migration extracted 1,300 literal inline pairs and 494 legacy aliases. Together with setup options and the crash dialog, both catalogs contain 1,709 entries. No `L(english, danish)` calls remain in the launcher. T/F callers use aliases in the same catalogs. Untranslated diagnostic/protocol literals are not represented as translated user messages.

Player guides and release notes are under `docs/user`. Reusable build/architecture instructions are under `docs/development`; local experiments and dated evidence are under `docs/development/log`. Combat-script validation and the SyncManager development screenshot were separated from player documentation. Current references were updated; immutable release-tag links were retained.

## Validation

- 29 Framework MSTest cases and 6 launch-core MSTest cases passed; both locked restores passed.
- 338 Control Panel integration assertions passed, including English/Danish layouts and saved preferences.
- 159 plugin/server-rule integration assertions passed.
- 131 account/queue/admission/focus assertions passed using synthetic records.
- 8 graphics transaction assertions passed using temporary files.
- 55 embedded-backend assertions and the reparse-ancestor check passed.
- Backend inherited-pipe, UTF-8, cancellation and descendant-preservation fixtures passed.
- 269 documentation link, anchor, image and structure assertions passed.
- Launcher and backend built; the installed executable verified against its 201-file backend manifest.
- A single-EXE smoke fixture exited with code 0 without a sidecar Core DLL.
- Defender found no threats in the final executable. Antivirus and real-time protection remained enabled.

One legacy 32-bit PowerShell UI harness could not start because of that host's execution policy. No policy was changed. The C# Control Panel harness ran through the permitted host instead. The complete set of historical exploratory scripts was not run, and no live six-character login benchmark or relog was performed for this refactor.

CI workflows were added for both MSTest projects; they have not been pushed or run on GitHub. Existing unrelated working-tree changes remain uncommitted.

The new `dist/AONexus.exe` was installed with a timestamped rollback copy in `dist/Previous`. The running Nexus process was left on its old build to preserve unsaved UI work. Restarting Nexus activates the new build.

Installed SHA-256: `6E5198060D3939DB067FF3655C6A4AE3A231A72E384E525D86C84D4005342FA6`.
