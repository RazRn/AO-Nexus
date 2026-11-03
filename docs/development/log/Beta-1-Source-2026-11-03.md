# 1.3.0 Beta 1 source update

This update labels the current development build Beta; it does not claim stable-release readiness or publish a binary package. LICENSE-NOTICE.txt and the outstanding runtime-release gates remain applicable. AO#, third-party plugin packs, local profiles, binaries and raw diagnostic recordings are excluded from the source publication.

## Changes

- Informational version is 1.3.0-beta.1; numeric assembly/file versions remain 1.3.0.0. The main title, header, splash, installed-version dialogs and problem-report version identify the Beta.
- Stable 1.3.0 is eligible to replace Beta 1 despite having the same numeric version. Older stable versions remain ineligible; the updater continues to reject prerelease metadata.
- A minimized card preview is labelled Minimized preview. Its tooltip explains that the game may stop drawing and points to Settings → Advanced → Minimize inactive game clients. Telemetry is independent of the preview.
- Recommended settings preserve existing auto-minimize and experimental opt-ins; a fresh profile does not enable them.
- README, player guide, offline guide, EN/DA What's New, experiment/plugin notes and privacy documentation describe the current Beta. Twenty-eight screenshots were regenerated from real controls with synthetic data and visually reviewed. No live account or game image is published.
- The public source snapshot is based directly on the previous GitHub main commit, rather than publishing the intervening private development history. Personal workspace prefixes in the generated inventory page are replaced with placeholders.

## Validation

- Launcher built from the isolated restored source tree; its embedded backend matches 201 files from that build tree.
- 78 Framework unit tests and 11 launch-core unit tests passed.
- 39 update metadata, version, digest, ZIP, locked-file and backup checks passed.
- 1,453 Game GUI fixture checks passed across English/Danish and normal/enlarged layouts.
- 327 documentation link, anchor, screenshot hash, dimension and structure checks passed.
- Retired AO-VK payload is still unavailable. No optional pack was downloaded or executed by this check.
- Defender's exact-file scan found no threats. No exclusions or disabled protection were used. This is a file-scan result, not completion of the separate runtime release review.

Local Beta executable SHA-256: 9EF197E50D1B565C7A08A5F329A9FDB640B3AC133FF0B0EB412E2ED66C1C43F1.

The Engineer standing-required casting spam remains reproduced and unresolved. The Beta notes explicitly say so. Earlier gameplay results and remaining limits are recorded in [release-readiness history](Release-Readiness-2026-11-03.md); this documentation pass did not relog clients or rerun movement experiments.
