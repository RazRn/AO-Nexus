# AO Nexus v1.0.14 — What's new

New since the previous public release, v1.0.11:

- **Minimize to tray:** keep Nexus running without taking up space in the taskbar or Alt+Tab. Click its tray icon to return; the tray menu also offers Open and Exit.
- **Optional Windows startup:** start Nexus in the tray when you sign in. This never logs game accounts in automatically.
- **Single instance:** opening Nexus again brings the existing window forward, even if it is hidden in the tray or you launch another copy from a different folder.
- **Two shortcuts per preset:** choose separate global shortcuts to start a saved team and log its clients out. Click either field and press your preferred combination.
- **Automatic shortcut saving:** each preset remembers its own bindings. Press Delete or Backspace while recording to clear a field, or Esc to cancel.
- **Conflict detection:** duplicate preset shortcuts and combinations already registered by another app are reported.
- **Start from anywhere:** a start shortcut selects the saved team and shows the normal login confirmation. Existing account, server and password checks still apply.
- **Normal `/camp` logout:** the logout shortcut shows the exact matched clients for confirmation and requests the game's normal timed logout. No forced close or instant quit.
- **Client identity checks:** logout checks the account, server, character, game folder, process ID, process start time and window title, then verifies again before sending the request.
- **English and Danish:** the new controls, help text and confirmations follow the selected language.

Shortcuts are unassigned by default and require Nexus to be running. Windows already open from an older release need to be restarted normally through the updated launcher before shortcut logout is available. AOSharp is not required. Combat, movement or cancellation can prevent camp from completing.

Login behavior remains unchanged: independent accounts may start in parallel, while multiple characters on the same account and server log in serially.

## Verification

Tested with synthetic profiles and inert clients: shortcut capture and persistence, Windows hotkey registration and release, tray behavior, conflict handling, client identity rejection, native camp dispatch on the owning UI thread, acknowledgement and repeat suppression. Existing launcher, news, password and account-limit regressions were also run. No live player characters were logged out during verification.

Based on AOQuickLauncher by never-knows-best and contributors. Customized by Luckyspawn.
