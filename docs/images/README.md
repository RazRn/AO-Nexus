# Guide images

The README has one unmarked overview. The player guide and Game GUI guide use thirteen distinct task illustrations. The nine retained launcher images use their original content-hashed names, crops and red callouts. The five Game GUI 1.2 images show the full client area without annotations, following the numbered steps already present in the application.

All images come from actual AO Nexus controls with example accounts, empty passwords and offline results. They do not show live server health or announce an unpublished release. No personal profile or game client is opened by the capture workflow.

## Regenerate

From the repository root, using the current built executable:

```powershell
& "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -File tests/Capture-GuideScreenshots.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Verify-Guide.ps1
```

The capture script runs the GUI manager's synthetic usability checks. Raw launcher captures stay in ignored `logs/guide-captures`; Game GUI captures are in `logs/game-gui-usability`. Only curated images are published here. `tests/Guide-ImagePlan.ps1` defines fixed source sizes, crops, callout coordinates and the five Game GUI source filenames. A changed window size fails verification. Also inspect all images after any control-position change; a size check cannot detect every shifted label.

`tests/Publish-GuideImages.ps1` updates image links in the README, Player Guide and Game GUI guide. Each image must appear exactly once across those pages. Content-hashed filenames remain in use for the nine launcher images; Game GUI images have explicit `-1.2` filenames and their actual SHA-256 hashes and QA source filenames are recorded in [SCREENSHOTS.md](SCREENSHOTS.md). Verification checks the hashes, dimensions and provenance of both kinds, and still checks every original red callout.

To update only the five Game GUI illustrations after approving the final usability captures:

```powershell
& "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -File tests/Verify-GameGuiUsability.ps1

# Inspect the five English, 100%, default-size images before publishing them.

powershell -NoProfile -ExecutionPolicy Bypass -File tests/Publish-GameGuiImages.ps1 -Version 1.2.0.0
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Verify-Guide.ps1
```

This copies `en-1-default-tab0/1/2/3.png` and `en-1-default-macros.png` to the five versioned filenames. They are unedited, full 1120 × 740 client areas, using synthetic accounts, layouts, skins, scripts, macros and recovery records. The publisher preserves the nine existing images and their provenance, records the new image hashes, and removes only the three obsolete Game GUI images after confirming they are no longer referenced.

Keep a screenshot only when it explains something the existing images cannot. Prefer a crop or a few red callouts to another full-window image. Do not use a screenshot to duplicate a paragraph of help text. Keep callout numbers in sync with captions, use readable full-size dialogs, and verify the rendered Markdown before publishing.

Documentation changes can be committed without a new application release. Already published release archives and checksums must remain unchanged; the revised offline guide joins the next normal package.
