# AO Nexus v1.0.21

- Choose **Use as startup default** when creating or editing a team preset. Only one team can be the default. Without a default, startup shows **No preset selected** and keeps your manual character checkboxes. Selecting a default does not log anyone in automatically.
- Server health now displays the server name without a dropdown when only one server is enabled. The manual Refresh button and explanatory footer are removed; automatic checks continue every 60 seconds while visible.
- Background health and news refreshes wait while a dropdown is open, so they no longer interrupt your selection.
- **Settings → Accounts → Manage health checks…** provides a table of login, world/zone and chat connection checks for each server. Enable or disable individual checks, edit an IP address or hostname and port, or restore the selected server's defaults. Monitoring settings are separate from account login settings.
- Funcom and Project Rubi-Ka defaults were rechecked against their own sources. Funcom uses its own chat ports; PRK's unverified zone/chat checks stay disabled. Public reports retain their verified sources and are distinguished from TCP reachability.
- English and Danish interface text, the player guide, privacy information and all guide screenshots are updated.

Validated with synthetic profiles, local socket probes, dropdown interaction tests and English/Danish layouts at normal and enlarged scale. No real game login was needed. Experimental same-account parallel login is not included.
