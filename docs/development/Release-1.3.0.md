# 1.3.0 feature freeze

Decision: 1 November 2026. The feature set is frozen. Remaining work is bug fixes, regression tests, accessibility/layout corrections, documentation and packaging. This is a scope decision, not a claim that release validation is complete.

## Core release scope

- Accounts, server selection, character selection, saved teams and ordered launches. Keeping the first client active during ordered startup remains core.
- Stock-client startup acceleration on verified compatible binaries, cancellation, progress and failure reporting.
- User-selected local AO# installations, existing plugin compatibility, shared/profession sets, load status and diagnostics. No public redistribution of unlicensed AO# or plugin bundles.
- Control Panel: character selection, health/nano/activity, response and memory status, optional Windows thumbnails, card order, collapsible/reorderable sections, logout/relog with confirmation and manual recovery.
- Team combat totals and per-pet breakdown, sorting, reset and script export. Accuracy regressions are release bugs; missing measurements must remain explicit.
- Manual timers, scripts/macros, layouts, skins and backups.
- Settings persistence, English/Danish resources, server news/health, update checks, error/crash diagnostics. Detailed development recording remains an explicit local opt-in.

## Experimental scope and enforcement

| Feature | Setting | Off behavior |
| --- | --- | --- |
| AO-VK management, effects and startup tuning | Settings → Advanced → AO-VK (`ExperimentalAovk`) | Graphics changes/downloads are blocked; AO-VK startup optimization and AO-VK auto-minimizing are not used. Interrupted graphics transaction recovery remains available. |
| Automatic formation, invitations and leader restoration | Settings → Advanced → automatic teams (`ExperimentalTeamAutomation`) | Preset choices remain saved but cannot start formation; the recovery controller cancels its outstanding plans and stops observing teams. |
| Nearby-player and NPC combat reports | Settings → Advanced → nearby combat (`ExperimentalNearbyCombat`) | All/NPC tabs and report export are unavailable; Nexus stops updating those aggregators. Team statistics remain available. |
| Automatic background-client minimization | Settings → Advanced → minimize inactive clients (`MinimizeInactiveClients`) | No ongoing automatic minimization. Ordered-launch focus handling is separate. |
| NPC death observation for timers | Timer editor → observe deaths (`ObserveDeaths`) | Timers are manual. Existing per-timer opt-ins remain saved. |

The three new settings default **off**, including when loading an older profile. Existing explicit minimization and per-timer choices are preserved. Do not infer consent from renderer files, prior command-line arguments, saved AutoFormTeam presets or the last selected statistics tab. Disabled tabs retain their preference so re-enabling restores the selection. Settings-dialog Cancel must not persist experiment changes.

AO-VK off does not uninstall or unload a renderer already installed in the game folder; it gates Nexus management and tuning. Uninstalling is a separate explicit graphics change with clients closed. These switches are not plugin permission boundaries: already-loaded plugins may continue publishing telemetry or provide their own team automation. Nearby-report flags do not rewrite a running plugin. Previously exported scripts remain on disk.

Unknown renderer combinations are not admitted by production startup tuning even with AO-VK enabled. Character-showcase renderer extraction, hover animation, new renderer variants and CPU-affinity features are outside this release. No experiments are promoted to core during the freeze without an explicit scope decision and recorded evidence.

## Release gates

1. Unit tests and offline launch/UI regressions pass with experiments off and with each affected experiment enabled.
2. Old-profile migration retains accounts, passwords, presets, plugin selection and Control Panel preferences without enabling new experiments.
3. Clean-machine packaging works without this developer's game folders. No unlicensed optional bundle is published.
4. Current Defender protection stays enabled. Scan the exact candidate artifact; no exclusions or detection-evasion workarounds. A clean local scan is not a guarantee for future signatures.
5. Record a stock-client six-character startup/stop/relog check and test opted-in AO-VK separately. Measure elapsed time and failures, rather than claiming an unmeasured ten-second target.
6. Check missing-plugin, process-exit and stale-telemetry paths; ensure settings disable outstanding automation and UI labels do not imply server ping.
7. Unresolved crash/data-loss or default-path regression blocks release. Optional experimental failures do not justify weakening default-path checks.

Verification evidence belongs in the development log. Do not mark outstanding live-client checks as passed from offline tests.
