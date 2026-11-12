using BenchmarkDotNet.Attributes;
using Treesi.Application;
using Treesi.Domain;
using Treesi.Git;
using Treesi.TestSupport;

namespace Treesi.Benchmarks;

[MemoryDiagnoser]
public class GitInspectionBenchmarks
{
    [Params(100, 1000)]
    public int FileCount { get; set; }
    private SyntheticRepository _repository = null!;
    private IInspectionService _service = null!;

    [GlobalSetup]
    public void Setup()
    {
        _repository = new SyntheticRepository(FileCount);
        _service = new InspectionService(new GitInspector());
    }

    [Benchmark]
    public Task<RepositorySnapshot> InspectWorkingTree() => _service.InspectAsync(_repository.Root);

    [Benchmark]
    public Task<FileDiff> ReadSelectedDiff() => _service.DiffAsync(_repository.Root, "sample.txt", Comparison.WorkingTree);

    [GlobalCleanup]
    public void Cleanup() => _repository.Dispose();
}
