# AO Nexus 1.3.0 Beta 1

Beta 1 identifies the current development build. This source update does not publish a binary release; redistribution permission and runtime-release checks remain outstanding.

- **Control Panel:** sortable combat statistics, individual pets grouped beneath their owners, compact character cards, health and nano bars, attack targets, observed aggro, zones and client response/memory status.
- **Stable cards:** selecting or restoring a character keeps card sizes and action buttons aligned. Previews preserve their proportions inside a fixed frame.
- **Your layout:** reorder and collapse Team, Damage Dumper and Timers; reorder character cards. Choices persist between sessions.
- **Team startup:** saved character order and an option to keep the first character focused while followers start in the background.
- **Logout and relog:** active formation movement stops before a confirmed logout. Automatic regroup pauses during the operation, without changing your saved preferences.
- **Plugins:** shared selections for all professions, per-character plugin sets, startup diagnostics and compatibility with a user-selected local AO# installation.
- **Combat scripts:** export Team, All and NPC reports to in-game scripts. Nearby reports require the experimental setting and a current observer.
- **Experiments:** automatic invitations/leader recovery, formations, movement refresh and AO-VK management are explicitly opt-in. See [Experimental features](../Experiments.md).
- **Optional auto regroup:** separated followers can try returning to the team leader; a blocked follower stops and attempts one Team location link. Enable it in Control Panel → Team and keep the panel open.
- **Settings:** clearer categories and a Use recommended settings button. Review the staged choices, then Save to apply; graphics and experimental choices are preserved.
- **Maintenance:** clearer diagnostics, resource-based English/Danish text, isolated core logic and automated unit tests.

## Compatibility and limitations

Use your own compatible AO# installation. Nexus does not distribute AO# or third-party plugin bundles. Already-loaded helpers need a client relog after updates; restarting Nexus alone cannot replace them.

The old AO-VK 0.7.1 download is retired. This Beta preserves an already-installed renderer but does not offer an automatic replacement download. AO-VK 0.8.1 has been tested locally; that is not a guarantee for every graphics card or future Defender definitions.

If a client shows a white scene after returning to ICC while its interface remains visible, try disabling **Offscreen surface technology** in that client's Rendering settings. This resolved the reported Wazoo case. The underlying renderer fault has not been isolated.

Aggro counts are nearby mobs observed targeting the character, not a server threat table. Service graphs measure connection response, not backend processing. Minimized clients may stop rendering; their previews can freeze while telemetry continues.

Formations stop on zoning. Wait for fresh team state and start them again. Unknown client binaries or incomplete rosters leave automation unavailable instead of guessing membership.

The Engineer startup casting issue remains open: “You must be standing up to execute a nano program” can repeat while the character looks standing. The current detector did not record this message during the reproduced case, so recovery is not claimed fixed.

## Updating

Keep a backup of your Nexus settings before replacing the application. Retain your local AO# folder and game installations. New experimental settings default off when upgrading older profiles; existing opt-ins and Control Panel choices remain saved. No Defender exclusions or disabled protection are required or recommended.
