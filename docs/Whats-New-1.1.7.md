# AO Nexus v1.1.7

- **Report a problem…** under **Settings → General** and in the activity window builds an anonymised report: versions, Windows, a setup summary, saved crash reports and recent diagnostics. Review it, then open a prefilled GitHub issue, copy it or save it. Nothing is sent until you submit it yourself.
- Logins, account and character names, character IDs (also in `Char…` folder names), team names, custom server names and addresses, game folders and personal paths are replaced with stable codes such as `Char-3fa2c1`, derived from a random key that stays on your PC. Passwords are never recorded.
- A local diagnostics log (`%LOCALAPPDATA%\AONexus\Diagnostics`, max 2 MB) records activity, per-character login steps from the login engine (start, time to the character window, client exit code, timeout and the window state it stopped in), Game GUI and news errors, and exceptions with stack traces. Turn it off or delete it under **Settings → Updates → Diagnostics**.
- Unexpected errors are caught: Nexus saves an anonymised crash report (20 newest kept), lets you continue or close, and offers to report it at the next start.
- New GitHub issue form for bug reports. The login engine changed, so builds need `build\Build.ps1 -RebuildBackend`.

Experimental same-account parallel login changes are not included.
