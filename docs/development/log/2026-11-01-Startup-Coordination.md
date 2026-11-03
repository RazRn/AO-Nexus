# Post-login startup coordination

The supplied 19:46 and 19:56 logs showed six game clients loading in 9.02 and 8.79 seconds, followed by approximately 25 seconds from launch to the final profession-plugin dispatch. Team setup was issuing requests against monitor-only warmup clients. Recovery, formation and plugin loading shared one busy polling loop, so sequential helper timeouts delayed subsequent plugin passes. The generic timeout message incorrectly prescribed updating/restarting Shortcuts.

Changes:

- `StartupPolling` in Nexus.Core gives plugin and team work independent, non-overlapping lanes. Busy ticks are dropped rather than queued. Faults release their lane.
- Published injector rows distinguish warmup/profession probing from a full set that actually requests Shortcuts. Formation and recovery skip monitor-only rows. Recovery also skips the roster reserved for active formation.
- Team helper pipe connections do not wait for an absent server. Reply reads remain bounded. Formation does not send a plan after a null, busy or error status response; unavailable and explicitly unsupported helpers have distinct messages.
- Formation polling/cancellation share a lock to protect state across the independent worker and launch actions. Existing session/start-time binding, follower acknowledgement, invite budget and game-thread checks are retained.
- The completion label now says clients loaded, with explicit notice when plugin setup/team formation continues. Per-character plugin readiness requires observed runtime startup; dispatch alone and the monitor probe cannot claim readiness. Successful formation has a named team summary.
- Formation expiry reports unconfirmed member names (or the leader's unconfirmed roster), and cancels queued plans instead of announcing successful completion.

The health-monitor-first safety probe and legacy full-plugin-list replacement are retained. A monitor instance may still restart once as that list is replaced; these changes do not promise continuous measurements across that transition. No new plugin protocol or client DLL is required.

Verification:

- 44 MSTest launcher/core tests passed, including independent-lane blocking/fault tests, warmup gating, pending/unsupported helpers, named timeout cancellation and honest readiness.
- Six absent helper pipes (both status and team-view calls) completed in about 2.5 ms on this PC, rather than accumulating configured connection waits. This is a transport fixture, not a six-client launch benchmark.
- 335 Control Panel, 32 plugin-loading/navigation, 21 automatic-team and 23 recovery integration assertions passed with synthetic data and no game interaction.
- Release build and embedded payload verification passed (201 backend files).
- Final single-executable smoke startup/exit passed.
- Defender custom scan reported no threats. Antivirus and real-time protection remained enabled; no exclusions were added.

Final candidate SHA-256: `3F4F8D29460270B0230F59BFF242FE55F29D974351A3274F9BBD04E5EC395421`.

One older pack-dependent harness, `Verify-PluginStartupFlow.ps1`, fails before simulation because `runtime/Local13Pack/Plugins/Nexus/NexusShortcuts.dll` hashes to `88C065710A85BFF749338F9B5222033C0A4DF4C2ED71F535F3086CCA01444E4E`, while the existing test policy pins `D4577352A6DFD8D6CC11FE5E4737DCAFA6EF6ED321A730035A33F34AECB3C8E1`. Neither the pack DLL nor admission policy was changed. That failure is recorded, not counted as a passing check or bypassed.

No live six-client relog was performed. The actual post-login improvement and the first log's delayed Geek membership still need live observation. Current experiments remain explicit opt-ins; this fix does not silently enable formation or AO-VK tuning.
