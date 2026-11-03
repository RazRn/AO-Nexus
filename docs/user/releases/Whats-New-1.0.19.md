# AO Nexus v1.0.19

- Added separate confirmations for minimizing to the system tray and quitting Nexus. Each includes **Don't show this again**, and both can be re-enabled in Settings. Cancelling leaves the launcher running as before.
- Windows startup can still open directly in the tray. An update restart uses its existing installation confirmation, without prompting a second time to quit.
- Removed hover tooltips throughout the launcher, including native clipped-label hints in the account tree and server-settings grid. Information remains available in Details and the relevant dialogs.
- Added update-check and installation messages to the activity log, a persistent update history, and a success message after an updated version restarts.
- A one-time **What's new** window shows the installed version's release summary after an update. The notes are bundled in English and Danish and work offline; ordinary startups do not show this window.
- Team presets have a dedicated **Update** button for saving the current character selection. **Edit** now changes only the name and shortcuts. Removed the explanatory line beneath the preset controls.
- Refreshed the guide screenshots.

Automatic checks still run once at startup when enabled. Manual checks remain available in Settings. Updates are never installed silently, and existing game windows stay open.

To try the update path from v1.0.18, open **Settings → Check for updates…**, review this release, then choose **Install and restart**. Accounts, saved passwords and team presets remain in the existing Windows profile.

Verified with isolated updater/package fixtures, confirmation and tray lifecycle tests, preset save/edit tests, English/Danish layout checks and regenerated screenshots. The public build keeps same-account logins serial; experimental parallel-login changes are not included.
