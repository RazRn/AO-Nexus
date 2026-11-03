# Plugins and server rules — 1.3.0 Beta

This describes the Beta source build. Check Releases for available downloads; this source update does not publish an AO# or plugin bundle.

## Main window tabs

The Game GUI and Plugins header buttons are replaced by tabs under the header:
**Characters · Plugins · Timers | Layouts · Skins · Scripts / Macros · Backups | Control Panel ↗**. Each opens as a page in the main
window; the log and *Log in selected* stay visible on every tab.

- Each character row has a plugin-set button at the right: click it to pick the set in place.
- **Plugins** is one page: a top strip (auto-load, AO# source, Import, Add DLL), then three
  columns: plugin sets · the selected set's plugins (grouped, with search, and who uses it) ·
  characters & teams (defaults, team overrides, the team switch) above *Running now*.
- **Layouts, Skins, Scripts, Backups** show their numbered steps side by side, with the apply
  button at the end of the last column.
- Beside *Log in selected*: **Auto-load plugins in game** (the same setting as on the Plugins tab).
- In the team bar: **Plugins for this team**. Off = characters logged in with that team get no
  plugins (for example a crafting team). New and existing teams start with it on.
- Leaving the Plugins tab with unsaved changes asks whether to save them. Game GUI tabs are
  unavailable while a login runs, and stay open while a GUI change is in progress.

## Using your own AO# installation

Open the **Plugins** tab.

- **Plugin sets**: a named list of plugins, for example *Doctor* = Doc handler + Manager · Sync.
  The first time the window opens, Nexus creates one starter set per profession.
- **ALL** is pinned above the professions. Select shared plugins here once; every enabled set
  includes them, with inherited selections marked **· ALL**. Selecting a plugin moves existing
  per-set copies into ALL; removing it here removes it from all sets. Profession-specific
  handlers stay in their own sets. Save changes, then use **Load now** or the next login to apply
  them. Existing running plugins are not restarted just by saving. No plugins / disabled teams
  remain off, and incompatible Z/AOSP combinations are still skipped and reported.
- **Missing** means the saved plugin ID is absent from the selected installation. For example,
  an old `Z.Handler.Adv` selection is not the installed AOSP `Adv handler`. Nexus preserves
  these saved choices until you explicitly remove or replace them; it does not swap suites.
- **Characters & teams**: choose each character's default set. Choose a team preset at the top
  to give characters a different set only when that team logs in.
- **Running clients**: characters Nexus logged in that are in the game now, with their plugin
  state. *Load plugins now* and *Unload plugins* act immediately.
- **AO# & loading**: turn on automatic loading, choose another AO# folder, import your
  AO# `config.json` (each character profile becomes that character's default set), or add a
  plugin DLL of your own.

With automatic loading on, Nexus waits until the game window shows the character, then loads its
set. If monitoring is on, Nexus Health Monitor is added to every set automatically.

### How it works

- `NexusPluginHost.exe` (src/PluginHost) does what AOSharp.exe's Inject button does: EasyHook
  injects `AOSharp.Bootstrap.dll`, connects to the bootstrapper's pipe (named after the game's
  process ID, like AOSharp.exe) and sends the plugin list. Closing that pipe unloads the plugins,
  so the host keeps running in the background while plugins are loaded. Restarting or updating
  Nexus does not unload them. The host stops by itself two minutes after nothing is loaded.
- A client that AOSharp.exe already injected is detected (its pipe exists) and never injected a
  second time.
- The AO# runtime and plugins come from an *AO# pack*: either built into the EXE (local
  builds made with `Build.ps1 -AOSharpPack <folder>`) or a folder laid out like `AO#\dist`.
  The built-in pack is extracted and verified under `%LocalAppData%\AONexus\AOSharp\<checksum>`.
- Log: `%LocalAppData%\AONexus\PluginHost\host.log`.

### Licensing

AO# (never-knows-best and contributors) and the bundled plugins (aosharp.utils, aosp-bots)
belong to their authors and have no license file granting redistribution. Builds made with
`-AOSharpPack` are for local use. `Build.ps1 -Package` refuses to package them.

## Server rules

Login servers, IP addresses, ports, accounts and characters allowed online, character slots and
health-check ports live in `config/servers.json` instead of in code.

- A copy is built into the EXE as the offline fallback.
- **Settings → Accounts → Update server rules and IP/port** fetches the list now. Nexus also
  checks when it starts (can be turned off on the same page).
- Source, in order: the *ServerRulesSource* setting if set; `servers.json` next to AONexus.exe;
  `..\config\servers.json` when Nexus runs from the repository's `dist\`; then GitHub
  (`config/servers.json` on main).
- Only a higher `revision` is applied automatically. Servers you edited yourself are left alone,
  and servers you removed are not added back.
- Address changes are always shown first. Accounts on a moved server ask for their password
  again, because saved passwords are tied to the server address.
- The status-page URLs for Rebirth and PRK stay in code, because each has its own parser.
- The login engine (NexusEngine) still has its own built-in fallback limits; the launcher always
  sends it the limits from the list.

To change a server: edit `config/servers.json`, raise `revision`, and press the update button.

## Another AO# in a Nexus client

Every few seconds Nexus checks each game client it logged in for AO# it did not put there:

- **Modules:** `EasyHook32.dll`, `AOSharp.Bootstrap.dll` or `AOSharp.Core.dll` loaded from a folder outside the Nexus AO# pack.
- **Pipe:** AO#'s bootstrapper pipe (named after the process ID) exists while the Nexus plugin host does not manage that client. The bootstrapper closes this pipe when its controller disconnects, so the check stays quiet after Nexus unloads its own plugins.

If Nexus has not loaded plugins there, the client is marked *Controlled by another AO#* and auto-load skips it (yellow in the log). If Nexus plugins are already loaded, it is marked *Two AO#s attached!* (red), because the game is then hooked twice. If AOSharp.exe is running while Nexus has plugins loaded, the log gets one warning per AOSharp.exe process.

Limitation: if Settings → AO# folder points at the same folder AOSharp.exe runs from and Nexus has already loaded plugins, a second attach from that folder loads the same files and cannot be told apart.

## Game windows left by an earlier login

Before each login, Nexus looks at clients the login engine still records as *logging in* for the selected characters:

- **Abandoned** (older than the engine's ready timeout plus 30 s, or no login engine running): the window never reached the game. Nexus closes it (normally, then by force) and logs the character in.
- **In use** (abandoned, but it is the foreground window): Nexus asks before closing it.
- **In progress** (another login may still be starting it): left alone and logged.

## Diagnostic ZIP: app state

The client-report ZIP also contains `app-state.txt` (ticked characters, team, plugin sets, switches, servers, known clients and plugin status) and `settings-snapshot.json` (all settings, credential-like fields removed). Both pass through the same redaction as the activity log.

## Saved team order and automatic invitations

Team preset → Edit contains a character list with Move up / Move down. Enable
“Use this login order” to start clients in that order; the first character is
the leader when “Automatically form team” is enabled. Updating the preset's
selection preserves the order of retained members and appends new members.
Team Window card order remains independent. Existing presets default to off.

Automatic formation applies to a full saved-team launch (including a full
relog with the preset selected). AO#, automatic plugin loading and In-game
buttons must be enabled. Nexus bundles its own Shortcuts helper separately
from the user's AO# installation. Teams are limited to 2–6 characters on
one login server. The helper arms followers first and invites only acknowledged,
nearby members of that preset as they become ready. It uses targeted AO#
invitations, not SyncManager's `/form`, which toggles to disband when in a team.
Existing unrelated teams are left alone. No leader transfer or disband occurs.

An invitation is retried at most three times per character, at least ten seconds
apart while pending; joining allows the next ready member immediately. Startup
expires after five minutes. Completion requires observing membership, not just
sending an invite. A completed plan does not re-form a team the user leaves.
Disabling the option or stopping login cancels the active startup plan on the
next plugin poll. Plans are bound to process start time and character identity;
they are not reconstructed just because Nexus restarts. Activity log records
waiting, invitations, joins, conflicts and completion.

Ordered starts keep the verified fast-start admission pipeline where supported;
they control start/admission order, not an absolute promise of world-ready order.
Unsupported clients wait for normal readiness. An ordered-start failure stops
later starts in that batch. Partial manual selections keep normal launch behavior.

Validation: Verify-AutoTeam.ps1 (21 state-machine checks), Verify-TeamWindow.ps1
(250 UI/data checks including preset ordering and helper acknowledgement), and
Verify-TrackedAccountLimits.ps1 (117 scheduler/session checks). Live six-client
auto-team formation was subsequently verified from the packaged launcher on 1 November at 14:11 local: all five followers joined Overwhelming automatically; Team Window showed the leader and all six with normal response time. Plugin readiness/team formation completed after game world-ready (about 30 seconds after clicking login).

## Log colours

The activity log (the footer and the log window) colours lines by their wording: green when something worked, yellow when something was skipped or needs a look, red when something failed.

## Plugin window files

Plugins draw their in-game windows from `.xml` files next to them (for example
`AOSP\UI\HandlerMainWindow\HandlerMainWindow.xml` for `/handler`). If a plugin opens a window whose
file is missing, the game client crashes. The pack builders keep every `.xml` except library
documentation (an `.xml` next to a `.dll` with the same name), and the build stops with a list
if any plugin DLL names a window file that is not in the pack.
