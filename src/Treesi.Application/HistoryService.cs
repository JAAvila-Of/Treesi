using Treesi.Domain;

namespace Treesi.Application;

public interface IHistoryService
{
    Task<HistoryPage> PageAsync(string repository, HistoryQuery query, CancellationToken cancellationToken = default);
    Task<CommitDetail> CommitAsync(string repository, string revision, CancellationToken cancellationToken = default);
}

public interface IHistoryReader
{
    Task<HistoryPage> PageAsync(string repository, HistoryQuery query, CancellationToken cancellationToken = default);
    Task<CommitDetail> CommitAsync(string repository, string revision, CancellationToken cancellationToken = default);
}

public sealed class HistoryService(IHistoryReader reader) : IHistoryService
{
    public async Task<HistoryPage> PageAsync(string repository, HistoryQuery query, CancellationToken cancellationToken = default)
    {
        query.Validate(repository);
        var page = await reader.PageAsync(repository, query, cancellationToken);
        if (!System.IO.Path.IsPathFullyQualified(page.Root) || !RevisionEndpoints.IsCommitId(page.StartCommitId)
            || (RevisionEndpoints.IsCommitId(query.Start) && page.StartCommitId != query.Start)
            || page.Path != query.Path || page.Offset != query.Offset || page.Limit != query.Limit || page.Items.Count > query.Limit
            || page.Items.Any(item => !RevisionEndpoints.IsCommitId(item.CommitId))
            || (page.NextOffset is not null && (page.Items.Count != query.Limit || page.NextOffset != query.Offset + query.Limit || page.NextOffset > HistoryQuery.MaximumOffset)))
            throw new InspectionException("invalid_history_page", "The provider returned a different or invalid history scope.");
        return page;
    }

    public async Task<CommitDetail> CommitAsync(string repository, string revision, CancellationToken cancellationToken = default)
    {
        new HistoryQuery(revision).Validate(repository);
        var detail = await reader.CommitAsync(repository, revision, cancellationToken);
        if (!System.IO.Path.IsPathFullyQualified(detail.Root) || !RevisionEndpoints.IsCommitId(detail.CommitId)
            || (RevisionEndpoints.IsCommitId(revision) && detail.CommitId != revision)
            || detail.Parents.Any(parent => !RevisionEndpoints.IsCommitId(parent)))
            throw new InspectionException("invalid_commit", "The provider returned invalid commit metadata.");
        return detail;
    }
}

public sealed class InstrumentedHistoryService(IHistoryService inner) : IHistoryService
{
    public Task<HistoryPage> PageAsync(string repository, HistoryQuery query, CancellationToken cancellationToken = default) =>
        InstrumentedInspectionService.MeasureAsync("history", () => inner.PageAsync(repository, query, cancellationToken));
    public Task<CommitDetail> CommitAsync(string repository, string revision, CancellationToken cancellationToken = default) =>
        InstrumentedInspectionService.MeasureAsync("commit", () => inner.CommitAsync(repository, revision, cancellationToken));
}
