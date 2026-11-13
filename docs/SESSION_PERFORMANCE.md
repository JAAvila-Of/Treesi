# Eight-tab retention and presentation probe

The isolated Windows performance workflow additionally exercises eight worktrees in one owned Treesi process. Each working folder contains 1,000 background files and its own modified probe; all share a repository with 10,000 imported linear commits. Every tab explicitly loads a 1,000-commit graph. Fixture setup is outside measurement, and no user repository participates.

The report `native-session-performance.json` retains four resource phases:

| Phase | Open tabs | Idle sampling |
| --- | ---: | --- |
| Initial loaded graph | 1 | 10 intervals of 500 ms |
| All graphs loaded | 8 | 10 intervals of 500 ms |
| After two complete tab cycles and capture workload | 8 | 60 intervals of 1 second |
| After closing seven tabs | 1 | 60 intervals of 1 second |

Every phase first settles for three seconds. No input or UIA queries are sent during idle sampling. The harness verifies that cycling reaches eight distinct repositories and restores their loaded graph views, then closes tabs through the product's normal Ctrl+W handling. It never forces garbage collection, trims the working set or changes runtime settings. Resident/private memory changes alone neither prove a leak nor establish that a particular object remains reachable; allocators can retain pages after tabs close.

Provisional observation thresholds are 512 MiB sampled working set, 600 MiB private committed memory and 2% idle CPU on the one-core scale. Flags are reported per phase, without becoming shared-runner correctness gates. Existing process ownership, admission, counter definitions and cleanup rules follow the [native process protocol](NATIVE_PERFORMANCE.md). The measurement excludes aggregate Git-child peaks, and two 60-second windows are not a long-duration soak test.

## Presentation capture

The workflow downloads only the standalone [PresentMon 2.6.0 console application](https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0). `eng/performance-tools/presentmon.json` pins its source and SHA-256 release digest. PresentMon is MIT-licensed; upstream license and third-party notices are retained beside results. No installer, service, overlay or product runtime dependency is added.

Capture uses a unique ETW session name, targets only the PID returned by the owned application launch and terminates after 20 seconds. It never stops existing trace sessions, requests an administrator restart or forces another rendering backend. During capture, the active graph receives 180 Down keys with requested 50 ms pauses through one native action sequence. The collector and application are closed only by their owning harness.

The [v1 CSV metrics](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/README-ConsoleApplication.md) retain dropped presents and per-swapchain present/display-change intervals. The parser requires the owned PID, rejects malformed or oversized data (16 MiB/10,000 rows), groups swapchains separately and preserves missing display evidence. Empty captures, collector failures and presents without display intervals have distinct statuses. They do not pass presentation acceptance merely because session/resource sampling completed. Raw CSV, stdout/stderr, pinned tool manifest and license notices are retained for inspection.

Present/display intervals include time between input-driven updates and legitimate idle gaps. They are not render duration, input-to-photon latency or a fixed-rate FPS target. The report does not delete long gaps to manufacture a smoother percentile. Even available events from a virtual display are not evidence of behavior on a user's physical monitor. A backend or VM without supported events leaves presentation timing **unverified**; the successful resource measurement does not replace those missing data.
