# 1.3.0 downloadable release — 3 November 2026

The owner requested normal v1.3.0 publication so existing stable-only updaters can offer the release, and explicitly deferred the runtime-review/Microsoft-resolution prerequisite. The assembly/file version is 1.3.0.0 and informational/display version is 1.3.0. This does not assert that the historical Defender incident was resolved or the known Engineer casting defect was fixed.

Package.ps1 retains strict runtime review by default. Its explicit deferral option requires a reason and records that decision separately from scan outcomes. No Defender setting was changed. The release check ran elevated to perform Microsoft's exclusion query, which was unavailable in the earlier unelevated shell.

Final evidence:

- Defender exclusion checks succeeded and exact executable/ZIP scans reported no detection; real-time, antivirus and behavior protection remained enabled.
- Executable SHA-256: `7c3e36e82de147f87727f9e563ab470a137dd1ab7097a78d3e55eb23e3c123d1`.
- ZIP SHA-256: `c377566b8deb2fdf007628de83242e56702ac987f2627f213ad2254abf455bb2`.
- 201 embedded backend files and 14 monitoring/single-file upgrade checks passed.
- 39 updater checks and 29 release-policy checks passed, including explicit versus implicit review deferral.
- Package contains the executable, public documentation, dependency notices and checksums; no local profiles, credentials, AO# installation or plugin bundles.

Known limitations and existing attribution/licensing notices remain documented. Live gameplay results belong to the earlier release-readiness records; this branding/package pass does not claim another live six-client run.
