# 1.3.0 freeze implementation validation

Implemented the [release scope](../Release-1.3.0.md) and [user-facing experiment controls](../../user/Experiments.md).

New persisted, default-off flags: `ExperimentalAovk`, `ExperimentalTeamAutomation`, `ExperimentalNearbyCombat`. Existing minimization moved to the Experiments page; timer death observation remains a per-timer opt-in. English and Danish strings use the shared resource catalog.

Runtime checks cover renderer install/download/save, both backend startup profile decisions, AO-VK background minimization, formation/recovery, nearby aggregation, tabs and exports. The obsolete renderer command-line/environment opt-in was removed. Recovery cancels already-issued plans when disabled, including within its normal poll interval. A saved automatic-formation checkbox cannot override a disabled ordered-login choice when the experiment is off.

Validation on this development PC:

- 35 MSTest launcher/core tests passed, including legacy defaults and explicit round-trip, verified-renderer gating, recovery cancellation, saved tab restoration, settings-dialog cancellation semantics, and independent ordered login.
- 6 launch-core tests passed.
- 335 offline Control Panel checks passed. Existing experimental fixtures now explicitly opt in; removed three obsolete command-line assertions.
- 270 documentation checks passed.
- Release build and embedded payload verification passed (201 backend files).
- Isolated single-executable startup smoke test passed; no game login is part of that test.
- Defender custom scan of the final executable reported no threats. Antivirus and real-time protection were enabled. No exclusions or protection changes.

Final candidate SHA-256: `4FD40B0175D42E1DC297DA27B9932E20B0821A434D273A2740BA148D8B029DF3`.

Local distribution: `dist/AONexus.exe`. Original pre-freeze rollback: `dist/Previous/AONexus.before-feature-freeze-20261101-200633.exe`. The running Nexus instance and game clients were not restarted, and live settings were not edited. New switches take effect when the new executable is started and settings are loaded.

No new live six-client performance, combat-accuracy or renderer stress run was performed for this change. The release gates remain outstanding where they require that evidence; this record does not certify a public release.
