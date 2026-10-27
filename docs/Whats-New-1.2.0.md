# AO Nexus 1.2.0

A redesigned Game GUI, faster supported classic-client launches, and optional AOSharp health monitoring.

**[Download AO Nexus for Windows](https://github.com/RazRn/AO-Nexus/releases/download/v1.2.0/AO-Nexus-Luckyspawn.zip)** · [SHA-256 checksum](https://github.com/RazRn/AO-Nexus/releases/download/v1.2.0/AO-Nexus-Luckyspawn.zip.sha256.txt)

Extract the ZIP into a writable folder and run **AONexus.exe**. Existing accounts and preferences stay in your local Nexus profile.

## Game GUI, made clearer

- **Layouts:** choose a saved layout, select characters, and review which settings to replace.
- **Skins:** choose a game installation and see which accounts share it before activating a skin.
- **Scripts and macros:** separate workflows, with script previews and clear destination choices.
- **Backups:** filter saved backups and see what will be restored before applying them.

[Read the Game GUI guide](https://github.com/RazRn/AO-Nexus/blob/v1.2.0/docs/Game-GUI.md)

## Other improvements

- **Integrated startup:** Nexus handles launching directly, with loading optimizations for the supported classic client and timings for each character and the whole team.
- **Smoother interface:** dark window startup, a smoother activity line, and optional minimization when switching between managed AO windows.
- **Optional AOSharp monitoring:** client responsiveness appears in the header and tray. Enable Nexus Health Monitor in your AOSharp profiles to receive measurements.
- **Local diagnostic reports:** save recent measurements and matching Windows errors as a ZIP to help investigate client exits.
- **New settings explained:** existing users get a short review of newly added options.

<details>
<summary>Compatibility and monitoring details</summary>

Classic loading optimizations require the verified supported client build. Server communication and zone loading still take time.

AOSharp and the game are installed separately. The Nexus monitoring plugin and helper are included in the launcher, including through the updater. Monitoring measures the local AOSharp update loop and test IPC path; it does not measure server packet loss or every plugin's messages.

Game GUI changes retain backup and normal-logout protections. Diagnostic reports stay local until you share them, and a client exit alone does not prove a crash.

</details>

<details>
<summary>Testing</summary>

The Windows release passed the full automated verification suite. This includes 1,453 Game GUI usability checks across English/Danish, minimum/default window sizes and 150% scaling, plus 227 existing manager checks. All five Game GUI screenshots were visually reviewed, and 239 documentation/image checks passed.

Startup, account/session handling, passwords, profiles, updates, diagnostics, layout/skin/script recovery, local AOSharp IPC and embedded components were also verified. The classic texture-filtering implementation matched 100,000 test cases against the verified client DLL. The release package contains 12 allowlisted public files.

Automated GUI tests use synthetic accounts and temporary files without changing players' live layouts or macros.

</details>
