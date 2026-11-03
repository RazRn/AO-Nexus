# 1.3.0 local candidate validation — 3 November 2026

Status: local candidate installed; public release is not approved. No GitHub push, upload, Microsoft submission or license permission request was performed. Existing unrelated working-tree changes were preserved.

Current installed candidate SHA-256: `9c7886b0ae20b3d03aa33d12e6ab29635c85f255b16ff6cd4e3459798809698b`. Earlier evidence below belongs to the preceding candidates where explicitly noted.

The installed `dist/AONexus.exe` now matches the final isolated-source build. A previous executable is retained locally as an ignored `.exe.bak` file. The selected 64-plugin AO# installation and AO-VK 0.8.1 test game were retained.

## Fixes from this validation

- **Early startup race:** Reloaded.Injector 1.2.5 indexes `Process.Modules[0]`. An immediately started process can temporarily expose an empty module list. Nexus now waits for a nonempty Windows loader snapshot containing kernel32, polls only while necessary, and fails after five seconds. Access-denied errors are not retried. A failed initial injection cleans up only the client this launch just created, avoiding an abandoned login window. Diagnostics include method names without arguments or source paths.
- **Fresh team membership:** the engine roster can become empty after zoning while the game's GUI still shows the team. The tested GUI export provides a bounded roster of identities. Its compiler-specific list layout is gated on the exact GUI hash, read with ReadProcessMemory, and checked for size, loops, backlinks, duplicates, self-membership and header changes. Automatic teams and both formation entry points use this shared reader. Unknown or incomplete state does not authorize movement or invitations. See live-test limitations below.
- **Control Panel confirmations:** its window is intentionally unowned so it can sit behind Nexus. Global login/logout shortcuts now also check its child dialogs explicitly. They cannot bypass a pending confirmation just because the panel is not owned by the main window.
- **Diagnostic responsiveness:** email redaction no longer retries every suffix of a long word. A 100,000-character regression fixture verifies bounded processing while preserving email redaction. The formerly very slow maintenance fixture now passes.
- **Retired renderer download:** the bundled descriptor still described AO-VK 0.7.1. It is now explicitly retired and rejected before cache/download/install. Normal builds no longer generate this optional archive automatically. Existing renderer installations remain intact; a replacement needs a verified new bundle and immutable release tag.
- **Preview accuracy:** synthetic news headlines are identified as examples only in preview mode. Normal live-news filler remains hidden.
- **UI wording:** Control Panel now visibly uses `Attack: Idle` and `Aggro: None`. English/Danish catalog parity passes.
- **Nano posture recovery:** Geek repeatedly received “must be standing” while visibly standing in the stock-renderer run. An explicit sit–stand cycle immediately allowed Feeble Automaton to execute and the pet appeared. A bounded helper now requests explicit Stand Up only after repeated exact standing-required feedback while the local player is alive, stationary and in a casting-compatible standing state. It makes at most two requests per zone, at least five seconds apart, never toggles sitting, and exposes rejection/correction counts through HEALTH. Automatic correction of a live mismatch remains unverified; the next AO-VK launch did not reproduce the rejection.

## Reproducible evidence

Local evidence is under ignored `logs/ReleaseCandidate/` and `logs/FormationBuild/`; it is not part of a public package.

| Check | Result |
| --- | --- |
| Isolated source export, locked dependency restore | Passed; 348 source inputs copied without build outputs or game files |
| Framework MSTest suite | 62 passed, 0 failed, 0 skipped |
| Framework suite after posture recovery addition | 67 passed, including five new recovery-policy tests |
| Launch-core MSTest suite | 11 passed, 0 failed, 0 skipped |
| GUI roster decoding | Five of the 62 tests cover malformed, stale and valid lists |
| Loader readiness | Five of the 11 tests cover immediate readiness, delay, partial-copy retry, access denial, exit and timeout |
| Diagnostic/privacy integration | 61 checks passed, including long-token processing |
| Log retention and maintenance | 20 checks passed, isolated temporary files only |
| Optional components | 36 checks passed; retired embedded distribution rejected without downloading/executing it |
| Control Panel integration | 370 checks passed |
| Native combat meter | 43 checks passed |
| Profile import, password preservation, settings/sidebar, hotkeys, GUI tools | Focused integration checks and 50 printed umbrella stages passed |
| Camp control, embedded backend, news, payload | Camp-control test reflection was updated to select the intended overload; rerun passed. Embedded backend checks, 53 news checks and 201 backend-file manifest checks passed |
| Git whitespace check | Passed with CRLF-aware whitespace rules |
| Exact executable Defender custom scan | Exit 0; no threats found; signature 1.463.107.0 |
| Antivirus, real-time and behavior protection | Enabled throughout; no active threats reported at final scan |

The source-only tests ran in a fresh temporary directory with no copied DLL/EXE/ZIP/PDB build artifacts. This establishes portable unit-test inputs, not a clean Windows installation test. The GitHub Actions workflow exists but has not been run remotely by this session.

The umbrella verification stopped on the obsolete camp-control reflection lookup, not on a runtime assertion. That fixture was fixed and the remaining stages were run individually. This was not one uninterrupted passing umbrella run.

## Live evidence and limits

The first attempted launch exposed the loader race and aborted after leaving one client at login. That process was removed and the race fixed; it was not counted as a successful run.

After the fix, all six AO-VK clients reached world-ready in **10.60 seconds** as measured by the backend. Including preparation, the launch operation took about **15 seconds**. All six formed one team about **26 seconds after the launch click**. A second six-client relog using the final executable completed world loading in **10.41 seconds**, with team formation about five seconds later. Plugin startup was separately confirmed. These timings come from two successful local runs and are not a universal performance guarantee.

All six live GUI rosters were validated and returned ready with matching membership. Nexus was restarted while clients remained open; the final executable recovered all six clients and confirmed all plugin startups. The Control Panel showed the actual team leader and current health/nano data. Formation was left stopped.

The separate stock-renderer installation then loaded all six in **15.82 seconds**, and a stock relog completed in **11.94 seconds**. The corresponding complete teams were observed at 16:00:12 and 16:01:55 local time. All six plugin startups and matching ready GUI rosters were confirmed. The Geek posture issue above was observed during this test; these timings do not imply an issue-free stock run.

After normal logout, the exact saved Nexus profile was restored and all six were launched again in the original AO-VK 0.8.1 test installation with the posture helper. This final launch completed in **10.5 seconds**; the team formed about **15.6 seconds after launch started**. All six helper HEALTH replies included the new posture counters, with zero rejections/corrections. Geek's pet was present and deliberate sitting was preserved until manually stood again. Formation remained stopped.

An end-to-end zoning/leadership-recovery test of the new GUI roster reader is still required before that release gate can be marked passed. Static validation and an unzoned live roster do not substitute for it. The recent Wazoo white-screen workaround is documented in the user release notes; the renderer's underlying fault is not claimed fixed.

## Publication gates still open

1. The strict Defender release preflight could not verify exclusion status: MpCmdRun returned `0x80070005` (access denied). The ordinary exact-file scan passed, but the stronger gate remains blocked. No protection setting or exclusion was changed.
2. The existing Defender runtime-review policy requires exact-hash scenario evidence and a Microsoft false-positive resolution for the recorded incident. No such verdict is available; no attestation was fabricated and packaging gates were not bypassed.
3. `LICENSE-NOTICE.txt` still records unresolved redistribution permission for AOQuickLauncher-derived NexusEngine/LoginHandler code. AO# and third-party plugin bundles likewise have no verified redistribution permission in this checkout. Public source/binary availability is not treated as permission. No public bundle was uploaded.
4. Complete the outstanding post-zone live check and exercise posture recovery against a recurring live mismatch, then repeat exact-artifact validation if the executable changes.

These are explicit release blockers, not permission prompts for the current local development work. Continue local testing on the installed candidate; do not label it a finished public release yet.

## Navigation border follow-up

The main navigation now reserves one continuous bottom edge outside its child panels. Tabs and the standalone Control Panel action share the themed background and align to the available height, including scaled layouts. The release build passed; existing synthetic previews were inspected at normal and enlarged sizes. The updated executable passed its Defender custom scan (see ignored logs/NavigationBorderPreview/defender-scan.txt). Only Nexus was restarted; the six game processes were retained. The live loading timings above belong to the preceding candidate with identical launch code; they were not rerun for this paint-only change.

## Formation toolbar alignment follow-up

Formation controls now use equal-height groups with vertically centred labels and editors, matching dropdown/button heights and the themed numeric spinner. Groups wrap together at narrow widths. Existing Control Panel checks passed (370); wide and narrow synthetic previews were inspected. The build and exact-file Defender scan passed. Only Nexus was restarted, retaining all six game clients. No formation or movement behaviour changed.

## Settings cleanup follow-up

Settings now groups launch/account controls, updates/server rules and diagnostics in separate pages. Related actions share rows; the dialog keeps a stable size with vertical scrolling and a visible Save/Cancel footer. Graphics selectors have reserved layout height and content no longer expands horizontally. The new Use recommended settings action stages core launch, monitoring, logging, update and confirmation preferences only. It preserves language, Windows startup, sidebar selections, graphics and experimental opt-ins, and neither saves nor runs actions until Save.

Validation: 68 Framework unit tests and 168 settings layout checks passed (English/Danish at 100%/150%). The draft test covers repeated application, cancellation/disposal, preservation of existing experimental choices, and absence of settings mutation/action callbacks. Build and exact-file Defender scan passed. Local installation retains a rollback EXE; game clients did not require a relog.

## Automatic regroup follow-up

Added optional Auto regroup beside the Control Panel formation controls, gated by the existing team automation experiment. A fresh per-process permission requires the actual leader, matching team rosters, zone and sync channel. Permission expires after three seconds and leader game-update evidence after two. The game-thread guard takes an exclusive movement receiver lease after an accepted-sync separation stop; attack callbacks remain untouched. Clear-route ordinary movement ends on arrival, obstruction, four seconds without progress, a 20-second deadline, manual focus, combat/casting, zoning, stale state or leader exit. A blocked follower keeps movement sync suspended until released or reunited, and requests one explanatory Team location link through the existing cooldown/channel-confirmed rally sender.

Validation: 77 Framework unit tests, 89 isolated game-adapter guard checks, and 38 production-plugin/local-IPC transport checks passed. Tests cover arrival, blocked and stationary movement, frozen updates, manual focus, combat/aggro, zoning, option withdrawal, leader exit, channel changes, unsupported pace, competing formation ownership, roster/process mismatch and preference persistence. Release and real-runtime monitor builds passed. Defender's exact-file scan found no threats; protection settings were unchanged. This does not replace the separate release gates above.

Installed the candidate with rollback EXE retained as dist/AONexus.before-auto-regroup-20261103.exe.bak. With explicit user approval, relogged all six test clients and enabled the saved Auto regroup checkbox. All six publish AutoRegroupSupported=true and fresh telemetry. Five current followers receive enabled grants naming the actual leader; the leader receives a disabled grant. The GUI roster matches all six members. Control Panel layout and checkbox were inspected at wide/narrow window sizes. Overwhelming was restored to the foreground with the Control Panel left open. Existing movement refresh stays events-only and formation remains stopped.

This launch reported 9.88 seconds for all six clients; team formation completed at 17:04:50 local time. The live check verifies installation, team formation, capability and grant issuance. An intentional live separation, blocked return and resulting in-game location message have NOT yet been exercised; the corresponding behavior was tested with inert adapters. Evidence remains in ignored logs/auto-regroup-*.txt and logs/auto-regroup-live-install.json.

## Final source-build and live follow-up

The latest candidate was built from an isolated export of 521 source/documentation inputs with no initial DLL, EXE, PDB or ZIP inputs and no copied game installation. The build restored pinned AO# reference source and Detours, compiled the native and managed backend, built the helpers and verified the embedded manifest against 201 backend files. Compilation retains existing warnings; it completed successfully. All current C# and project files were compared against the tested export: no differences.

The fresh build exposed a private Local13Pack dependency in Build-Shortcuts.ps1. Its default now uses the same pinned reference-source restore as telemetry; an explicit runtime override remains available. The guard and play-tools fixtures also accept explicit reference paths or restore the pinned dependency. The play-tools fixture now includes the shared GUI roster sources. Default build instructions no longer request gated public packaging.

Final checks:

- 78 Framework unit tests and 11 launch-core tests passed. The final 78 also passed in the isolated source export.
- 89 inert game-adapter guard checks and 53 timer/formation checks passed using freshly built reference assemblies.
- 392 Control Panel integration checks passed, including stable card/action bounds across selection and different client aspect ratios.
- Exact final executable Defender custom scan returned 0 and reported no threats. An earlier scan of a temporary-path artifact reported skipped and was NOT counted as validation; the final artifact was scanned at its normalized local candidate path. Protection remained enabled.

Character selection no longer changes card height. All previews have a reserved 16:9 frame; the actual client image is fitted and centred without stretching or cropping. Restoring a minimized client can update its image proportions without rearranging cards, health bars or buttons. The live Control Panel was inspected with restored and minimized clients.

A live log review exposed transient sharing/not-found exceptions during regroup permission replacement. Readers and writers now coordinate through a per-path, 50 ms bounded named mutex while retaining atomic replacement, an 8 KiB read limit and fail-closed permission handling. The concurrent fixture alternates 500 writes with 1,000 reads and checks that accepted snapshots are complete. Missing or oversized input grants no movement.

The final executable was installed with a rollback copy retained locally. The approved Relog all restarted five clients; Sexologist remained open, so that run is NOT recorded as a successful six-client relog. A separate normal relog updated Sexologist. All six then reported the same new monitor path, zero observed monitor exceptions, matching ready six-member rosters, and Overwhelming as actual leader. Formations were stopped. Overwhelming was subsequently relogged alone; his new process returned to the same six-member roster and regained leadership. The final live snapshot is saved locally in logs/Finalization/leader-recovery.json. This verifies relog recovery, not zoning recovery or a crash simulation.

Live limitations remain explicit: the partial first relog needs investigation before claiming reliable Relog all during active formations. Deliberate separation/blocked auto-regroup with its Team message, and the post-zone GUI roster scenario, remain unverified in this final pass. Geek recorded no new posture rejections, so automatic correction of the original mismatch was not exercised. These are not marked passed by the offline fixtures.

No public GitHub push, release upload, permission request or Microsoft submission was made. The unresolved source-distribution notice and binary-release gates above still apply. The local candidate is usable for continued validation; it is not labelled an approved public release.

## Logout coordination and blocked-regroup live follow-up

The partial relog was traced to an explicit client refusal of normal `/quit` for Sexologist, rather than a launcher timeout or failed replacement process. Funny business was active, and Nexus did not stop its movement before camping. The native refusal does not identify its exact cause, but allowing formation/regroup movement during the logout countdown was a confirmed coordination gap.

Confirmed normal logout/relog now invalidates formation keep-alives, drains pending movement updates, revokes regroup grants and waits for formation stop confirmation before sending `/quit`. Existing three-second regroup permissions are allowed to expire. Cancelled confirmation leaves movement untouched; completion restores polling without restarting the old formation or changing saved preferences. The extra preflight applies to logout, not initial login. Failure to confirm a stop prevents logout and reports the reason.

400 Control Panel checks passed. In the live test, all six clients first confirmed active Funny business. Cancelling Relog all retained the same six processes and active formation. Confirming the next attempt stopped formation and restarted all six successfully. Backend world loading took 10.59 seconds; the complete login operation reported 14.1 seconds. The team formed at 17:57:54 CET, with matching ready six-member rosters, Overwhelming as actual leader and zero observed monitor exceptions. Evidence: `logs/Finalization/logout-test-active.json`, `logout-test-cancelled.json`, `logout-test-returned.json` and `zoning-regroup-events.json` (local, ignored).

During the subsequent ICC/Jobe test, Overwhelming, Sexologist and Calm crossed to Jobe Platform while the other three remained in ICC. All six retained ready, matching GUI rosters and the actual leader. This verifies roster continuity across a split-zone transition, not successful coordinated zoning of all six. Calm and Sexologist then encountered a blocked return route at about 30 metres, stopped and each posted one visible clickable Team location link. Telemetry recorded `Regroup blocked: Path obstructed` at 18:00:09 CET. Evidence: `logs/Finalization/zoning-jobe.json` and `regroup-blocked-events.json`. This closes the earlier untested blocked-return/message scenario; it does not establish general navigation reliability.

The manual Regroup action exposed a separate stale engine-roster dependency. It now receives the same fresh, ready GUI roster used by team automation. A missing roster, absent local player/target, self-target, combat, zoning, unsuitable channel, obstruction or competing navigator still rejects movement. Twenty movement-probe checks passed, including a valid GUI roster with an empty engine roster; 53 play-tools checks passed. This last manual-Regroup change is built but its live validation is still pending.

The logout-tested installed artifact is SHA-256 `A8C97C6B64283536DA90EFAF3265CB40E1C9F76180B77A4766959BF1E762004B`. The subsequent source-consistent build including the manual-Regroup correction is `F71507F298EC597BE13501D56D889329E36520D7575EDC34A731FB884AAC3DCD`; its backend manifest check covered 201 files and its exact-file Defender scan returned 0 with no threats. Real-time, antivirus and behavior protection stayed enabled. This scan does not satisfy the separate Microsoft incident-resolution or strict release-preflight gates above.

That subsequent build was installed at `dist/AONexus.exe`, retaining `dist/AONexus.before-manual-regroup-20261103.exe.bak`. Nexus was restarted and reconnected to all six existing game processes; each retained a ready matching roster, Overwhelming remained leader and all formations were inactive. The updated manual-Regroup helper will load on the next client relog; it has not been live-verified yet. The three Jobe clients were left stopped while the user was asked to return them to ICC. This navigation handoff is not recorded as a successful reunion. Final retained-client evidence: `logs/Finalization/installed-final-kept-clients.json`.

## Returned-team and manual-Regroup verification

The user returned the three Jobe clients to ICC. Fresh telemetry confirmed all six in Andromeda within approximately three metres, with matching six-member rosters. The approved Relog all then replaced all six processes on the final artifact: backend world loading took 9.36 seconds, the complete login operation reported 13.5 seconds, and team formation completed at 18:37:15 CET. All six loaded the updated monitoring payload, reported ready matching rosters and retained Overwhelming as leader with no active formation.

Manual Regroup on Calm accepted Geek as a valid nearby teammate and displayed `Follow requested` in-game. This verifies live roster validation and command dispatch; at approximately three metres no meaningful travel distance was recorded, so it is not a longer-path navigation test. The test follow was released with a normal movement key. Evidence: `logs/Finalization/final-regroup-relogged.json`, `manual-regroup-calm-to-geek.json`, `final-regroup-validation.json` and `final-regroup-positions.json`.

Geek reproduced the standing-error spam after this relog while visibly standing. Two observed chat states showed continuing messages around the manual-Regroup reply. His Regroup request correctly refused while casting was pending. HEALTH continued to report zero posture rejections and corrections: the current feedback-based recovery did not detect this live failure. This is now a reproduced unresolved defect, not merely an untested scenario; no automatic posture-recovery success is claimed. The captured state is `logs/Finalization/geek-posture-reproduced.json`. Investigate the error delivery path before changing recovery behavior; no movement or posture state was forced to conceal the failure.
