using Treesi.Domain;

namespace Treesi.Application;

public interface IGraphReader
{
    Task<CommitGraphPage> ReadGraphAsync(string repository, GraphQuery query, CancellationToken cancellationToken = default);
}
public interface IGraphService
{
    Task<CommitGraphPage> ReadAsync(string repository, GraphQuery query, CancellationToken cancellationToken = default);
}
public sealed class GraphService(IGraphReader reader) : IGraphService
{
    public async Task<CommitGraphPage> ReadAsync(string repository, GraphQuery query, CancellationToken cancellationToken = default)
    {
        query.Validate(repository);
        var page = await reader.ReadGraphAsync(repository, query, cancellationToken);
        (query with { Scope = page.Scope }).Validate(repository);
        var ids = page.Commits.Select(item => item.CommitId).ToArray();
        if (page.ContentTrust != "untrusted" || page.Scope.Tips.Count == 0 && ids.Length != 0
            || page.Limit != query.Limit || ids.Length > query.Limit || page.HasMore && ids.Length != query.Limit
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length
            || page.Commits.Any(item => !RevisionEndpoints.IsCommitId(item.CommitId) || item.Parents.Count > GraphQuery.MaximumParents
                || item.Parents.Any(id => !RevisionEndpoints.IsCommitId(id) || id == item.CommitId) || item.Parents.Distinct(StringComparer.Ordinal).Count() != item.Parents.Count)
            || query.Scope is { } pinned && (page.Scope.Root != pinned.Root || !page.Scope.Tips.SequenceEqual(pinned.Tips) || !page.Scope.References.SequenceEqual(pinned.References) || page.Scope.ReferencesLimited != pinned.ReferencesLimited))
            throw new InspectionException("invalid_graph", "The provider returned an invalid or different graph snapshot.");
        var positions = ids.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
        if (page.Commits.SelectMany((item, index) => item.Parents.Select(parent => (parent, index))).Any(edge => positions.TryGetValue(edge.parent, out var parent) && parent <= edge.index))
            throw new InspectionException("invalid_graph", "The provider did not return child-before-parent topology.");
        return page;
    }
}
public sealed class InstrumentedGraphService(IGraphService inner) : IGraphService
{
    public Task<CommitGraphPage> ReadAsync(string repository, GraphQuery query, CancellationToken cancellationToken = default) =>
        InstrumentedInspectionService.MeasureAsync("graph", () => inner.ReadAsync(repository, query, cancellationToken));
}
