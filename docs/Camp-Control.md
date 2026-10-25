# Preset quit control

The GUI never sends keyboard input, a clipboard payload, an arbitrary chat command, `WM_CLOSE`, or a process-kill request to game clients.

For a normal saved-character launch, the backend creates a random per-process token. It passes the token in the child's environment and saves it in the existing per-user active-client registry alongside account, endpoint, character ID, executable location, PID, creation time and observed character name. Passwords are not part of this record.

After login, the injected LoginHandler checks for the native exports used by the normal timed logout path. It creates three auto-reset events under `Local\AONexus.Quit.v1.<pid>.<token>`: `request`, `accepted` and `rejected`. They use the game process token's default Windows object DACL and the current session's namespace. The token is not a password or a security boundary against software already running as the same Windows user. Other users are not granted access by this code.

A 250 ms window timer handles requests on the game's owning UI thread. The callback verifies the originally launched character is still selected, rejects zoning or a missing engine, and invokes `N3Msg_StartCamping`. Only a successful return is followed by `SetTimeLogoutStatus(1)`: exit to desktop after the normal countdown, matching the successful `StartCamping` branch of `StartQuitToSystemMessage` in the inspected AO Rebirth client. Status 2 returns to login and was the incorrect choice in v1.0.14. No AOSharp dependency is loaded. `ActivateGameClosing` is never called. Repeated requests are refused for 30 seconds to avoid restarting the countdown.

The module remains loaded only if timer registration succeeds; Windows releases its events and timer when the game exits. Missing exports or unsupported clients disable quit control rather than substituting keyboard automation or a forced exit. Character-creation clients do not enable this bridge; the newly saved character must be started normally first.

The GUI reads a bounded active-client registry under its existing mutex, matches only saved references from the chosen preset and checks the live process. The confirmation contains this snapshot, not a query that can later acquire additional clients. Immediately before signalling, it rechecks PID, creation time, executable and title. It waits up to three seconds for a native acknowledgement; on timeout it clears the pending signal and does not retry. An acknowledgement proves that the client invoked the timed quit path, not that the server completed the logout.

## Tests

- `Verify-PresetHotkeys.ps1`: actual field capture, JSON round trip, preserved bindings when updating members, duplicate rejection, native Windows registration, registration while hidden in tray, unregister cleanup, localized layout and account/server/path filtering.
- `Verify-CampControl.ps1` and `CampControl.Tests.cpp`: native wrong-character/zoning/missing-engine/refused-camp guards, normal logout status, dispatch from a timer installed by a worker onto the owning UI thread, and repeat suppression.
- The same native fixture is compiled as an inert `AnarchyOnline.exe` in a fresh temporary directory. The production managed sender verifies its process identity and sends a request through the named events to the native callback. A deliberately wrong process creation time is rejected before signalling.

Tests do not inject into or log out real game clients. The actual server-side countdown and refusal behavior still need a player-controlled in-game check for each supported client/server version.

The event namespace changed from Camp.v1 to Quit.v1 so an older client cannot acknowledge a quit request while only returning to login. Restart existing game clients normally through v1.0.15 or later. Internal CampHotkey and CampToken storage names are retained so saved shortcuts and active-client records remain readable.

The status mapping was checked against the installed AO Rebirth `GUI.dll` (SHA-256 `e485384721e2fe13972e840dfb6a9fe29b1ba4eb71b42cc049e5097a570b6de1`): `StartQuitToSystemMessage` stores status 1 after successful `N3Msg_StartCamping`; `StartQuitToLoginMessage` uses 2. AOSharp's `Game.Quit()` also uses status 1, but calls the immediate closing entry point, which Nexus does not use. This is static verification of the inspected client, not an end-to-end live logout test across every server.
