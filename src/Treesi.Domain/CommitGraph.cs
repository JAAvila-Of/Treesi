namespace Treesi.Domain;

public sealed record GraphReference(string Name, string CommitId, string Kind);
public sealed record GraphScope(string Root, IReadOnlyList<string> Tips, IReadOnlyList<GraphReference> References,
    string? Reference = null, bool ReferencesLimited = false);
public sealed record GraphCommit(string CommitId, IReadOnlyList<string> Parents, string Subject, string Author, string AuthoredAt);
public sealed record CommitGraphPage(GraphScope Scope, IReadOnlyList<GraphCommit> Commits, int Limit, bool HasMore,
    bool IsShallow, string ContentTrust = "untrusted");

public sealed record GraphQuery(string? Reference = null, int Limit = 200, GraphScope? Scope = null)
{
    public const int MaximumCommits = 1000;
    public const int MaximumReferences = 200;
    public const int MaximumParents = 32;
    public void Validate(string repository)
    {
        if (string.IsNullOrWhiteSpace(repository) || repository.Contains('\0')) throw new InspectionException("invalid_path", "Choose a repository path without NUL characters.");
        if (Limit is < 1 or > MaximumCommits) throw new InspectionException("invalid_graph", "Graph windows contain 1 to 1,000 commits.");
        if (Reference is not null) new RevisionSelection(Reference, Reference).Validate();
        if (Scope is not { } scope) return;
        if (!Path.IsPathFullyQualified(scope.Root) || scope.Tips.Count > MaximumReferences || scope.References.Count > MaximumReferences
            || scope.Tips.Any(id => !RevisionEndpoints.IsCommitId(id)) || scope.Tips.Distinct(StringComparer.Ordinal).Count() != scope.Tips.Count
            || scope.References.Any(item => !RevisionEndpoints.IsCommitId(item.CommitId) || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 1024 || item.Name.Any(char.IsControl) || item.Kind is not ("head" or "branch" or "remote" or "tag"))
            || scope.References.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != scope.References.Count
            || scope.Reference != Reference)
            throw new InspectionException("invalid_graph", "The pinned graph scope is invalid or belongs to a different selection.");
    }
}
