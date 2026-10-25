# AO Nexus v1.1.10

- **Faster team logins.** A character is confirmed in the world after three identical window-title checks 150 ms apart instead of 500 ms apart, and the next character on the same account starts 0.25 s later instead of 1 s. In the v1.1.9 measurements confirmation took about 1.0 s and the fixed pause another 1.0 s per character; together this saves about 1.3 s per character.
- **Same-account characters still start one at a time.** Starting the next character as soon as the previous one was selected was tested with a 6-character team on Funcom Rubi-Ka: every earlier character was disconnected (exit code 0) while still loading into the world once the next one logged in. Nexus therefore keeps waiting until each character is in the world.
- Settings labels for the login options are shorter, so the Accounts tab fits at 150% scaling.

Includes everything from [v1.1.9](https://github.com/RazRn/AO-Nexus/releases/tag/v1.1.9): login progress per character, spaced client starts and skipping the login screens by default.
