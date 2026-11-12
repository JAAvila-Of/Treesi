# Test execution and isolation

Treesi separates normal verification, native desktop automation, and performance measurements. Running the regular solution never starts the native UI suite or benchmarks.

| Layer | Entry point | Execution environment |
| --- | --- | --- |
| Domain, application, Git, CLI, architecture, isolation policy | `Treesi.slnx` | Local terminal or hosted CI |
| Theme, layout, search, filtering, rendered diff checks | `Treesi.slnx` | Avalonia Headless, in memory |
| Real Windows accessibility and interactions | `Treesi.Native.Tests` | Explicitly enabled interactive GitHub-hosted Windows VM |
| Git inspection and selected diff benchmarks | `Treesi.Benchmarks` | Hosted smoke job or a configured macOS performance host |

## Automatic verification

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

The workflow publishes Treesi, installs pinned Appium tools only in the disposable VM, binds the server to loopback, and exercises a generated Git fixture. Native checks cover direct file opening, accessibility IDs, change counts, Light/Dark selection, filtering, comparison changes, and search. The session closes its own application. Failure diagnostics contain only the synthetic fixture and accessible workspace.

Appium 3.9.0 is paired with WinCore 3.3.2. The newer WinCore 4 line requires Appium 4, so installing the newest driver major with the stable Appium 3 server would be incompatible. The tool package lock records the dependency graph. The .NET Appium client is 9.0.0. These automation components are Apache-2.0 licensed.

The isolated solution is intentionally separate:

```sh
dotnet restore Treesi.Isolated.slnx --locked-mode
dotnet build Treesi.Isolated.slnx --configuration Release --no-restore
```

Do not run native UI tests on a personal desktop, attach to a desktop-root session, or register a persistent public-repository runner on a workstation. The current native suite targets Windows; native macOS/Linux integration and assistive-technology testing remain additional coverage.

## Performance measurements

BenchmarkDotNet 0.15.8 measures working-tree inspection and selected-file diffs using generated repositories of 100 and 1,000 background files plus probe files. Setup and fixture creation are outside the timed benchmark. Shared dependencies are compiled in Release in the isolated solution. The harness selects InProcessNoEmit for .NET 11 compatibility. Cases share a process, so these results do not measure application startup or process isolation.

`benchmarks.yml` is manual and uses a hosted Linux VM with a Dry job to validate the benchmark harness. Its results are smoke measurements, not a stable product baseline. For longer measurements, use a configured performance host. Explicit `TREESI_ISOLATED_SUITE=benchmarks` is required. `TREESI_BENCHMARK_PROFILE` selects `short` or `dry`; do not add `--job` overrides that replace the configured toolchain.

The reference-host results still depend on power state, thermal conditions, background workload, Git version, SDK, and caches. Compare runs only with matching conditions. Do not turn a successful benchmark process into a claim that product latency or memory budgets have been achieved. Startup, idle CPU, full desktop memory, large-repository stress, and frame responsiveness are not covered by these initial microbenchmarks.

## Reports and scope

TRX reports identify passed, failed, and skipped tests. CI failures and unavailable native sessions remain failures. PNG files are render diagnostics, not human-approved visual baselines. Benchmarks are reported separately from correctness checks. No suite uses real customer repositories or sends their contents to an external analyzer.

Sources: [Avalonia Headless](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform), [Appium WinCore](https://github.com/appium/appium-wincore-driver), [GitHub-hosted runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners), [BenchmarkDotNet](https://benchmarkdotnet.org/articles/overview.html).
