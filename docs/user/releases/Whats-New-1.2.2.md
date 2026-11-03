# AO Nexus v1.2.2

Client reports now make it clear when they contain no responsiveness measurements, and provide more useful evidence for troubleshooting longer sessions.

- **Understand missing measurements.** Character status and reports distinguish monitoring being disabled from missing, unreadable, invalid, unsupported or mismatched plugin snapshots. These reasons help investigate setup; they do not establish the root cause.
- **Keep earlier peaks and gaps.** Alongside the latest 120 samples, a summary retains state counts, plugin coverage, peak memory and maximum observed update gaps and reply times for the period Nexus observed each client.
- **Easier reports to follow.** Start with `report-summary.txt`. Aliases are consistent across the ZIP, duplicate saved exits are omitted, and distinct historical evidence is preserved.
- **Correct Windows information.** Reports use the installed Windows version and build from the registry rather than the legacy compatibility version.
- **Updated help.** The player guide, monitoring instructions and privacy documentation explain the new report contents and their limits.

Regular process samples do not prove the game was responsive. Exit code zero and absent Windows events do not rule out a crash or hang. A short memory window cannot establish whether a leak exists.

Monitoring remains optional. Enable it in **Settings → Accounts**, then enable **Nexus Health Monitor** for each relevant character in AOSharp under the same Windows user. Existing v1.2.1 monitoring plugins remain compatible; this update does not require reloading them. Older reports cannot recover missing measurements or earlier history.

Download **AO-Nexus-Luckyspawn.zip** from the release assets. Existing profiles and saved password preferences are preserved. Nothing is uploaded automatically.
