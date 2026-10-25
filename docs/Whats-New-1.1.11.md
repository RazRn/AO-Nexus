# AO Nexus v1.1.11

- **Scripts in Game GUI.** The new **Scripts** tab lists the chat scripts (`/name`) of each game installation with a preview. Copy selected scripts to another installation or to all of them, import script files from your PC, delete scripts or open the scripts folder. Scripts work right away, also in running clients, so no relog is needed. Only plain text files are handled; program files, binary files and linked folders are refused.
- **Macros per character.** The Scripts tab shows the macros of a chosen character, and **Copy macros to chars…** copies them to other characters. Under **Layouts**, templates and **Copy from char…** have a new **Macros** component. Hotbars refer to macros by number, so Macros is ticked automatically together with Hotbars and macro buttons keep running the same macros.
- Every script and macro change is backed up first and can be restored under **Backups**. Restoring also backs up the current state, so it can be undone.
- Macros and scripts can contain character names. They stay on your PC and are never included in diagnostics or problem reports.

See the [Game GUI guide](https://github.com/RazRn/AO-Nexus/blob/main/docs/Game-GUI.md#scripts-and-macros) for details.

Includes everything from [v1.1.10](https://github.com/RazRn/AO-Nexus/releases/tag/v1.1.10): faster team logins and login progress per character.
