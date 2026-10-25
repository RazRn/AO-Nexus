# AO Nexus v1.0.20

- Settings are divided into General, Accounts, Sidebar and Updates tabs.
- Server news replaces Transmissions. Choose publishers in Settings; the front-page source picker is removed. Optional Server news and Server health share sidebar tabs, or can both be hidden to make more room for accounts.
- Server health gives a compact login, world/zone, chat and official-report overview. It checks only the displayed server, every 60 seconds while visible, with bounded requests and a manual-refresh throttle. Unverified or stale information is explicitly Unknown. Reachability from your PC is distinct from official service reports.
- News refreshes only selected sources while visible and pauses when hidden or in the tray.
- Release notes in the update dialog have readable line breaks, headings and bullets instead of raw Markdown.
- Log retention is adjustable under Settings → Updates, defaulting to 5 MB per log. Oldest entries are trimmed; the activity log remains session-only, while update history stays on disk.
- Update cleanup removes completed staging files, retries abandoned stages after 24 hours, respects active/locked files and keeps one previous executable for recovery. Account data and game files are preserved.
- An offline Privacy & security overview explains password storage, local data, connections, update verification and their limitations. The full [privacy and security document](Privacy-and-Security.md) includes endpoint and retention details.
- Credits now read “Enhanced by Luckyspawn”, following the original AOQuickLauncher attribution. The guide and screenshots are refreshed.

Automatic update checks still run once at startup when enabled, or manually. Download and installation require confirmation. Experimental same-account parallel logins remain excluded.
