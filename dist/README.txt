AO NEXUS 1.0.11
Based on AOQuickLauncher by never-knows-best and contributors.
Customized by Luckyspawn.

START
Double-click AONexus.exe. Keep this folder anywhere you prefer.
The installed version appears at the lower-right corner of the app header.
The login engine is compressed inside the EXE; no second app folder is needed.
Windows 10/11 with .NET Framework 4.8 is required. The game is installed separately.

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
Use the source picker to check any combination of servers. Your choices save
automatically and are restored when AO Nexus starts, like other settings.
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
Use Settings > Forget passwords to clear saved passwords for all AO Nexus accounts.
To replace one saved password, select its account row and choose Update saved
password in the Details panel. Enter the new game password twice and Save password.
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
