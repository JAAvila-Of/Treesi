using Treesi.Application;
using Treesi.Domain;
using Treesi.Git;
using Treesi.TestSupport;

namespace Treesi.Benchmarks;

public sealed class EngineWorkload : IDisposable
{
    private readonly SyntheticRepository _repository = new(1000);
    private readonly IInspectionService _working = new InspectionService(new GitInspector());
    private readonly IHistoryService _history = new HistoryService(new GitInspector());
    private readonly IGraphService _graphs = new GraphService(new GitInspector());
    private readonly IRevisionService _revisions = new RevisionService(new GitInspector());
    private readonly IWorktreeService _worktrees = new WorktreeService(new GitInspector());
    private readonly string _tip;
    private readonly RevisionEndpoints _endpoints;

    public EngineWorkload(int commits)
    {
        try
        {
            _tip = _repository.AddLinearHistory(commits);
            _repository.AddLinkedWorktree();
            _endpoints = _revisions.ResolveAsync(_repository.Root, new(_tip + "~1", _tip)).GetAwaiter().GetResult();
        }
        catch { _repository.Dispose(); throw; }
    }

    public async Task<FileDiff> OpenWorkingReview()
    {
        var snapshot = await _working.InspectAsync(_repository.Root);
        return await _working.DiffAsync(snapshot.Root, "sample.txt", Comparison.WorkingTree);
    }
    public Task<HistoryPage> ReadHistoryPage() => _history.PageAsync(_repository.Root, new(_tip, Limit: 50));
    public Task<CommitGraphPage> ReadGraph200() => _graphs.ReadAsync(_repository.Root, new(Limit: 200));
    public Task<CommitGraphPage> ReadGraph1000() => _graphs.ReadAsync(_repository.Root, new(Limit: 1000));
    public Task<FileDiff> ReadPinnedRevisionDiff() => _revisions.DiffAsync(_endpoints, "sample.txt");
    public Task<RepositoryWorktrees> ListWorktrees() => _worktrees.ListAsync(_repository.Root);
    public void Dispose() => _repository.Dispose();
}
