# Launcher and client performance investigation

Measurements collected on 26–27 October 2026; consolidated on 28 October 2026.
Repository baseline for this write-up: `118e207` (AO Nexus 1.2.2).

This records the local Fiddler, process-observation, Process Monitor, client-hook,
and launcher-benchmark investigation behind the startup improvements. The saved
Sysinternals capture is **Process Monitor (Procmon)**. No separate Process Explorer
measurement export was identified in the reviewed evidence.

The largest demonstrated early improvement was removing the client's initial
4-second and 7-second fades. Later gains came from initial texture processing
and, for the verified Rebirth configuration, sequencing authentication on actual
server acceptance while clients prepared in parallel. Rewriting the launcher UI
alone would address a much smaller part of the measured delay.

## Evidence and measurement boundaries

The initial lab used the classic `18.8.62_EP1` client on official Funcom Rubi-Ka,
first in ICC and later in an indoor weapons shop. The recorded machine had an
Intel Core Ultra 9 275HX, 24 logical processors, approximately 63.4 GiB RAM, and
NVMe storage. The later six-character tests used AO Rebirth; they are a separate
workload, not additional samples of the official-server experiment.

These were warm/repeated local tests with changing desktop and service load.
There was no controlled cold boot or cache purge. Small exploratory blocks must
be compared within their block, not pooled into a single performance ranking.

Different endpoints answer different questions:

| Endpoint | What it establishes |
| --- | --- |
| Launcher first composed window | A synthetic launcher UI has reached its compositor milestone. |
| Input-idle / window appears | A process/window milestone; not proof of login or world readiness. |
| Character window title | Character confirmation; the loading overlay can still be present. |
| Client loading-overlay removal | Native world-ready milestone used for lab timings; not an exact GPU-present timestamp or completion of all background work. |
| Server admission response | Permission to release the next authentication on the inspected Rebirth path; not proof of successful later rendering. |

The lab's client timer starts around process creation, after some preparation.
Production probes and outer harness timers include different amounts of validation,
injection, worker startup, and status delivery. Their absolute values are not
interchangeable. Screenshots and F10 input checks confirmed selected runs only.

The [sanitised measurement summary](performance/measurements-2026-10.json) preserves
aggregate numbers, experiment labels, and hashes of the source summaries.
It contains no raw HTTP sessions, packet payloads, account names, character IDs,
machine-specific paths, or whole-system process capture.

## Fiddler and network observations

Fiddler was inspected and a launcher-only capture rule for `Anarchy.exe` was
configured. A direct probe of the official dimension manifest through the local
Fiddler proxy **failed while sending the request**. It produced no usable successful
HTTP latency measurement and is not evidence that the game service was slow.

The file named `official-launcher-fiddler-01.jsonl` is a **ProcessObserver log**, not
a Fiddler session export. Its first observed window was the licence agreement.
It cannot establish authentication round trips or time until a character was playable.
No successful Fiddler session export was identified among the reviewed lab files.

The useful client-network work instead instrumented connection/send/receive timing
and byte counts alongside native client milestones. This distinction matters:
HTTP launcher observations must not be presented as measurements of AO's game
protocol. No credential replay or protocol shortcut is established by this evidence.

Instrumentation also has a cost. Within Trace06, two traced runs had a median
of 6.528 seconds versus 6.184 seconds for two untraced runs. That small sequential
block suggests measurement overhead but does not isolate its exact causal cost.
Normal fast-profile runs disabled network tracing and deep profiling.

## Process Monitor and remaining loading work

The original PML included other processes. Analysis selected only the two designated
AO test processes. There were 79,529 and 79,603 events before their loading overlays
disappeared. ResourceDatabase reads, driver registry lookups, and repeated package
path probes accounted for substantial activity.

The CSV/XML export lacked an operation **Duration** column. Counts of `PATH NOT FOUND`,
registry queries, or file reads therefore do not establish time spent blocked.
Requested `ReadFile` bytes are not physical disk traffic. Active Procmon recording
also means these runs must remain separate from later uninstrumented benchmarks.

A separate detailed native trace found approximately 2.98 seconds of main-thread
CPU work within a 3.25-second world-loading phase, while measured file API calls
totalled approximately 22 ms. That supports investigating client content processing
in that sample; it does not prove disk or network latency is irrelevant everywhere.

## Client startup experiments

### Fades, direct login, and cosmetic menu models

| Block / variant | Runs | Median to overlay removal |
| --- | ---: | ---: |
| Paired02: normal fades | 1 | 17.104 s |
| Paired02: GUI login, both fades zero | 3 | 5.995 s |
| Paired02: direct quiet login, both fades zero | 3 | 5.960 s |
| Paired04: skip cosmetic login meshes | 2 | 5.468 s |

The normal-fade row is a single reference observation. The later mesh block is
not a controlled estimate of mesh removal's independent benefit.

Quiet login by itself did not remove the initial fades. It is a setting in the
injected login handler, not an established built-in `AnarchyOnline.exe` command-line
switch. Direct login with zero fades performed similarly to GUI login with zero
fades in the paired block. Skipping 20 cosmetic login models provided a further
candidate improvement while preserving actual world loading.

Single-run fade ablations were also retained: shortening only fade-out gave
10.892 seconds; shortening only fade-in gave 14.698 seconds. These are exploratory
results, not independently repeatable estimates of every transition's cost.

### Texture processing

In Pixels11, three reference runs had a 7.016-second median, compared with
6.642 seconds for three runs using the optimised scalar bilinear filter: about
374 ms / 5.3% lower. All three pairs favoured the scalar filter.

The routine was checked against the original function with 100,000 random cases,
25,533,619 pixels, and complete mutable-state comparisons. A live shadow run
compared 506,754 rows with zero mismatches. Within that shadow run the original
routine took 1,196 ms and the candidate 932 ms; executing both routines makes
that run unsuitable as a normal startup benchmark.

The first shadow prototype crashed before login because the decoder input list
lacked its terminating null pointer. That contract was corrected and the offline
test strengthened. The crash handler exited with code zero: **zero exit status
alone was not counted as successful world entry**.

A SIMD candidate also produced zero mismatches but took 1,455 ms against the
original's 1,140 ms in its shadow comparison, so it was rejected. Disabling texture
compression produced only a single exploratory comparison and was not selected
as a default. Fast-core selection likewise showed no stable benefit: CPU05 medians
were 5.840 seconds with the normal scheduler and 5.959 seconds with selected cores
(three runs each).

The final combined visual/F10 validation took 8.019 seconds. This later observation
is retained rather than replacing it with an earlier best time. The original
client analysis records 46 exploratory runs, 44 reaching overlay removal; incomplete
and crashed attempts must not be silently treated as completed runs.

### Different location

Ten further starts in the indoor weapons shop compared the scalar filter with
the otherwise identical reference profile, without network tracing or profiling.

| Variant | Runs | Median | Range |
| --- | ---: | ---: | ---: |
| Fast scalar filter | 5 | 3.166 s | 3.034–3.247 s |
| Reference filter | 5 | 3.335 s | 3.154–3.575 s |

Four of five pairs favoured the fast filter. The earlier ICC timings were collected
at another time; the faster indoor results do not isolate location as the sole cause.

## Launcher technology and packaging

The shell benchmark recorded 838 observations: 786 valid and 52 invalid. Invalid
observations came from the original no-CRT argument parser executing the wrong mode;
they were retained and excluded from ranking. QPC readiness timestamps corrected
approximately 14–27 ms of managed reporting overhead.

Main-block composed-window medians, 25 valid observations per listed variant:

| Synthetic shell | Median |
| --- | ---: |
| Native C++ x64 | 79.06 ms |
| Native C++ x86 | 79.44 ms |
| .NET NativeAOT raw Win32 | 105.28 ms |
| .NET Framework raw Win32 x64 | 127.78 ms |
| .NET Framework WinForms x64 | 149.32 ms |
| .NET 10 WinForms x64 | 266.89 ms |

These are small shell prototypes, not equivalent full AO Nexus implementations.
The tiny x86/x64 native difference does not justify a ranking.

Compressed single-file deployment was slower in all 25 paired composed rounds,
with a median paired penalty of 124.85 ms and median peak working sets around
110 MiB versus 47 MiB. ReadyToRun and uncompressed single-file versus folder
deployment did not establish a consistent recommendation. Corrected no-CRT code
did not beat ordinary native code in the contemporaneous recheck.

The native anchor itself improved from 79.06 ms to 54.47 ms between blocks. This
demonstrates why results from separate system states must not be pooled. Fresh
bundle extraction had three samples per bundle on a warm OS, not a cold-boot test.

## Integration and same-account sequencing

The supported startup changes were subsequently integrated into NexusEngine with
client hashes and native attachment guards, an opt-out, normal unsupported-client
fallbacks, and unchanged game files on disk. Character creation remains outside
the fast path. Details and integration checks are in [Fast startup](Fast-Startup.txt).

For Rebirth, the inspected upstream revision was
`2d4edfded3bfee27f5c89753e1250e9c94b107e7`. Starting another same-account login too
early can replace the pending generation before the preceding ticket is claimed.
On the inspected path, `ChatServerInfo` follows successful admission; it provides
a release signal instead of guessing a fixed interval. The deployed public-server
revision was not independently verified.

The six-character serial reference completed in 31.908 seconds. A fixed 500 ms
experiment lost its first client before world-ready and was stopped. Acceptance-
controlled preloading reached 13.505 seconds in the selected exploratory run and
13.481 / 13.265 seconds in final repeats: 18 successful logins over those three runs.
The first character became ready later because preparation was shared across the
team. See [Admission pipeline](Admission-Pipeline.txt) for intermediate variants,
gate-order checks, fallback behaviour, and the preserved intermittent UI-test result.

## Background clients

The separate six-client AOSharp experiment found ordinary minimisation sufficient
on the tested classic renderer. Across three phases, 6,600 local IPC requests
received 6,600 replies, with no two-second timeouts and no measured update interval
above 50 ms. Windows power-policy overrides showed no demonstrated responsiveness
benefit, so no extra rendering hook or persistent policy override was justified.

This measured local AOSharp update/IPC responsiveness, **not server packet loss**
or every combat plugin. The experimental window manager and complete measurements
are described in [Background clients](Background-Clients.txt).

## Follow-up: reported slowdown on 27 October

One production run reached world-ready at 6.826 seconds. Fast-startup became active
at 4.856 seconds, versus 2.106 seconds in the earlier recorded integrated run.
Most extra delay was before fast-startup activation. The instrumentation did not
separate early client work from hook-installation time within that interval.

A subsequent lab / Nexus / Nexus / lab comparison reached the stage milestone at
3.239 / 2.587 / 2.586 / 2.972 seconds; outer harness times were
3.288 / 2.902 / 2.867 / 3.027 seconds. A normal UI follow-up reached world-ready at
2.746 seconds. The reported slowdown was therefore not consistently reproduced.
The installed and previously tested executables matched, and fast startup plus
quiet login were enabled.

Cache, I/O, and scheduling remained hypotheses, not demonstrated causes. If it
recurs, separately timestamp login-initialisation entry and hook-install completion,
then correlate CPU, I/O, and waits. Weakening integrity checks is not supported
by the isolated slow observation.

## Remaining work and provenance

- Repeat on cold starts, additional zones and clients, and different machine loads.
- Exercise failed login, disconnect/reconnect, patch mismatch, and long sessions.
- Profile the remaining content-processing work with instrumentation overhead measured.
- Evaluate prewarming with preparation time, memory, and idle CPU included.
- Test background responsiveness with real plugin workloads and zoning.

Original local sources include `client-analysis.json`, `startup-analysis.json`,
`procmon-analysis.json`, `new-location-analysis.json`,
`login-speed-investigation-2026-10-27.txt`, and the investigation conversation.
The companion JSON contains a hash inventory for these files and aggregate data
selected explicitly for publication. The original PML, CSV, process logs, HTTP
capture state, and test-account information remain local. The published summary
is sufficient to inspect the reported aggregates, but not to reproduce raw-trace
analysis without the retained local evidence.

Historical prototype recommendations are not automatically current defects:
quiet startup, native world-ready reporting, and acceptance-controlled Rebirth
sequencing were subsequently integrated. This document does not publish the
separate local 1.3.0 source preview or change application behaviour.
