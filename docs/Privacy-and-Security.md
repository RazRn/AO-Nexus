# Privacy and security in AO Nexus

This describes the implementation in this source checkout, not a claim that software or a game server is risk-free. An overview is also available offline under **Settings → General → Privacy & security**.

## Passwords and account data

- Password saving is optional per account. New accounts default to asking at each launch action. An entered password can serve several selected characters on that account in the same action.
- Saved passwords use Windows DPAPI with `CurrentUser`. Records are bound to the account ID, username and configured login endpoint. They live in `%LOCALAPPDATA%\AONexus\Credentials`, separately from the profile JSON.
- Disable **Remember password** in Edit account to remove that account's saved copy. **Settings → Accounts → Forget passwords** removes all Nexus saved copies. **Update saved password** changes the local copy, not the game account's password.
- Passwords exist in process memory during use. Nexus passes launch requests through the backend's redirected standard input, not command-line arguments or a plaintext request file. Managed strings cannot be guaranteed to disappear immediately from memory.
- DPAPI does not defend against malware running as the same Windows user, an administrator, or a compromised running game/backend. It is local storage protection, not transport encryption. The bundled backend uses the game's existing authentication protocol; Nexus does not add TLS to game login. Trust the selected server and game installation before entering credentials.
- Account names, usernames, character IDs, server addresses, local paths and team presets in `AONexus.json` are **not encrypted**. Logs can contain names and paths. Review and redact them before sharing. Nexus does not automatically upload profiles, credentials, logs or crash reports and has no analytics integration.

## Connections made by the app

For a fresh profile, the setup wizard runs before optional news, health or GitHub checks. Finish saves the selected preferences; Skip leaves these features off. Adding accounts during setup does not log in, request passwords or contact a game server. Existing/imported profiles retain their prior choices and are not forced through setup again.

| Feature | Destination and data | When / control |
| --- | --- | --- |
| Game login | The configured account address and port, through the bundled login backend and game client. Authentication belongs to the selected server. | After a user launch action and confirmation. Game clients make their own world/chat/other connections independently of Nexus. |
| Server news | HTTPS to `www.anarchy-online.com/news`, `project-rk.com/feed.xml`, and `www.ao-rebirth.com/index.php`, restricted to the selected publishers. Standard HTTP metadata, no account credentials. | While Server news is visible, initially and every 15 minutes, or Refresh. Choose sources/disable in Settings → Sidebar; hidden/tray stops automatic requests and cancels in-flight work. |
| Server health | TCP connect-and-close to the built-in or user-configured login/zone/chat endpoints for the displayed server, or all selected servers when background checks are enabled; no login or application payload. Public HTTPS reports from `www.ao-rebirth.com/server-status.php` or `project-rk.com/api/status` when applicable. Normal DNS resolution may occur. | Every 60 seconds while visible by default. Opt-in background monitoring continues across all selected servers while Nexus runs, including hidden/in the tray; enabling tray notifications also enables background monitoring. Disable background checks and hide the health panel to stop requests. Batches do not overlap and are throttled by at least 15 seconds. Choose/disable servers under Settings → Sidebar; edit connection targets under Settings → Accounts → Manage health checks. Public report URLs are not editable. See [endpoints and limitations](Server-Health.md). |
| Update check | HTTPS to `api.github.com/repos/RazRn/AO-Nexus/releases/latest`. User-Agent includes Nexus version; no GitHub login/token, game profile or password. | Once per startup if opted in, or manually from Settings → Updates. First start asks for the preference; it remains changeable. No repeating background check. |
| Update download | HTTPS to GitHub; redirects limited to `github.com`, `release-assets.githubusercontent.com`, `objects.githubusercontent.com`. | Only after choosing Install and restart. |
| GUI skin download | HTTPS to author-published `drive.google.com` links and `drive.usercontent.google.com` redirects. Pinned SHA-256 packages, bounded downloads and extraction; no game credentials. | Only when Download is chosen in Game GUI. Download installs separate data files; Apply skin is a separate confirmed operation. |
| Open article, website or release | Opens the selected public URL in your default browser. | User click. Browser cookies/history/extensions and the destination's privacy practices apply. |

Remote services can see your public IP address, request time and normal protocol metadata. Windows DNS/proxy/network configuration can also affect these connections. The launcher does not run a public web server, Discord webhook sender or port scan. Custom servers start with a login connection check; you can also configure world/zone and chat checks under **Settings → Accounts → Manage health checks**. A server name alone does not attach a custom server to another server's public report.

## Update verification and recovery

The updater accepts stable releases from `RazRn/AO-Nexus`, requires a newer version, checks the downloaded ZIP against GitHub's SHA-256 metadata, validates the included executable checksum and assembly version, bounds download/extraction sizes, and extracts only the expected executable entry. HTTPS certificate validation is not bypassed. These checks depend on GitHub and the release account; they are **not an independent publisher signature** and do not protect against a compromised maintainer account publishing malicious code.

Installation requires confirmation and write access to the app folder; it does not request administrator elevation. A staged helper waits for the original process, rechecks executable hashes, and atomically replaces the executable with a backup. Failure to start the replacement triggers restoration. Launch success is not a guarantee that every feature works. After startup, cleanup retains one previous Nexus executable for manual recovery. Your game windows, profile and credentials are not part of replacement.

## Local files and retention

Game GUI templates contain only selected supported preferences, never the whole account folder. Templates and backups can contain character labels, local paths and personal GUI settings. They are not encrypted or uploaded automatically. Skin imports reject executable/script entries and unsafe archive paths; the author download's optional command script is omitted and never executed. Package hashes identify reviewed contents, not a publisher signature. Data validation cannot guarantee compatibility or appearance in every game client.

GUI changes require confirmation and affected clients must exit before preference writes. Nexus can ask to relog known clients via `/quit`, waits for actual process exit, and rechecks before writing. Unknown clients block the change. Character-only operations match affected character identities and preference paths. For shared skin changes, an unverified Prefs-to-game mapping can expand the confirmation to all identified running AO clients. Cancellation cannot undo an already completed logout. File transactions back up changes and attempt rollback on failure; unresolved recovery data is preserved. Do not manually launch affected clients or edit their preferences during an operation.

| Location | Retention |
| --- | --- |
| `%LOCALAPPDATA%\AONexus\AONexus.json` and profile recovery files | Saved settings; preserved across updates. |
| `%LOCALAPPDATA%\AONexus\Credentials` | Encrypted passwords until removed through account settings/Forget passwords. |
| `%LOCALAPPDATA%\AONexus\GuiLayouts` | Templates until deleted. Latest 10 completed layout backups per character and 10 successful skin-selection backups per Prefs installation; interrupted/unrecognized recovery records remain for inspection. |
| `Gui` beside the game's `Prefs` folder | Installed custom skins remain for reuse. Identical imports reuse matching contents. Temporary download/import staging is cleaned on success or failure. Original game skin files are not replaced. |
| `%LOCALAPPDATA%\AONexus\News` | Bounded per-source news cache, reused for offline fallback. |
| `%LOCALAPPDATA%\AONexus\Runtime` | Verified, content-addressed bundled login engine. Reused when the payload is unchanged. Old runtime versions are preserved because open game clients may still use their native components. |
| In-memory activity log | Current session only. Default 5 MB of UTF-8 text; adjustable 1–100 MB under Settings → Updates. Oldest entries are discarded first. The searchable window shows the same retained log. **Clear log** asks for confirmation and clears the entire session log, including entries hidden by a search; it does not clear the separate update history. This is a text limit, not a total RAM limit. |
| `%LOCALAPPDATA%\AONexus\Updates\update-history.log` | Same configurable cap per log, newest entries retained; no accumulating rotated copies. Lowering the cap trims existing history. |
| `%LOCALAPPDATA%\AONexus\Updates\<GUID>` | Download/extraction failures are cleaned immediately where possible. Completed stages are cleaned after the updated app starts. Abandoned stages are retried on startup once older than 24 hours; failed-update details are also recorded in bounded history. Locked stages are skipped and retried next startup. |
| `AONexus.exe.previous-<GUID>` next to the app | One most recent previous executable is retained per installation; older verified Nexus backups are removed on startup. |
| `%LOCALAPPDATA%\AOQuickLauncher` | Shared backend client tracking/session coordination and possibly a legacy launcher profile. Nexus does not erase the old app's profile. |

Cleanup only removes recognized updater-owned files in direct GUID directories, rejects reparse paths, does not recurse through unknown subdirectories, and respects the active updater lock. Unknown files are left alone. It does not modify game installs, Prefs or other applications. A folder inaccessible to Windows cannot be guaranteed to be cleaned until access is restored.

For complete removal, close Nexus, disable Start with Windows, finish/close game clients using its runtime, then remove the installation and the Nexus-specific AppData folder yourself. Keep a private profile backup if wanted. Do not delete the shared AOQuickLauncher folder blindly if you also use another launcher. Local filesystem deletion does not erase external backups, cloud copies or another application's stored credentials.

## Source references

The relevant implementation is in `CredentialStore.cs`, `AccountPasswords.cs`, `BackendRunner.cs`, `AppUpdates.cs`, `LocalMaintenance.cs`, `ServerNews.cs`, `ServerHealth.cs`, `HealthEndpoints.cs`, `ServerSidebar.cs`, `GuiLayouts.cs`, `GuiSkins.cs`, `GuiSessions.cs`, `MainForm.GameGui.cs`, and `Models.cs` under `src/Launcher`. The inherited login protocol and client integration are under `src/Backend`.
