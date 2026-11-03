# Guide images

The README and guides contain 28 distinct captures from AO Nexus 1.3.0 Beta 1: the main screens, setup/account dialogs, Control Panel, Damage Dumper, plugins, timers, seven Settings pages and the Game GUI tools.

These are real WinForms controls with offline example accounts, empty passwords and synthetic measurements. No personal profile, live game scene or third-party plugin pack is published. The images are full client areas without annotation overlays. Screenshots illustrate controls, not proof of live server health or gameplay validation.

## Regenerate

Build the launcher, then run these commands from the repository root:

~~~powershell
& "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -File tests/Capture-GuideScreenshots.ps1 -AssemblyPath dist/AONexus.exe
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Verify-Guide.ps1
~~~

The capture fixture uses isolated models and timer data. Control Panel sample clients have synthetic process IDs; formation polling is disabled and no client command is sent. The Game GUI usability checks use temporary files. Raw captures stay in ignored logs/guide-captures and logs/game-gui-usability.

[Guide-ImagePlan.ps1](../../../tests/Guide-ImagePlan.ps1) records expected sizes. [Capture-ControlPanelGuide.ps1](../../../tests/Capture-ControlPanelGuide.ps1) supplies the Beta-specific examples. The publisher updates image references and [SCREENSHOTS.md](SCREENSHOTS.md), records SHA-256 hashes and removes obsolete generated images only inside the documentation image directories.

Inspect every capture after layout changes. Automated checks verify links, dimensions and provenance, but cannot judge clipped controls or misleading sample data. Already-published release archives are not changed by refreshing these source documentation images.
