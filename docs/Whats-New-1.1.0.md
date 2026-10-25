# AO Nexus v1.1.0 — Game GUI manager

- **Game GUI** brings character layouts, installation skins and recovery backups together in three tabs.
- Capture a character's GUI as a reusable template, copy directly from another character, or apply a template to selected characters or all configured characters. Window layout, hotbars, keybindings and pet names are separate choices.
- Set a default layout for new characters. Nexus waits for the game to create their real preference folders and applies queued defaults before a later offline launch. Existing characters can receive the default explicitly.
- Download seven **Notum Dovvetech/Darktech** themes from the author's published sources, with classic-client or new-engine packages. Import complete skin ZIPs, copy installed skins between installations, or return to the default AO skin. Downloaded skin archives are not bundled in the release.
- Online targets receive a relog confirmation. Nexus requests `/quit`, waits for clients to exit, applies the change and restarts the identified clients through the normal login flow. There is no forced termination; unrecognized clients require manual closure.
- Changes create recovery backups. Skin restore changes only the skin selection, preserving unrelated preference changes. Multi-target failures trigger a reverse-order rollback attempt and report recovery failures.
- Storage is bounded: the latest 10 completed layout backups per character and 10 successful skin-selection backups per preference installation are retained. Unresolved recovery records are preserved. Identical skin imports reuse verified contents, and temporary downloads and staging files are cleaned up.
- English and Danish controls follow the existing Nexus design. The [Game GUI guide](Game-GUI.md) explains scopes, workflows, skin attribution and compatibility limits.

## Validation and limits

Checked with **58 layout**, **87 UI**, **18 session-safety** and **48 skin** checks, using temporary preferences and synthetic clients. Skin checks include the reviewed author archives, import rejection cases, backup integrity, retention and installation isolation.

Live-game relogging and the resulting in-game appearance have **not** been verified. Hotbar references and layouts can depend on the character, profession, resolution and client version. Classic and new-engine skin packages differ; verify one installation before a wider rollout. The author's compatibility statement does not establish support for every current server's client build.

Experimental same-account parallel login is not included.
