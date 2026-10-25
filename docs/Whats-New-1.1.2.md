# AO Nexus v1.1.2 — Experimental Game GUI and background health

- **Game GUI · Experimental:** capture or copy character layouts with separate choices for window layout, hotbar actions, key bindings and pet names. Save templates, choose a default for new characters, apply to selected targets or review a rollout to all.
- **AO default layout:** a built-in reset reads each target installation's shipped defaults and clears supported position overrides. It preserves hotbar actions/count, bindings, pet names, chat subscriptions and the current skin. **Apply saved default** applies your personal template instead.
- **Skins:** separate controls for the installation-wide skin, including default AO, imported archives and reviewed Notum Dovvetech downloads from the author. Choose the correct classic/new-engine variant.
- **Safe ordering:** source capture and target changes request `/quit`, wait for actual exit, then read/write preferences and relog. Character-only changes no longer include unrelated characters merely because installation discovery lacks evidence. Unknown clients still block unsafe writes. Changes have backups; failed batches attempt rollback.
- **Starter layouts:** the source includes a generator and guide for five original community-inspired, layout-only examples. No third-party player preferences or skins are bundled.
- **Background health:** Settings → Sidebar offers background checks and optional outage/recovery tray notifications. Two failures confirm an issue; a success confirms recovery. Repeated failures stay quiet, and the tray retains unresolved issues. Windows controls popup duration.
- **Readable updates:** release notes have a larger reading area, highlighted headings, separate bullets and preserved paragraphs. Remote notes are rendered as text, not HTML or supplied RTF.
- **Presentation:** GUI headings remain readable during operations. The guide and all screenshots have been refreshed.

Game GUI is experimental: try one character first, inspect it in-game and use Backups if needed. Skins and window sizes differ between clients. Synthetic tests cover reset/preservation, backup/restore, failed writes, relog identity, health transitions and update handling. Starter layouts have 856 checks across four canvas sizes; that does not replace in-game testing.

Existing accounts, passwords and team presets are preserved. Experimental same-account parallel login is excluded.

See [Game GUI](Game-GUI.md), [starter layouts](Starter-GUI-Layouts.md), [server health](Server-Health.md) and [privacy/security](Privacy-and-Security.md).
