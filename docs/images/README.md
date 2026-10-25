# Guide images

The README has one unmarked overview. The player guide and Game GUI guide use ten distinct task illustrations. Red numbered boxes identify controls described in the adjacent caption. Progress and health are cropped to avoid repeating the dashboard.

All images come from actual AO Nexus controls with example accounts, empty passwords and offline results. They do not show live server health or announce an unpublished release. No personal profile or game client is opened by the capture workflow.

## Regenerate

From the repository root, using the current built executable:

```powershell
& "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -File tests/Capture-GuideScreenshots.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Verify-Guide.ps1
```

The capture script runs the GUI manager's synthetic workflow checks. Raw captures stay in ignored `logs/guide-captures`; only curated images are published here. `tests/Guide-ImagePlan.ps1` defines fixed source sizes, crops and callout coordinates. A changed window size fails capture and requires reviewing those coordinates. Also inspect all images after any control-position change; a size check cannot detect every shifted label.

`tests/Publish-GuideImages.ps1` updates image links in the README, Player Guide and Game GUI guide. Each image must appear exactly once across those pages. Content-hashed filenames prevent GitHub's image cache from retaining an older screenshot. It writes [SCREENSHOTS.md](SCREENSHOTS.md) with hashes and removes obsolete generated PNGs from this folder.

Keep a screenshot only when it explains something the existing images cannot. Prefer a crop or a few red callouts to another full-window image. Do not use a screenshot to duplicate a paragraph of help text. Keep callout numbers in sync with captions, use readable full-size dialogs, and verify the rendered Markdown before publishing.

Documentation changes can be committed without a new application release. Already published release archives and checksums must remain unchanged; the revised offline guide joins the next normal package.
