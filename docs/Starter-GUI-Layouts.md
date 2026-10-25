# Starter GUI layouts

These five starters are **original AO Nexus arrangements inspired by community guides and discussions**. They are not recovered player preferences, exact reproductions of screenshots, or endorsements by the linked authors. Community sources inform the design; the generated template files and schematic previews are our own.

They are intended as starting points to try, adjust in the game, and capture again under your own name. A schematic preview describes placement; it is **not a screenshot or proof of compatibility with a particular game client**.

## The five arrangements

| Starter | Purpose and adaptation | Community evidence |
| --- | --- | --- |
| **Compact Solo** | Keep the center clear and place a small set of frequently used panels near the edges. This is a restrained, general-purpose arrangement rather than a copy of a specific character's setup. | [Babaoroody's HUD customization article](https://babaoroody.wordpress.com/2009/06/27/customize-your-ao-hud-gui/) describes the author's own minimalist stock-GUI redesign and reducing unnecessary visible interface elements. |
| **Team Healer** | Give the team panel priority, with related status information nearby. The layout supports monitoring the team; it does not automate healing or assign healing actions. | [The same first-person article](https://babaoroody.wordpress.com/2009/06/27/customize-your-ao-hud-gui/) specifically describes grouping team and personal health/nano information toward the upper-right so both can be checked together. Our exact panel coordinates are new. |
| **Pet Commander** | Reserve space for pet information alongside team/status panels, leaving room for the pet window to grow. Useful to try on an MP, Engineer, or Bureaucrat. | [AO-Universe's Basic GUI Guide](https://www.ao-universe.com/guides/classic-ao/gameplay-guides-6/basic-gui-guide) explains the pet window's commands, horizontal resizing, and automatic vertical expansion with additional pets. The adjacency and spacing in our starter are our interpretation. |
| **Expedition Map** | Give navigation and mission information a predictable place while retaining a clear play area. It does not install maps, grant map upgrades, or change mission data. | [AO-Universe's guide](https://www.ao-universe.com/guides/classic-ao/gameplay-guides-6/basic-gui-guide) documents map windows, the mission window, and moving panels out of the sidebar. This is a guide-inspired arrangement, not a documented named player's layout. |
| **Clean Combat** | Reduce peripheral clutter and group combat-relevant panels for quick reading. Only supported portable settings are changed; this is not an exact reconstruction of a custom HUD. | [Athen Paladins' “Your GUI setup” discussion](https://athenpaladins.org/forums/viewtopic.php?t=342) contains first-person player setups. Posts from 5 June 2006 discuss central player/enemy health displays, narrower chat windows, and NCU at the lower-right. Those ideas inform this starter; old external screenshots are not treated as verified geometry. |

## What applying a starter changes

- These are **layout-only** templates. They do not provide someone else's hotbar actions, key bindings, pet names, credentials, or character/account identity.
- Existing hotbar frames may move. Their actions, selected pages, visibility and configured count are preserved; extra bars outside the starter's arrangement are untouched.
- Existing chat routing and channel subscriptions are preserved. Moving a supported chat window is different from importing another player's communication setup.
- A layout does not install or activate a skin. Use the **Skins** tab separately if you want a different appearance.
- The GUI manager creates backups before supported changes and offers restoration to the original target. Apply to one character first, inspect it in-game, then roll out to more characters if it suits you.
- Customizations made while the client is running can be overwritten when that client saves its preferences. Use the manager's close/relog flow and let clients exit normally before files are changed.

## Resolution and client caveats

### Generate the optional examples

From a source checkout with a built launcher, run Windows PowerShell:

```powershell
./tools/New-StarterGuiLayouts.ps1 -AssemblyPath ./dist/AONexus.exe -StoreRoot "$env:LOCALAPPDATA/AONexus/GuiLayouts" -Width 1920 -Height 1080
```

Use the game window's client size for Width/Height. This adds five templates and a local schematic gallery under `StarterPreviews/index.html`; it never applies them to characters. Existing templates with the same generated IDs are preserved. Close and reopen the Game GUI dialog to refresh the list. The generator requires .NET Framework 4.8; it can also write to an isolated temporary library for review first.

The built-in **AO default layout** is separate and is available without running this generator.

Read the generated template's reference viewport information before applying it. Coordinates and window sizes are not universal: a smaller window, different aspect ratio, font size, DPI setting, or skin can change what fits. The launcher must not claim that moving panels also scales the game's UI artwork.

Test at the reference resolution first, then at your actual game-window size. Check long team names, open inventories, map/mission panels, and the pet window with all pets present. Preserve enough space for panels that expand dynamically. Capture your adjusted version as a personal template.

The historic guides describe clients from different eras. Their design ideas do not prove compatibility with every Retail AO, Project Rubi-Ka, AO Rebirth, classic-engine, or new-engine client. Template schema checks and synthetic tests cannot replace an in-game inspection.

AO-Universe describes the attack/casting progress bars as fixed at the upper-left. These starters must not promise to move every HUD element. Its pet-window guidance also notes that a pet must be spawned before the window is first available. A missing pet panel should therefore be checked in-game with a summoned pet before treating it as a broken layout. [Guide](https://www.ao-universe.com/guides/classic-ao/gameplay-guides-6/basic-gui-guide)

## Additional research and choices we did not copy

[MojeAnarchy's 2015 beginner guide](https://www.mojeanarchy.cz/news/spusteni-hry1/) documents a concrete 1600×900 setup with three stacked hotbars at the lower-right, inventory beneath them, a smaller chat area, and a top-centered compass. It is useful context for compact edge-based layouts. We deliberately do **not** call a starter “Three-Bar” or import its action contents: the starter collection preserves the user's hotbars.

Mortus's post in the [Athen Paladins discussion](https://athenpaladins.org/forums/viewtopic.php?t=342) describes separate organization, combat, and tell views with a shared input arrangement and click-through behavior. Those communication settings are personal and outside this starter collection's layout-only scope. We do not fabricate extra chat windows or replace subscriptions to imitate it.

[Skuly's AOU Mod forum post](https://forums.funcom.com/t/aou-mod-1-2-5-fixes/150105) provides another historical personal setup and links a 1920×1080 screenshot. The image was not available during this review, so it was not used as a geometry reference. Later replies report a skills-window issue with that mod; the existence of an attractive screenshot is not a compatibility guarantee.

For optional skin customization, [the ndGUI author's feature post](https://forums.funcom.com/t/notum-dovvetech-gui-4-34/6549) documents compact menus and several themes. Theme choices remain separate from these layouts. Use the correct package for the client engine and test one installation before broad rollout.

No community screenshots, private player preference folders, or third-party skin assets are redistributed as part of these starter layouts. Source links are provided for attribution and further reading.

Research checked: 25 October 2026.
