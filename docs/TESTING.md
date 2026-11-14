# Test execution and isolation

The [commit graph](COMMIT_GRAPH.md) has synthetic Git, headless desktop and isolated native Windows coverage. Headless checks exercise compact geometry, virtualized row creation, merge lanes, snapshot extension, tab state and late-result rejection. They do not open native windows. The native graph scenario searches an initial commit, reviews its full diff and returns to the retained selection.

Treesi separates normal verification, native desktop automation, and performance measurements. Running the regular solution never starts the native UI suite or benchmarks.

| Layer | Entry point | Execution environment |
| --- | --- | --- |
| Domain, application, Git, CLI, architecture, isolation policy | `Treesi.slnx` | Local terminal or hosted CI |
| Theme, layout, search, filtering, rendered diff checks | `Treesi.slnx` | Avalonia Headless, in memory |
| Real Windows accessibility and interactions | `Treesi.Native.Tests` | Explicitly enabled interactive GitHub-hosted Windows VM |
| Linux desktop-entry native Home startup | `LinuxNative` category, `native-linux.yml` | Explicitly enabled disposable hosted Linux VM with an owned Xvfb display |
| Observed Windows launch and idle process resources | `NativePerformance` category, manual `native-performance.yml` | Separate interactive GitHub-hosted Windows VM with its own opt-in |
| Git inspection and selected diff benchmarks | `Treesi.Benchmarks` | Hosted smoke job or a configured macOS performance host |

## Automatic verification

Repository-session checks exercise independent selection/options/search/layout/scroll state, cancellation and uncooperative late results, the eight-tab bound, close/Home behavior, keyboard cycling and pinned historical context without re-reading Git. The native isolated suite additionally checks tab accessibility, returning from Home and closing the final tab without deleting fixture files. See [session semantics and limits](REPOSITORY_SESSIONS.md).

Accessibility tests drive real headless keyboard events and query automation peers for read-only document values, rejected writes, region traversal, contained panel focus, live metadata, Unicode previews, visible layout focus and bidirectional search. Native Windows coverage queries UIA values/read-only state and exercises F6/Tab/Escape. See [coverage and manual acceptance still pending](ACCESSIBILITY.md).

Full-context tests cover working/staged/HEAD comparisons, fixed SHA-1/SHA-256 revisions, initial commits, file edges, non-mutation, CLI support and output-limit refusal. Headless coverage checks both themes/layouts, source anchors, separate edit navigation, draft/focus behavior, transactional failure/cancellation and superseded results. The native Windows suite expands/contracts through accessibility and verifies search remains available.

Source presentation adds finite-lexer and Unicode range tests, budget/fallback cases, and headless render checks in unified/split layouts for both themes. Checks inspect real token runs and rendered inline-background pixels, verify contrast, preserve text/selection/search, and ensure local display changes do not query Git. Native Windows coverage exercises the display toggles through accessibility in the disposable VM.

History coverage includes pinned pagination, SHA-1/SHA-256 initial commits, deleted literal paths, distinct merge-parent comparisons, shallow boundaries, invalid requests, index/object-store non-mutation and CLI results. Headless tests cover both themes at compact sizes, parent selection, file scope, bounded message previews, recovery and late-result cancellation. The isolated Windows suite also selects historical/initial commits through accessibility and returns to working changes.

`ci.yml` runs on pushes, pull requests, and manual dispatch. It restores locked dependencies, builds Release, runs the regular suites, checks formatting, and compiles the isolated suites without executing them on Windows, macOS, and Linux. It retains TRX results and synthetic headless renders for three days. Newer runs cancel superseded verification runs.

```sh
dotnet restore Treesi.slnx --locked-mode
dotnet build Treesi.slnx --configuration Release --no-restore
dotnet test Treesi.slnx --configuration Release --no-build
```

## Native Windows verification

`native-ui.yml` uses a fresh standard Windows VM on pushes to main or manual dispatch. Admission checks require a GitHub-hosted interactive Windows session. The suite repeats the guard before creating any driver session. A developer desktop, self-hosted runner, missing opt-in, or Windows service session fails rather than skipping tests or reporting success.

The correctness workflow filters out `NativePerformance`. That category runs only through the separate manual workflow with `TREESI_ISOLATED_SUITE=native-performance`. It records three owned-process launches and idle/graph resource samples; see [native observation scope](NATIVE_PERFORMANCE.md). The two modes cannot substitute for each other's opt-in. A native test command without a category filter includes the performance test and therefore fails admission in correctness mode; use the documented workflows.

The same manual category also exercises [eight-tab retention and a per-PID presentation probe](SESSION_PERFORMANCE.md). Resource phases retain all samples; a missing/unsupported ETW capture remains explicit unavailable presentation evidence. Passing that test's resource/session protocol is not a frame-pacing certification.

The workflow publishes Treesi, installs pinned Appium tools only in the disposable VM, binds the server to loopback, and exercises a generated Git fixture. Native checks cover direct file opening, accessibility IDs, change counts, Light/Dark selection, filtering, comparison changes, and search. The session closes its own application. Failure diagnostics contain only the synthetic fixture and accessible workspace.

Appium 3.9.0 is paired with WinCore 3.3.2. The newer WinCore 4 line requires Appium 4, so installing the newest driver major with the stable Appium 3 server would be incompatible. The tool package lock records the dependency graph. The .NET Appium client is 9.0.0. These automation components are Apache-2.0 licensed.

The isolated solution is intentionally separate:

```sh
dotnet restore Treesi.Isolated.slnx --locked-mode
dotnet build Treesi.Isolated.slnx --configuration Release --no-restore
```

Do not run native UI tests on a personal desktop, attach to a desktop-root session, or register a persistent public-repository runner on a workstation. The separate [Linux native lab](LINUX_NATIVE_LAB.md) covers desktop-entry Home launch on an owned virtual X11 display. Windows filters exclude `LinuxNative`; Linux runs only that category. Native macOS, full Linux interactions and assistive-technology testing remain additional coverage.

## Performance measurements

BenchmarkDotNet 0.15.8 measures working-tree inspection and selected-file diffs using generated repositories of 100 and 1,000 background files plus probe files. Engine review cases additionally use 1,000/10,000 imported commits, two worktrees, fixed revision diffs and 200/1,000-commit graph windows. Setup and fixture creation are outside the timed benchmark. Shared dependencies are compiled in Release in the isolated solution. The harness selects InProcessNoEmit for .NET 11 compatibility. Cases share a process, so these results do not measure application startup or process isolation. See [performance protocol and provisional engine targets](PERFORMANCE.md).

`benchmarks.yml` is manual and uses a hosted Linux VM with a Dry job to validate all benchmark cases, followed by a warm latency report with 30 samples per operation. Its results are smoke measurements, not a stable product baseline. For longer measurements, use a configured performance host. Explicit `TREESI_ISOLATED_SUITE=benchmarks` is required. `TREESI_BENCHMARK_PROFILE` selects `short` or `dry`; do not add `--job` overrides that replace the configured toolchain. `TREESI_BENCHMARK_COMMIT` must identify the source SHA for latency reports. Failed/empty BenchmarkDotNet measurements fail the harness; latency targets are reported separately without becoming shared-runner gates.

The reference-host results still depend on power state, thermal conditions, background workload, Git version, SDK, and caches. Compare runs only with matching conditions. Do not turn a successful benchmark process into a claim that product latency or memory budgets have been achieved. Startup, idle CPU, full desktop memory, large-repository stress, and frame responsiveness are not covered by these initial microbenchmarks.

## Reports and scope

TRX reports identify passed, failed, and skipped tests. CI failures and unavailable native sessions remain failures. PNG files are render diagnostics, not human-approved visual baselines. Benchmarks are reported separately from correctness checks. No suite uses real customer repositories or sends their contents to an external analyzer.

Sources: [Avalonia Headless](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform), [Appium WinCore](https://github.com/appium/appium-wincore-driver), [GitHub-hosted runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners), [BenchmarkDotNet](https://benchmarkdotnet.org/articles/overview.html).
