# AO Nexus v1.0.15

- Preset logout shortcuts now request `/quit`: log out through the game's normal countdown, then close the client instead of returning to character selection.
- **YOUR SQUADS** has its own bordered panel, containing the preset picker, save/update/rename/delete buttons and both shortcut fields.
- Section actions share a row with their heading: Expand/Collapse beside Accounts by server, and Refresh beside Transmissions. This removes the spare row above the account list.
- The preset dropdown matches the adjacent buttons in height and top alignment.
- Existing saved shortcuts are preserved. Confirmation, character/process checks and repeat protection still apply.

Restart existing game clients normally through v1.0.15 to enable the new quit bridge. Earlier clients are identified as needing a restart; Nexus does not silently send the old camp command. Combat, movement or cancellation can interrupt logout. There is no forced termination.

The experimental same-account parallel-login feature is not included.
