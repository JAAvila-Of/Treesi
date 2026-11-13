# Engine performance protocol

Performance results have three distinct layers: synthetic engine operations, headless layout correctness and native desktop behavior. This protocol measures the first. It does not establish native startup, frame pacing, idle CPU, application working set or end-to-end input latency.

## Workloads

`GitInspectionBenchmarks` retains inspection and selected-file diff cases with 100/1,000 background files. `EngineReviewBenchmarks` adds six operations with 1,000 background files, 1,000 or 10,000 imported linear commits and two registered worktrees:

| Operation | Timed work |
| --- | --- |
| Open working review | Inspect the working tree, then read `sample.txt`'s working diff |
| History page | Read 50 entries from a fixed commit ID |
| Graph 200 / 1,000 | Capture local reference tips and query a bounded topological window |
| Pinned revision diff | Read the selected file using endpoints resolved during setup |
| Worktrees | Read registered worktree metadata |

Fixtures are generated only in owned temporary directories. History import uses [Git fast-import](https://git-scm.com/docs/git-fast-import). Setup, endpoint resolution and cleanup are outside timed operations. The current working tree stays on its initial commit with modified/untracked probe files; imported history lives on a separate branch. A linked worktree has its own probe changes. The history is linear; merge-lane rendering, large binary blobs and pathological repositories are separate workloads.

BenchmarkDotNet Short runs provide exploratory means and managed allocations; Dry only checks harness execution. InProcessNoEmit is retained for the current SDK compatibility. Operations share a process and warmed OS/runtime caches. [MemoryDiagnoser](https://benchmarkdotnet.org/articles/configs/diagnosers.html) reports managed allocation activity, not native desktop memory or the memory of Git child processes. Benchmark failures or absent measurements fail the harness. Priority elevation warnings are retained and never bypassed.

## Warm latency samples and provisional targets

`--latency-report <output.json>` performs five warmups followed by 30 sequential measured requests per operation on the 10,000-commit fixture. The report records every sample, mean, median, minimum, maximum and empirical p95 using nearest rank (29th sorted sample). It also records the source SHA, runtime, OS, architecture, fixture shape and targets. Timing includes validation, Git processes, parsing and small result checks. It excludes fixture generation, JSON serialization and UI presentation.

These initial engine targets were selected before collecting the expanded reference-host results:

| Warm operation | Provisional p95 target |
| --- | ---: |
| Open working review | 200 ms |
| History, 50 entries | 100 ms |
| Graph, 200 commits | 150 ms |
| Graph, 1,000 commits | 300 ms |
| Pinned revision diff | 100 ms |
| Two worktrees | 100 ms |

Targets are reference-host investigation thresholds, not universal product guarantees. The report marks `withinTarget` per operation; exceeding a target remains a recorded result rather than a correctness failure. Shared hosted runners do not enforce these targets. Thirty samples offer an initial observation, not a stable tail-latency estimate or evidence of a long-term SLO. Short-run confidence intervals, thermal throttling, power mode and competing workloads must be considered before changing code or targets.

## Repeatable execution

Use a configured isolated macOS host or a disposable hosted runner; never run benchmark workloads on the main user desktop. Compile Release from an exact committed revision, restore locked dependencies, retain environment information (Git/SDK/CPU/power context), and avoid concurrent workload or verification suites on the same host. Do not compare timings across different machines as a regression percentage. A useful comparison repeats both baseline and candidate under matching conditions and examines raw samples and allocation reports.

```sh
export TREESI_ISOLATED_SUITE=benchmarks
export TREESI_BENCHMARK_PROFILE=short
export TREESI_BENCHMARK_COMMIT=<full-source-commit-id>
dotnet run --project benchmarks/Treesi.Benchmarks -c Release --no-build -- --filter '*Benchmarks*' --exporters json --artifacts /path/to/results
dotnet run --project benchmarks/Treesi.Benchmarks -c Release --no-build -- --latency-report /path/to/results/engine-latency.json
```

`benchmarks.yml` manually exercises all cases with Dry and records warm latency samples on a hosted Linux VM. Its artifacts validate execution and report shape; they do not replace a controlled reference host. Results and machine-specific operational details remain lab material. This document defines the reusable method rather than claiming unmeasured desktop performance.
