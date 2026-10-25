AO NEXUS 1.1.5
Based on AOQuickLauncher by never-knows-best and contributors.
Enhanced by Luckyspawn.

START
Double-click AONexus.exe. Keep this folder anywhere you prefer.
The installed version appears at the lower-right corner of the app header.
The login engine is compressed inside the EXE; no second app folder is needed.
Windows 10/11 with .NET Framework 4.8 is required. The game is installed separately.

MINIMIZE TO TRAY
Minimize hides Nexus in the Windows system tray, removing it from the taskbar
and Alt+Tab. Login queues and new-character detection continue in the background.
Click the Nexus tray icon to restore it, or right-click for Open AO Nexus / Exit
AO Nexus. Look under the tray's hidden-icons arrow if Windows hides the icon.
The window's X still closes Nexus. Exiting leaves your game windows open.
Only one Nexus instance runs per Windows user session. Opening Nexus again
restores the existing window, even when it is in the tray.

START WITH WINDOWS
In Settings, check Start with Windows and Save to start Nexus in the tray when
you sign in to Windows. This is off by default and never logs game accounts in.
Uncheck it and Save to remove automatic startup. No administrator rights needed.
After moving the EXE, open it from the new location and Save Settings again to
update the startup path. Windows Task Manager can also disable startup apps.

PRESET SHORTCUTS
Under Team presets choose Save new or Edit. Click Start shortcut or Logout shortcut and press your
combination (for example Ctrl+Alt+F6 and Ctrl+Alt+F7), then Save. Cancel discards edits.
Click a field and press Delete/Backspace to clear it, or Esc to cancel recording.
Shortcuts are unassigned by default and work while Nexus runs, including in the
tray. Duplicate or unavailable combinations are reported. F12 and Windows-key
combinations are reserved. Start shows the usual launch confirmation.
Logout asks before sending normal /quit to the matched preset clients. Combat,
movement or cancellation may prevent logout. Nexus never forces a game closed.
Older running clients need a normal restart through v1.0.15 or later to enable
quit control. Newly created chars also need one normal launch after being saved.
AOSharp is not required. Closing Nexus itself leaves all game clients open.

FIRST START
If AO Launcher is already set up on this Windows profile, AO Nexus copies its
accounts, presets, server catalog and encrypted saved passwords on first start.
The original files are not changed. From then on the two apps save independently.
Remembered passwords remain protected by Windows for the current Windows user.
Share this release ZIP, not your AppData folders.

PLAY
Add an account and choose its game installation and server. Import your account's
Prefs folder to discover character IDs, or add characters manually.
Check the characters to launch, or choose a saved setup.
The narrow panel beside the account list shows the selected account, char or
server. Confirm the account, server and character counts before logging in.
Stop login stops the queue;
it does not close your game windows.
Create new char appears below an account while its configured capacity permits.
It starts a client in character creation; finish creation in the game. Keep
AO Nexus open: when the char enters the world, its name and ID are detected
from that game client and added automatically, unchecked. Prefs are not required.
You can use the launcher while creation is open. Detection waits up to one hour.
Closing AO Nexus stops detection and leaves game windows open. If detection
ends or the client does not support it, import Prefs or add the ID manually.

SERVERS
Settings contains the editable server catalog, account limits, simultaneous
characters per account and total character capacity. Account-specific capacity
overrides support purchased slots. Zero means unlimited/unknown in the launcher.
These are launcher checks; the game server always has the final say.
Missing or moved installations are detected before login and can be corrected.

NEWS
The news panel reads the official Funcom, Project Rubi-Ka and AO Rebirth sources.
Choose publishers under Settings > Sidebar and Save. Sources and panel visibility are remembered.
Publication dates are displayed so old announcements are not mistaken for new
events. Source websites can change or be temporarily unavailable; cached news
remains readable. Refresh requests current articles. Clicking a headline opens
its original source in your default browser. The feed is not a server-status monitor.

LANGUAGE AND DATA
Danish and English: automatic from Windows, with an override in Settings.
Each account has a Remember password checkbox in Add/Edit account and its password prompt.
New accounts default to asking each time. Existing accounts retain their remembered-password setting.
When unchecked, enter the password once per account for each login or character-creation action.
The same entry is used for all selected chars on that account, without saving it for later use.
Uncheck Remember password and save the account to remove its saved password.
Use Settings > Accounts > Forget passwords to clear saved passwords for all AO Nexus accounts.
To replace one saved password, select its account row and choose Update saved
password in the Details panel. Enter the existing game-account password once and choose Save password.
This updates only the encrypted copy in AO Nexus, without logging in or changing
the password on the game server. Saving enables Remember password for that account.
Other accounts are unaffected; cancelling leaves the saved password unchanged.
AO Nexus data: %LocalAppData%\AONexus
Original AO Launcher data: %LocalAppData%\AOQuickLauncher (left intact).
Login-session coordination is shared with the original launcher so concurrent
use of both apps still observes server limits.

SOURCE AND BUILD
Source is in src\Launcher, src\Common and src\Backend in the AO-Nexus project.
Build with PowerShell: .\build\Build.ps1 -RebuildBackend -Package
Requires Visual Studio desktop C++/.NET Framework tools and the .NET 10 SDK.
GUI debugging symbols remain in the source build output and are not needed to play.

This is an independent community launcher, not an official Funcom product.
See CREDITS.txt and Licenses for upstream attribution and dependency notices.

TEAM PRESETS
Use Update to save the checked characters to the selected team. It is disabled
when the saved selection already matches. Edit changes the name, shortcuts and startup default.
Save new creates a separate team.

ACTIVITY LOG
Click the log preview to open a larger read-only window with live updates and text search.
Ctrl+F focuses search. Clear search restores all lines. Disable Follow new entries
to read older lines. The current session log uses the size limit in Settings > Updates (5 MB by default).

GITHUB UPDATES
Settings > Updates > Check for updates checks stable releases from RazRn/AO-Nexus on GitHub.
First launch asks whether to enable startup checks and remembers your answer. You can change it in Settings.
Install and restart downloads, verifies and replaces the EXE after Nexus exits.
Your profile and game windows stay intact. Finish launcher activity first.
The installation folder must be writable; no administrator access is requested.
A previous EXE backup stays beside the installed file. Staging and result logs
are in %LOCALAPPDATA%\AONexus\Updates. No GitHub login or token is needed.
Versions before 1.0.18 require one manual update before this feature is available.

WINDOW CONFIRMATIONS
Minimize and Quit each ask first, with a separate Don't show this again checkbox.
Cancel leaves Nexus running. Re-enable either confirmation in Settings.
Windows startup and confirmed update restarts skip these extra prompts.

UPDATE HISTORY
Checks run once at startup when enabled, or manually in Settings. No polling.
Update activity is logged and the new version reports a successful installation.
Persistent history: %LOCALAPPDATA%\AONexus\Updates\update-history.log
A What's new window appears once after a successful update, with bundled release
notes in your selected language. It works offline and does not appear on normal starts.

SETTINGS, LOGS AND PRIVACY (v1.0.21)
Settings has General, Accounts, Sidebar and Updates tabs. Manage login servers
and Forget passwords are under Accounts. News and health are optional sidebar
tabs; both can be hidden. Health checks only the displayed server every 60 seconds
while visible. Reachable is TCP connectivity, not proof that game login works.
Unknown means missing/stale/unverified information. News refreshes selected
sources every 15 minutes while visible. Requests pause when hidden/in the tray.

Settings > Updates has a log-size limit: 5 MB per log by default, adjustable
1-100 MB. Oldest entries are trimmed. Activity is session-only; update history
is saved locally. Completed update stages are cleaned after restart; abandoned
stages are retried after 24 hours. Locked files are retried on a later start.
One previous EXE is retained for recovery. Cached runtimes are kept because
open game clients can still use them. Profiles and game files are preserved.

Settings > General > Privacy & security explains password encryption, local files,
network connections and update checks offline. Nexus has no analytics or automatic
log/crash upload. Remote services still see your IP and normal request metadata.
The full public documentation is at:
https://github.com/RazRn/AO-Nexus/blob/main/docs/Privacy-and-Security.md
STARTUP TEAM AND HEALTH ENDPOINTS (v1.0.21)
Create/Edit team has Use as startup default. Only one default is selected at
startup; without one, no preset is selected and manual checkboxes are preserved.
A default never logs anyone in automatically.

Settings > Accounts > Manage health checks edits each connection host, port
and enabled state. These are monitoring settings, separate from game login.
Save the table and Settings. Restore selected server defaults resets its checks.
Unverified zone/chat endpoints start disabled; public report sources stay fixed.
The health panel has no manual Refresh or footer guide. One enabled server
uses a plain name. Updates wait while dropdowns are open.

GAME GUI (v1.1.2)
Choose Game GUI at the top of Nexus. Layouts captures a saved character setup,
copies directly from another char, or applies a template to selected chars.
Window layout, hotbars, key bindings and pet names are independent choices.
Roll out to all reviews the affected targets. Use for new chars queues a default
until their saved preferences exist and the clients are offline.
Skins manages the installation-wide skin: Default AO, complete imported ZIPs,
or seven author-sourced Notum Dovvetech/Darktech themes. Choose the correct
Classic or New engine variant. Downloading does not activate a skin.
Online targets require a confirmed /quit, actual exit, then file changes and
relogin. Unknown clients must be closed manually. No forced shutdown is used.
Backups can restore changed settings; failed batches attempt reverse rollback.
The latest 10 completed layout backups per char and 10 successful skin-selection
backups per installation are retained. Unresolved recovery records are kept.
Test on one character/installation first; appearance and compatibility vary.
Full guide: https://github.com/RazRn/AO-Nexus/blob/main/docs/Game-GUI.md
BACKGROUND HEALTH (v1.1.2)
Settings > Sidebar offers background checks and tray outage/recovery notifications.
Notifications enable background monitoring. Nexus must remain open, including in
the tray. Two failed checks confirm an issue (usually 1-2 minutes); one successful
check confirms recovery. Windows controls popup duration; the tray retains status.
Unknown/stale reports do not claim recovery. Switch off background checks and hide
the health panel to stop monitoring. No credentials are sent by health probes.

EXPERIMENTAL GAME GUI AND RESET (v1.1.2)
The front-page button is marked Game GUI - Experimental. Try one character first.
Layouts always includes AO default layout: select it, choose characters, then Apply layout.
This uses that installation's defaults and backs up changes. Actions, bar count,
bindings, pet names, chat subscriptions and the current skin are kept.
Apply saved default uses your personal default template instead.
Update release notes now have readable headings, bullets and paragraph spacing.

v1.1.3 fixes stale border and dropdown fragments after resizing or maximizing Nexus.

v1.1.4: Open log > Clear log asks for confirmation before clearing the full session
activity history, including filtered-out entries. New logging continues normally.
Activity is kept in memory; the separate update-history file is not deleted.

v1.1.5: Compact border legends, roomier dialogs and a clickable log preview.
The header line shows confirmed login/logout progress and waiting activity.
Choosing No preset selected clears current checkboxes, keeping saved teams.
Logout still uses /quit; Nexus waits up to 90 seconds and never forces exit.
