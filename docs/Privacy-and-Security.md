# Privacy and security in AO Nexus

This describes the v1.0.21 implementation, not a claim that software or a game server is risk-free. The same overview is available offline under **Settings → General → Privacy & security**.

## Passwords and account data

- Password saving is optional per account. New accounts default to asking at each launch action. An entered password can serve several selected characters on that account in the same action.
- Saved passwords use Windows DPAPI with `CurrentUser`. Records are bound to the account ID, username and configured login endpoint. They live in `%LOCALAPPDATA%\AONexus\Credentials`, separately from the profile JSON.
- Disable **Remember password** in Edit account to remove that account's saved copy. **Settings → Accounts → Forget passwords** removes all Nexus saved copies. **Update saved password** changes the local copy, not the game account's password.
- Passwords exist in process memory during use. Nexus passes launch requests through the backend's redirected standard input, not command-line arguments or a plaintext request file. Managed strings cannot be guaranteed to disappear immediately from memory.
- DPAPI does not defend against malware running as the same Windows user, an administrator, or a compromised running game/backend. It is local storage protection, not transport encryption. The bundled backend uses the game's existing authentication protocol; Nexus does not add TLS to game login. Trust the selected server and game installation before entering credentials.
- Account names, usernames, character IDs, server addresses, local paths and team presets in `AONexus.json` are **not encrypted**. Logs can contain names and paths. Review and redact them before sharing. Nexus does not automatically upload profiles, credentials, logs or crash reports and has no analytics integration.

## Connections made by the app

| Feature | Destination and data | When / control |
| --- | --- | --- |
| Game login | The configured account address and port, through the bundled login backend and game client. Authentication belongs to the selected server. | After a user launch action and confirmation. Game clients make their own world/chat/other connections independently of Nexus. |
| Server news | HTTPS to `www.anarchy-online.com/news`, `project-rk.com/feed.xml`, and `www.ao-rebirth.com/index.php`, restricted to the selected publishers. Standard HTTP metadata, no account credentials. | While Server news is visible, initially and every 15 minutes, or Refresh. Choose sources/disable in Settings → Sidebar; hidden/tray stops automatic requests and cancels in-flight work. |
| Server health | TCP connect-and-close to the built-in or user-configured login/zone/chat endpoints for the currently displayed server; no login or application payload. Public HTTPS reports from `www.ao-rebirth.com/server-status.php` or `project-rk.com/api/status` when applicable. Normal DNS resolution may occur. | Visible health view only, every 60 seconds. Server switches are globally throttled to at least 15 seconds; no overlapping batches. Choose/disable servers under Settings → Sidebar; edit connection targets under Settings → Accounts → Manage health checks. Public report URLs are not editable. See [endpoints and limitations](Server-Health.md). |
| Update check | HTTPS to `api.github.com/repos/RazRn/AO-Nexus/releases/latest`. User-Agent includes Nexus version; no GitHub login/token, game profile or password. | Once per startup if opted in, or manually from Settings → Updates. First start asks for the preference; it remains changeable. No repeating background check. |
| Update download | HTTPS to GitHub; redirects limited to `github.com`, `release-assets.githubusercontent.com`, `objects.githubusercontent.com`. | Only after choosing Install and restart. |
| Open article, website or release | Opens the selected public URL in your default browser. | User click. Browser cookies/history/extensions and the destination's privacy practices apply. |

Remote services can see your public IP address, request time and normal protocol metadata. Windows DNS/proxy/network configuration can also affect these connections. The launcher does not run a public web server, Discord webhook sender or port scan. Custom server health checks use only their configured login endpoint; names alone do not attach a custom server to another server's public report.

## Update verification and recovery

The updater accepts stable releases from `RazRn/AO-Nexus`, requires a newer version, checks the downloaded ZIP against GitHub's SHA-256 metadata, validates the included executable checksum and assembly version, bounds download/extraction sizes, and extracts only the expected executable entry. HTTPS certificate validation is not bypassed. These checks depend on GitHub and the release account; they are **not an independent publisher signature** and do not protect against a compromised maintainer account publishing malicious code.

Installation requires confirmation and write access to the app folder; it does not request administrator elevation. A staged helper waits for the original process, rechecks executable hashes, and atomically replaces the executable with a backup. Failure to start the replacement triggers restoration. Launch success is not a guarantee that every feature works. After startup, cleanup retains one previous Nexus executable for manual recovery. Your game windows, profile and credentials are not part of replacement.

## Local files and retention

| Location | Retention |
| --- | --- |
| `%LOCALAPPDATA%\AONexus\AONexus.json` and profile recovery files | Saved settings; preserved across updates. |
| `%LOCALAPPDATA%\AONexus\Credentials` | Encrypted passwords until removed through account settings/Forget passwords. |
| `%LOCALAPPDATA%\AONexus\News` | Bounded per-source news cache, reused for offline fallback. |
| `%LOCALAPPDATA%\AONexus\Runtime` | Verified, content-addressed bundled login engine. Reused when the payload is unchanged. Old runtime versions are preserved because open game clients may still use their native components. |
| In-memory activity log | Current session only. Default 5 MB of UTF-8 text; adjustable 1–100 MB under Settings → Updates. Oldest entries are discarded first. The searchable window shows the same retained log. This is a text limit, not a total RAM limit. |
| `%LOCALAPPDATA%\AONexus\Updates\update-history.log` | Same configurable cap per log, newest entries retained; no accumulating rotated copies. Lowering the cap trims existing history. |
| `%LOCALAPPDATA%\AONexus\Updates\<GUID>` | Download/extraction failures are cleaned immediately where possible. Completed stages are cleaned after the updated app starts. Abandoned stages are retried on startup once older than 24 hours; failed-update details are also recorded in bounded history. Locked stages are skipped and retried next startup. |
| `AONexus.exe.previous-<GUID>` next to the app | One most recent previous executable is retained per installation; older verified Nexus backups are removed on startup. |
| `%LOCALAPPDATA%\AOQuickLauncher` | Shared backend client tracking/session coordination and possibly a legacy launcher profile. Nexus does not erase the old app's profile. |

Cleanup only removes recognized updater-owned files in direct GUID directories, rejects reparse paths, does not recurse through unknown subdirectories, and respects the active updater lock. Unknown files are left alone. It does not modify game installs, Prefs or other applications. A folder inaccessible to Windows cannot be guaranteed to be cleaned until access is restored.

For complete removal, close Nexus, disable Start with Windows, finish/close game clients using its runtime, then remove the installation and the Nexus-specific AppData folder yourself. Keep a private profile backup if wanted. Do not delete the shared AOQuickLauncher folder blindly if you also use another launcher. Local filesystem deletion does not erase external backups, cloud copies or another application's stored credentials.

## Source references

The relevant implementation is in `CredentialStore.cs`, `AccountPasswords.cs`, `BackendRunner.cs`, `AppUpdates.cs`, `LocalMaintenance.cs`, `ServerNews.cs`, `ServerHealth.cs`, `ServerSidebar.cs`, and `Models.cs` under `src/Launcher`. The inherited login protocol and client integration are under `src/Backend`.
