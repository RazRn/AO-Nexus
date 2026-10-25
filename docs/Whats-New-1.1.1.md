# AO Nexus v1.1.1 — Background health and GUI capture clarity

- Optional background health checks monitor all selected servers while Nexus runs, including in the tray or on another tab. Settings → Sidebar controls background checks and tray notifications separately; notifications enable background checks.
- Two failed observations confirm an issue. One notification marks the incident, repeated failures stay quiet, and a positive observation sends a recovery notice. Unknown or stale reports never claim recovery. The tray retains unresolved issues and the activity log records transitions. Windows controls popup duration and may suppress notifications.
- Game GUI headings and help remain readable during operations. Interactive controls are locked individually; the operation footer stays compact.
- Capture/copy confirmations explicitly identify the source character. Running sources must `/quit` and fully exit before preferences are read; Nexus relogs them afterward. For closed sources, the confirmation asks whether their latest changes were saved by a normal logout.
- Character-layout operations match the selected character even when installation discovery has no corroborating log. Other characters on the same account are no longer included merely because that mapping is unavailable. Shared skins retain installation-wide checks; unknown clients still block unsafe writes.
- Five optional community-inspired starter layouts are available for local testing: Compact Solo, Team Healer, Pet Commander, Expedition Map and Clean Combat. See [starter layouts](Starter-GUI-Layouts.md) for sources and resolution limitations.

This local test build also contains the [Game GUI manager introduced in v1.1.0](Whats-New-1.1.0.md). It has not yet been published as a GitHub release. Experimental same-account parallel login remains excluded.

Validation uses synthetic health transitions, background scheduling, tray lifecycle, source-confirmation and GUI rendering tests. Real game behaviour and Windows notification presentation still depend on the client and operating-system settings.
