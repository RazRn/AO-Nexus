# AO Nexus v1.1.9

- **Login progress per character.** `LoginHandler.dll` reports each stage from inside the game client — exports found, hooks attached, login screen ready, login-server connection result, every login-screen state change, character list reached and character requested — to the login engine over a private, current-user-only pipe. Reports contain fixed stage names and numbers only, never usernames, passwords or character names, and a missing or full pipe never affects the login. Timelines appear in the anonymised diagnostics log and in Report a problem, so a stuck login shows *where* it stopped (for example "character list reached, then timeout").
- **Windows error dialogs are captured.** If a game client shows a standard Windows dialog while logging in, its text is added to the diagnostics (anonymised).
- **Spaced client starts.** Starts from the same game folder are at least 1.5 s apart; other folders are not delayed. AO briefly locks files in its folder while starting, and simultaneous starts could fail with *No write access to the main AO folder*.
- **Experimental: Skip drawing the login screens** (Settings → Accounts). Enables the login handler's quiet mode: the client goes straight to the selected character without drawing the login and character screens.
- Diagnostics also record when the character name first appeared, separating game load time from Nexus's own confirmation.

Builds need `build\Build.ps1 -RebuildBackend`. Experimental same-account parallel login changes are not included.
