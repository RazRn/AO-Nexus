# AO Nexus v1.1.8

- **Fix:** character IDs written directly before punctuation — for example the login engine's *Waiting for the game window for ID 123456789...* — were not anonymised in the diagnostics log and problem reports in v1.1.7. They are now replaced with the same `Char-…` code as the character name. Found by an emulated failed login.
- Diagnostics saved by v1.1.7 can still contain such IDs. Clear them with **Settings → Updates → Delete diagnostics now**; nothing was ever sent automatically.
- Build and test scripts run in Windows PowerShell 5.1 as well as PowerShell 7.

Includes everything from [v1.1.7](https://github.com/RazRn/AO-Nexus/releases/tag/v1.1.7): **Report a problem…**, the anonymised diagnostics log and crash reports.

Experimental same-account parallel login changes are not included.
