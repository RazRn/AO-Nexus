# AO Nexus 1.3.1

You can now enable **Automatically relog crashed clients** in **Settings → Login**.
Nexus restarts a recognized crashed character using its saved password and normal launch queue.
The option starts off and requires Nexus to remain open.

- First restart after 10 seconds; second after 30 seconds, with at most two attempts per character in ten minutes during the current Nexus session.
- Already-running replacements, including pending logins and zoning clients, prevent duplicate launches.
- Normal logout, intentional closes and unknown exits remain closed. Frozen clients still need manual recovery.
- Existing automatic team recovery can reinvite the returning character and restore former leadership when enabled and available.

Only Nexus needs restarting for this update; currently running game clients do not need to relog.
Your existing AO# folder, plugin sets and other settings are retained by this update.

See [automatic crash relog](../Player-Guide.md#automatic-crash-relog) for requirements and cancellation behavior.
