AO NEXUS — OFFLINE QUICK GUIDE
Based on AOQuickLauncher by never-knows-best and contributors.
Enhanced by Luckyspawn.

GET STARTED
1. Extract the complete release ZIP into a folder you can write to, then start
   AONexus.exe. The game must be installed separately for your chosen server.
2. Choose Add account. Enter a display name and the actual game login username.
3. In Game and server, choose the server and folder containing AnarchyOnline.exe.
4. Import character IDs from the account's Prefs folder, or save the account and
   use Add character. Check the characters you want to play.
5. Choose Log in selected, review the confirmation and enter passwords as needed.

Requires Windows 10/11 and .NET Framework 4.8. The login engine and its runtime
are bundled; players do not need a .NET SDK or a separate AOQuickLauncher folder.
The app version appears in the header. The source download is not the runnable ZIP.

ACCOUNTS, PREFS AND CHARACTERS
Prefs are optional. Find Prefs folders helps identify existing installations.
Select the account's folder under Prefs and use Find characters to import IDs.
The usual starting point is %LOCALAPPDATA%\Funcom\Anarchy Online.
Review the suggested game folder and server before saving. Imported chars start
unchecked; their actual names are detected when they enter the game.
Without Prefs, log into a char normally, press Shift+F9 and find its ID. For
50000:123456789, enter 123456789 under Add character. Remove only removes the
launcher entry; it does not delete the character on the game server.
Create new char opens the game's creation screen when the configured capacity
permits. Keep Nexus open until the char enters the world for automatic detection.
If detection ends, import its Prefs or add its ID manually.

TEAMS AND LOGIN
Check chars across accounts/servers, then use Team presets > Save new.
Selecting a saved team restores its checkboxes; it does not start games.
Update saves changed checkboxes to that team. Edit changes its name, shortcuts
and startup default. No preset selected clears current checkboxes, not saved teams.
Use as startup default selects a team when Nexus opens, without logging in.
Without a startup default, Nexus keeps manual checkboxes and selects no preset.
Separate accounts can start in parallel. Multiple chars on the same account and
server start one at a time; opened clients stay open, subject to server limits.
The header line shows confirmed login/logout progress and waiting activity.

STOP, LOG OUT OR CLOSE NEXUS
Stop login stops further login activity; game windows already opened stay open.
In a team's Save new/Edit dialog, assign separate Start and Logout shortcuts.
Click a field and press Ctrl/Alt plus a key; Delete clears and Esc cancels recording.
Shortcuts work while Nexus runs, including in the tray. Save commits the changes.
Logout asks before sending normal /quit to safely matched clients, then watches
their actual exit for up to 90 seconds. Combat or movement may interrupt logout.
Clients are never forcibly closed. Start clients normally through the current
Nexus version to enable quit control; newly created clients need a normal launch.
Closing Nexus leaves game windows open but stops its shortcuts and monitoring.
Minimize hides Nexus in the tray. Click the tray icon to restore it. Minimize and
Quit confirmations have separate Don't show this again preferences in Settings.
Start with Windows is optional and starts Nexus in the tray, without game logins.
Only one Nexus instance runs per Windows user session; reopening restores it.

PASSWORDS AND SERVER SETTINGS
Remember password is optional per account. Unchecked: enter it once per account
for each login action. Checked: save it encrypted for your current Windows user.
After changing a game password, select the account and Update saved password.
This replaces only Nexus's saved copy, not the password on the game server.
Uncheck Remember password and save the account to remove its saved password.
Settings > Accounts > Forget passwords clears all saved account passwords.
Remembering preferences remain; the next prompt can save a new password.
Each account has its own game folder and server. Settings > Accounts contains
the editable server catalog. Max. accounts and Chars per account limit simultaneous
logins; Character slots is stored-character capacity. Zero means no launcher
limit/unknown. These settings do not change the game server's rules or capacity.

NEWS, HEALTH AND LANGUAGE
Settings > Sidebar chooses news sources and enables Server news/Server health.
News refreshes every 15 minutes while visible; headlines open their source sites.
Health checks run every 60 seconds while visible by default. Optional background
checks cover selected servers while Nexus runs, including in the tray.
Optional tray notifications enable background checks: two failed checks confirm
an issue; one successful check confirms recovery. Windows controls popup duration.
Reachable means TCP connectivity from your PC, not proof that game login works.
Unknown means missing, stale or unverified information. Health endpoint addresses
can be edited under Settings > Accounts > Manage health checks; login is separate.
Settings > General offers English, Danish or automatic Windows-language selection.

GAME GUI — EXPERIMENTAL
Layouts lets you capture a saved char setup or copy one to selected targets. Choose
window layout, hotbars, key bindings and pet names separately. Use for new chars
queues a personal default until their saved Prefs exist and the clients are offline.
AO default layout resets supported presentation settings; Apply saved default
uses your personal template instead. Skins affects shared installation preferences.
Downloads/imports do not activate a skin until Apply skin. Test one char first.
Nexus confirms affected clients, requests /quit and waits for actual exit before
reading changed source Prefs or writing target Prefs. It backs up changes and
can relog identified clients afterward. Unknown clients must be closed manually.
Use Backups for recovery. Do not reopen affected clients during a file operation.

LOGS, UPDATES AND LOCAL DATA
Click the terminal log preview to open its searchable window. Ctrl+F focuses
search. Clear log asks before clearing this session, including filtered-out lines.
Activity is held in memory; clearing it does not delete the saved update history.
Settings > Updates limits each log to 5 MB by default (adjustable 1–100 MB).
Startup update checks are opt-in; manual checks are in Settings > Updates.
Checks happen once at startup or on request, not repeatedly in the background.
Review release notes, then choose Install and restart. Updates require confirmation;
finish/stop pending launcher operations first. Your profile and games are kept.
Checksums verify downloads against GitHub release metadata; they are not an
independent publisher signature. The application folder must be writable.
Completed update stages are cleaned after restart; one previous EXE is retained.
Profiles, encrypted passwords, news, templates and runtimes live under
%LOCALAPPDATA%\AONexus. Account names, paths and presets are not encrypted.
Nexus has no analytics or automatic log uploads. Review logs before sharing them.
Share the release ZIP, not your personal AppData folders. Back up profiles privately.

FULL GUIDES AND SOURCE
Player guide: https://github.com/RazRn/AO-Nexus/blob/main/docs/Player-Guide.md
Game GUI: https://github.com/RazRn/AO-Nexus/blob/main/docs/Game-GUI.md
Server health: https://github.com/RazRn/AO-Nexus/blob/main/docs/Server-Health.md
Privacy: https://github.com/RazRn/AO-Nexus/blob/main/docs/Privacy-and-Security.md
Build instructions: https://github.com/RazRn/AO-Nexus/blob/main/docs/Building.md
Downloads: https://github.com/RazRn/AO-Nexus/releases

Independent community project; not an official Funcom, Project Rubi-Ka or AO
Rebirth product. See CREDITS.txt, Licenses and LICENSE-NOTICE.txt for attribution,
dependency terms and the unresolved upstream redistribution-license status.
