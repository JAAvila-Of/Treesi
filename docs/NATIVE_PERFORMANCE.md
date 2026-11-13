# Native Windows process observations

The manual `native-performance.yml` workflow publishes a self-contained Release application and runs only the `NativePerformance` category in a fresh interactive GitHub-hosted Windows VM. Both workflow admission and the test's `IsolatedExecution.Require("native-performance")` refuse developer desktops, self-hosted runners, services and other suite modes. Normal native correctness checks explicitly exclude this category; the regular solution never launches it.

## Process ownership and startup observations

The harness creates a synthetic repository with 1,000 background files, modified/untracked probe files and a separate branch containing 10,000 imported linear commits. Setup is outside measurements. It starts three independent Treesi processes sequentially, passing the probe file directly. OS/runtime caches are not cleared, so these are new-process observations rather than verified cold-cache launches.

Each observation records:

- Launch to detection of the process's own main-window handle, polled at approximately 20 ms.
- Time spent attaching an Appium session to that exact window handle.
- Launch to the first successful UIA observation of the selected file, enabled diff search and expected read-only comparison text, polled at approximately 100 ms.
- Theme label, window DPI and the process resource samples described below.

The accessible-review time **includes** Appium attachment, HTTP/UIA queries and polling delays. It is an upper observation bound, not application-only startup or first-frame presentation. The attach duration is reported separately; subtracting it does not recover a precise render timestamp. Three launches retain individual values and do not estimate p95 or an SLO.

Sessions use [WinCore's existing-window attachment](https://github.com/appium/appium-wincore-driver/blob/v3.3.2/README.md#capabilities), never a desktop-root session. The harness knows the PID returned by its own launch and never selects processes by name. It closes that window after quitting the attached automation session, with a bounded fallback that terminates only its owned process tree. No profiling endpoint, settings change or test-only file transport is added to the product.

## Idle resources and loaded graph

After readiness, the harness allows three seconds to settle, then records eleven process-counter samples across ten 500 ms intervals. During this idle window it sends no UIA requests or input. Actual wall-clock timestamps, rather than the intended interval length, determine the CPU rate.

Reported Windows counters distinguish [working set](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.workingset64) (resident process pages, including shared pages) from private committed memory. Neither is the managed allocation rate reported by BenchmarkDotNet. Lifetime peak working set can predate the idle sampling window. The sample includes managed and native memory of Treesi itself; it excludes the test host, Appium and Git child processes. It is not an aggregate system or complete process-tree peak.

CPU is the change in the process's [total processor time](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.totalprocessortime) divided by observed wall time. `oneCoreCpuPercent` treats one continuously occupied core as 100%; `machineCpuPercent` additionally divides by logical processor count. A multithreaded process can exceed 100% on the first scale. These metrics must not be compared without checking the normalization.

On the third process, the harness opens Graph and extends it through 200, 400, 600, 800 and 1,000 commits, retaining the elapsed time from each UIA click through observed completion. It then takes another idle resource window with the 1,000-row graph loaded. These are automation-observed interaction durations, **not frame pacing, FPS or input-to-photon measurements**. The graph and working diff remain subject to their existing bounds.

## Provisional observations and artifacts

Initial investigation thresholds, chosen before measurement, are 2 seconds to observe a window, 5 seconds to observe the first comparison, 250 MiB maximum sampled working set, 300 MiB maximum sampled private committed memory and 2% idle CPU on the one-core scale. Per-run flags report these comparisons, including graph resource samples where present. A complete measurement may exceed a threshold; shared-VM timing/resource targets are not correctness gates. Timeouts, missing comparison content, invalid counters or incomplete sampling fail the measurement test.

The artifact retains `native-process-performance.json`, TRX, exact source SHA, SDK/Git/OS/CPU/display-driver/power-plan context and automation logs. JSON contains every resource sample, individual launch/action observations, provisional thresholds, failure/completion status and measurement scope. Counter aggregation is checked by regular FluentOperations tests. A passing workflow proves measurement completion, not that all threshold flags are true.

Controlled native startup, presentation-frame timestamps, long idle sessions, eight-tab retention and macOS/Linux native resource behavior remain additional acceptance work. Repeat baseline and candidate under matching power, display, caching and workload conditions before attributing a regression or improvement.

The [eight-tab and presentation-probe protocol](SESSION_PERFORMANCE.md) extends this workflow with independent session graphs, two 60-second idle windows, tab closure and a pinned per-PID ETW collector. Its resource completion and presentation-support status are evaluated separately.
