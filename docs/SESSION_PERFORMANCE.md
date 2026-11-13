# Eight-tab retention and presentation probe

The isolated Windows performance workflow additionally exercises eight worktrees in one owned Treesi process. Each working folder contains 1,000 background files and its own modified probe; all share a repository with 10,000 imported linear commits. Every tab explicitly loads a 1,000-commit graph. Fixture setup is outside measurement, and no user repository participates.

The report `native-session-performance.json` (schema version 5) retains five resource phases:

| Phase | Open tabs | Idle sampling |
| --- | ---: | --- |
| Initial loaded graph | 1 | 10 intervals of 500 ms |
| All graphs loaded | 8 | 10 intervals of 500 ms |
| After two complete tab cycles and capture workload | 8 | 60 intervals of 1 second |
| After closing seven tabs, search input focused | 1 | 60 intervals of 1 second |
| Same remaining tab, graph focused | 1 | 60 intervals of 1 second |

Every phase first settles for three seconds. No input or UIA queries are sent during idle sampling. The harness verifies that cycling reaches eight distinct repositories and restores their loaded graph views, then closes tabs through the product's normal Ctrl+W handling. It never forces garbage collection, trims the working set or changes runtime settings. Resident/private memory changes alone neither prove a leak nor establish that a particular object remains reachable; allocators can retain pages after tabs close.

The first post-close phase preserves focus in the graph search input left by Ctrl+W navigation. The final phase focuses the graph in the same process, then repeats the 60-second observation. Both results remain in the report. This separates two user-visible idle states without disabling the caret, animations or runtime behavior. It does not isolate every source of CPU activity or establish causation from one run.

Provisional observation thresholds are 512 MiB sampled working set, 600 MiB private committed memory and 2% idle CPU on the one-core scale. Flags are reported per phase, without becoming shared-runner correctness gates. Existing process ownership, admission, counter definitions and cleanup rules follow the [native process protocol](NATIVE_PERFORMANCE.md). The measurement excludes aggregate Git-child peaks, and three 60-second windows are not a long-duration soak test.

## Per-thread CPU attribution

Each resource phase reads the owned process's threads immediately before and after the existing process-counter sampling loop. Snapshots retain read start/end times, per-thread observation timestamps, creation time, user/kernel CPU counters and optional descriptions. Read-only thread handles use `THREAD_QUERY_LIMITED_INFORMATION`; the owning PID is checked on the same handle before counters or descriptions are read. Handles and description buffers are released. Snapshots are bounded to 4,096 threads and descriptions to 128 characters. No thread is suspended, injected into or modified.

Only pairs with the same OS thread ID **and creation time** produce deltas. Each pair uses its measured wall interval. New, ended/reused and unreadable thread IDs remain explicit; their CPU is not guessed. Endpoint reads are not simultaneous, can miss short-lived threads, and surround rather than exactly coincide with the process-counter interval. Summed matched thread CPU need not equal process CPU. Empty descriptions and inaccessible/exited threads do not establish zero activity. These observations identify candidate threads, not native functions or presentation timing.

The two managed-profile collections also retain thread snapshots, separately from unprofiled measurements. Their counters include collector attachment/rundown effects and must not be used as unprofiled budget results. Match OS thread IDs to the raw trace from the **same owned process and run**, checking identity and thread churn before assigning a role. Thread names alone are not attribution evidence. See [GetThreadTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getthreadtimes) and [GetThreadDescription](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getthreaddescription).

## Managed stack profiles

After all five unprofiled resource phases, the same owned process receives two additional 30-second EventPipe collections: first with the graph search input focused, then with the graph focused. Each settles for three seconds. No input or UIA queries occur during collection. These are diagnostic captures, excluded from resource thresholds and comparisons because the profiler changes the workload.

The VM installs [dotnet-trace 10.0.750501](https://github.com/dotnet/diagnostics/releases/tag/v10.0.750501) from a SHA-512-verified NuGet package declared in `eng/performance-tools/dotnet-trace.json`. The package is the only installation source; no global tool, service or product dependency is added. Its MIT license and packaged third-party notices accompany results. Each command owns its process, has a 120-second timeout and retains stdout/stderr. Collection uses `dotnet-sampled-thread-time`, a 16 MiB dropping buffer and a 30-second duration; completed trace files above 64 MiB are rejected. No heap dump, forced GC, native profiler or machine-wide trace is requested.

Raw `.nettrace` files and inclusive/exclusive top-40 reports are retained separately by focus state. [Managed thread sampling](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace) estimates sampled thread time: waits and native boundaries can appear in stacks. Report percentages are **not process CPU percentages**. These traces do not resolve native rendering stacks or measure presentation, input latency or FPS. Compare the stacks with the preceding unprofiled process counters before attributing a cause; a periodic caret callback alone does not establish where rendering cost occurs.

## Native stacks in the disposable VM

After the managed profiles, two additional 20-second captures observe search and graph focus. This mode requires `TREESI_NATIVE_CAPTURE_SCOPE=disposable-vm-pid-export` in addition to the hosted-VM admission guard. The caller supplies the exact owned PID and process creation time; the exporter also checks the expected application executable. No process is discovered by name.

The [capability probe](NATIVE_PROVIDER_PROBE.md) found that the source PID filter must not be assumed effective. This is a **different, explicit mode**: a unique temporary VM-wide kernel sampling session with no legacy global keywords, no sampling-interval change and no restart of other sessions. Only the sampler's already-assigned profiling privilege is enabled temporarily in its own token. It never runs on a developer desktop, SSH benchmark host or self-hosted runner.

Raw ETL/ETLX files stay in a generated temporary directory outside artifact paths. The exporter accepts only thread identities present with the same ID and creation time at both endpoints in the owned process; a nonnegative conflicting PID is rejected. Other samples are excluded, with only an aggregate exclusion count retained. Kernel ISR/DPC samples are not charged to the target. The entire raw trace is deleted **before** symbol lookup, and again during cleanup; a killed sampler can leave transient files only in the disposable VM, outside uploads. Conversion output is suppressed so unrelated trace details do not enter logs. The report retains only target stacks, module basenames/versions and sample counts.

Bounds are 64 MiB raw files, 4,096 threads, 2,048 distinct target stacks and 64 frames per stack. Hitting a stack bound or losing events is explicit and fails the capture. The harness rejects exports above 4 MiB and requires search-focus user-stack evidence. Graph focus may produce no target samples while idle; that status does not prove zero CPU or presentation acceptance.

Symbol lookup occurs after capture and raw deletion, only for the top five leaf modules. It uses the MIT TraceEvent symbol reader and Microsoft's public HTTPS symbol store, with a five-second request timeout, no NGen generation, no source download and no acceptance of PDBs from unsafe local probe locations. Unavailable symbols remain module-relative offsets; questionable symbol matches retain their marker. PDB caches are not uploaded. Sample counts and instrumented captures are not unprofiled CPU percentages, fixed-rate FPS or input latency.

## Presentation capture

The workflow downloads only the standalone [PresentMon 2.6.0 console application](https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0). `eng/performance-tools/presentmon.json` pins its source and SHA-256 release digest. PresentMon is MIT-licensed; upstream license and third-party notices are retained beside results. No installer, service, overlay or product runtime dependency is added.

Capture uses a unique ETW session name, targets only the PID returned by the owned application launch and terminates after 20 seconds. It never stops existing trace sessions, requests an administrator restart or forces another rendering backend. During capture, the active graph receives 180 Down keys with requested 50 ms pauses through one native action sequence. The collector and application are closed only by their owning harness.

The [v1 CSV metrics](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/README-ConsoleApplication.md) retain dropped presents and per-swapchain present/display-change intervals. The parser requires the owned PID, rejects malformed or oversized data (16 MiB/10,000 rows), groups swapchains separately and preserves missing display evidence. Empty captures, collector failures and presents without display intervals have distinct statuses. They do not pass presentation acceptance merely because session/resource sampling completed. Raw CSV, stdout/stderr, pinned tool manifest and license notices are retained for inspection.

Present/display intervals include time between input-driven updates and legitimate idle gaps. They are not render duration, input-to-photon latency or a fixed-rate FPS target. The report does not delete long gaps to manufacture a smoother percentile. Even available events from a virtual display are not evidence of behavior on a user's physical monitor. A backend or VM without supported events leaves presentation timing **unverified**; the successful resource measurement does not replace those missing data.
