using BenchmarkDotNet.Attributes;
using Treesi.Domain;

namespace Treesi.Benchmarks;

[MemoryDiagnoser]
public class EngineReviewBenchmarks
{
    [Params(1000, 10000)]
    public int HistoryCommits { get; set; }
    private EngineWorkload _workload = null!;
    [GlobalSetup] public void Setup() => _workload = new(HistoryCommits);
    [Benchmark] public Task<FileDiff> OpenWorkingReview() => _workload.OpenWorkingReview();
    [Benchmark] public Task<HistoryPage> ReadHistoryPage() => _workload.ReadHistoryPage();
    [Benchmark] public Task<CommitGraphPage> ReadGraph200() => _workload.ReadGraph200();
    [Benchmark] public Task<CommitGraphPage> ReadGraph1000() => _workload.ReadGraph1000();
    [Benchmark] public Task<FileDiff> ReadPinnedRevisionDiff() => _workload.ReadPinnedRevisionDiff();
    [Benchmark] public Task<RepositoryWorktrees> ListWorktrees() => _workload.ListWorktrees();
    [GlobalCleanup] public void Cleanup() => _workload.Dispose();
}
