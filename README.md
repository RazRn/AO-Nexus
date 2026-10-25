# AO Nexus

**A Windows launcher for your Anarchy Online accounts, characters and teams.**

Keep separate game folders and login servers for Funcom AO, Project Rubi-Ka and AO Rebirth, then launch a saved team from one place.

**[Download the latest release](https://github.com/RazRn/AO-Nexus/releases/latest)** · [Player guide](docs/Player-Guide.md) · [Release notes](https://github.com/RazRn/AO-Nexus/releases)

![AO Nexus with a team preset, accounts grouped by server, details and news](docs/images/published/launcher-overview-0e15415286481c82.png)

*Example accounts and offline news; no personal accounts or passwords. [Screenshot provenance](docs/images/SCREENSHOTS.md).*

## Quick start

1. Download **AO-Nexus-Luckyspawn.zip** from the latest release's **Assets**. The automatically generated “Source code” ZIP is for developers.
2. Extract the whole ZIP into a writable folder, such as `%LOCALAPPDATA%\Programs\AO Nexus`, and run **AONexus.exe**.
3. Choose **Add account**. Enter your game username; under **Game and server**, select its server and the game folder containing `AnarchyOnline.exe`.
4. Import existing characters with **Find Prefs folders…** and **Find characters**, or save the account and use **Add character** to enter their IDs manually.
5. Check the characters you want and choose **Log in selected**. Review the confirmation and enter passwords when asked.

Requires Windows 10/11, **.NET Framework 4.8 or 4.8.1**, and an installed game client prepared for the chosen server. See [installation and download verification](docs/Player-Guide.md#install-and-first-launch) for folder, runtime and unsigned-app guidance.

## What you can do

| Feature | How it helps | Guide |
| --- | --- | --- |
| Accounts and team presets | Group characters by server; save teams across accounts; choose an optional startup default. | [Accounts](docs/Player-Guide.md#accounts-and-characters) · [Team presets](docs/Player-Guide.md#team-presets) |
| Launch and logout | Start independent accounts in parallel, with serial logins within each account. Assign separate team shortcuts for login and normal `/quit`. | [Launch, stop and logout](docs/Player-Guide.md#launch-stop-and-logout) |
| Password choices | Remember a password per account, encrypted for your Windows user, or enter it each time. Update or remove saved copies. | [Passwords](docs/Player-Guide.md#passwords) |
| News and server health | Choose news sources and connection checks; optionally monitor in the background and receive tray notifications. | [News and health](docs/Player-Guide.md#server-news-and-health) |
| Tray, updates and logs | Keep Nexus out of Alt+Tab, opt into update checks, and search the activity log. Update installation always requires confirmation. | [Everyday settings](docs/Player-Guide.md#settings-tray-and-updates) |
| Game GUI · Experimental | Capture selected layout components, apply them to characters, manage skins and restore backups. | [Layouts, skins and recovery](docs/Game-GUI.md) |

The interface supports English and Danish. It initially follows Windows' language, with a manual override in Settings.

## Scope and privacy

Nexus launches supported Windows AO clients. It does not install or patch the game, include AOSharp, or automatically support replacement clients with a different login protocol. The server controls which logins are allowed; configurable launcher limits help prevent invalid selections. Same-account parallel login is not included in the public build.

Accounts, settings, saved passwords and GUI templates stay on your PC. The app connects to your chosen game servers, enabled news/health sources, GitHub for requested update checks/downloads, and skin sources when you request a download. It has no analytics or cloud account sync. Read [Privacy and security](docs/Privacy-and-Security.md) for storage, connections, retention and update verification.

## Need help?

| Problem | Start here |
| --- | --- |
| No characters appear after adding an account | [Find Prefs or add an ID manually](docs/Player-Guide.md#accounts-and-characters). |
| A team does not retain checkbox changes | Select the preset and choose **Update** after changing its members. [Preset behavior](docs/Player-Guide.md#team-presets). |
| A saved game password changed | Select the account and choose **Update saved password** in Details. [Password options](docs/Player-Guide.md#passwords). |
| A game will not launch, update or close as expected | [Troubleshooting](docs/Player-Guide.md#troubleshooting). |

For a bug report, include the Nexus version, server/client type and steps to reproduce it. Remove passwords, usernames and personal paths from shared logs or screenshots.

## More documentation

- [Player guide](docs/Player-Guide.md): setup and everyday use, with focused red callouts.
- [Game GUI](docs/Game-GUI.md): experimental character templates, installation skins and backups.
- [Server health](docs/Server-Health.md): endpoints, timing and what check results mean.
- [Privacy and security](docs/Privacy-and-Security.md): local data and network behavior.
- [Build and test](docs/Building.md): contributor requirements, commands and source map.
- [Offline quick guide](dist/README.txt): the text guide included in future download packages.

## Credits and licensing

Based on [AOQuickLauncher](https://gitlab.com/never-knows-best/aoquicklauncher) by **never-knows-best and contributors**. Enhanced by **Luckyspawn**. See [credits](dist/CREDITS.txt) and [artwork provenance](assets/ARTWORK.txt).

**Upstream licensing remains unresolved.** Public source and download availability are not grants of redistribution rights. See [LICENSE-NOTICE.txt](LICENSE-NOTICE.txt) before redistributing or publishing derived work. Dependency notices remain in [dist/Licenses](dist/Licenses).

AO Nexus is an independent community project, not an official Funcom, Project Rubi-Ka or AO Rebirth product.
