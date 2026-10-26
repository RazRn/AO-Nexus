# AO Nexus 1.2.0 — Clearer Game GUI and faster launches

Game GUI now separates four jobs into guided workflows:

- **Layouts:** choose a saved setup, select characters and choose the parts to replace. Save and copy actions use clearer language; less common actions are under More actions.
- **Skins:** choose the installation first, see which accounts share it, then add or activate a skin.
- **Scripts:** separate script files from character macros. Preview script contents, choose a destination and review before replacing files. Macros have their own destination selection and explain their effect on existing hotbar buttons.
- **Backups:** filter the history by type and review the original target, date and affected settings before restoring.

This release also includes the accumulated local launcher improvements: integrated NexusEngine startup, supported classic-client loading optimizations, team and per-character timing, dark first paint, a smoother header activity line, and optional minimization only when switching between managed AO windows.

Optional AOSharp monitoring shows local update and IPC responsiveness in the header and tray. Stale observer measurements are distinguished from client delays, and warnings include their reason and timings. The plugin and helper are included in the portable executable, including when updating an existing installation. Enable Nexus Health Monitor in your existing AOSharp profiles to collect measurements.

Optional client-exit reports can save recent measurements and matching Windows errors as a ZIP. Existing users receive a short review of the new settings. Reports stay local until you choose to share them.

## Download

Download **AO-Nexus-Luckyspawn.zip** below and extract it into a writable folder. Run **AONexus.exe**. The automatically generated Source code archives are for developers. Existing accounts and preferences remain in the same local Nexus profile.

A SHA-256 checksum is provided alongside the ZIP. AOSharp itself and the game are not installed by this package.

## Limits

Classic startup optimizations require the verified supported client build. Loading a zone and contacting the server still take time. Game GUI changes keep their backup and normal-logout protections. Monitoring measures the local AOSharp update loop and test IPC path, not server packet loss or every other plugin's messages. A process exit alone is not proof of a crash.
## Validation

The Windows release build passed the full automated verification suite, including the real local AOSharp IPC fixture, startup/account/session tests, password and profile handling, updates, diagnostics, layout/skin/script recovery and embedded payload verification. The redesigned Game GUI passed 1,453 usability checks across English/Danish, minimum/default sizes and 150% scaling, plus the existing 227 manager checks. All five screen captures were visually reviewed; 239 documentation/image checks passed.

The classic texture-filtering implementation matched 100,000 cases against the verified client DLL. The package contains 12 allowlisted public files and verified embedded login/monitoring components. Automated GUI tests use synthetic accounts and temporary files; they do not change a player's live layouts or macros.