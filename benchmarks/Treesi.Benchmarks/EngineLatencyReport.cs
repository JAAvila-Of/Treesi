using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Treesi.Domain;
using Treesi.TestSupport;

namespace Treesi.Benchmarks;

internal static class EngineLatencyReport
{
    public static async Task WriteAsync(string destination)
    {
        var commit = Environment.GetEnvironmentVariable("TREESI_BENCHMARK_COMMIT");
        if (commit is null || !RevisionEndpoints.IsCommitId(commit)) throw new ArgumentException("TREESI_BENCHMARK_COMMIT must identify the exact source revision.");
        const int warmups = 5, samples = 30, commits = 10000;
        using var workload = new EngineWorkload(commits);
        var cases = new (string Name, double Target, Func<Task> Run)[]
        {
            ("open-working-review", 200, async () => { var result = await workload.OpenWorkingReview(); if (result.Hunks.Count == 0) throw new InvalidOperationException("The working diff fixture was not read."); }),
            ("history-50", 100, async () => { var result = await workload.ReadHistoryPage(); if (result.Items.Count != 50) throw new InvalidOperationException("The history fixture was not read."); }),
            ("graph-200", 150, async () => { var result = await workload.ReadGraph200(); if (result.Commits.Count != 200 || !result.HasMore) throw new InvalidOperationException("The graph fixture was not read."); }),
            ("graph-1000", 300, async () => { var result = await workload.ReadGraph1000(); if (result.Commits.Count != 1000 || !result.HasMore) throw new InvalidOperationException("The graph fixture was not read."); }),
            ("pinned-revision-diff", 100, async () => { var result = await workload.ReadPinnedRevisionDiff(); if (result.Hunks.Count == 0) throw new InvalidOperationException("The revision fixture was not read."); }),
            ("worktrees-2", 100, async () => { var result = await workload.ListWorktrees(); if (result.Items.Count != 2) throw new InvalidOperationException("The worktree fixture was not read."); })
        };
        var rows = new List<object>();
        foreach (var scenario in cases)
        {
            for (var i = 0; i < warmups; i++) await scenario.Run();
            var durations = new double[samples];
            for (var i = 0; i < samples; i++)
            {
                var start = Stopwatch.GetTimestamp(); await scenario.Run(); durations[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            var summary = LatencySummary.From(durations);
            rows.Add(new { operation = scenario.Name, targetP95Ms = scenario.Target, withinTarget = summary.P95Ms <= scenario.Target, summary, samplesMs = durations });
            Console.WriteLine($"{scenario.Name}: median {summary.MedianMs:F2} ms; p95 {summary.P95Ms:F2} ms; target {scenario.Target:F0} ms");
        }
        var report = new
        {
            schemaVersion = "1", sourceCommit = commit, measuredAtUtc = DateTimeOffset.UtcNow,
            scope = "Sequential warm engine operations including Git child processes; no native UI or process startup measurement.",
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(), processors = Environment.ProcessorCount,
            fixture = new { backgroundFiles = 1000, importedHistoryCommits = commits, worktrees = 2 },
            warmupsPerOperation = warmups, measuredSamplesPerOperation = samples, percentile = "nearest-rank", targets = "provisional-reference-host-observations-not-a-shared-CI-gate", results = rows
        };
        var full = Path.GetFullPath(destination); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }
}
