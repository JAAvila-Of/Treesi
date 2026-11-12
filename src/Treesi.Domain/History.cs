namespace Treesi.Domain;

public sealed record HistoryQuery(string Start = "HEAD", string? Path = null, int Offset = 0, int Limit = 30)
{
    public const int MaximumPageSize = 50;
    public const int MaximumOffset = 10000;
    public void Validate(string repository)
    {
        if (string.IsNullOrWhiteSpace(repository) || repository.Contains('\0'))
            throw new InspectionException("invalid_path", "Choose a repository path without NUL characters.");
        new RevisionSelection(Start, Start).Validate();
        if (Limit is < 1 or > MaximumPageSize || Offset is < 0 or > MaximumOffset)
            throw new InspectionException("invalid_history_page", "History pages contain 1 to 50 entries with an offset from 0 to 10,000.");
        if (Offset > 0 && !RevisionEndpoints.IsCommitId(Start))
            throw new InspectionException("invalid_history_page", "Continue history using the complete start commit ID returned by the first page.");
        if (Path is not null) RevisionPath.Normalize(System.IO.Path.GetFullPath(repository), Path);
    }
}

public sealed record CommitSummary(string CommitId, string Subject, string Author, string AuthoredAt)
{
    public override string ToString() => Subject;
}
public sealed record HistoryPage(string Root, string StartCommitId, string? Path, int Offset, int Limit,
    IReadOnlyList<CommitSummary> Items, int? NextOffset, string ContentTrust = "untrusted");

public sealed record CommitDetail(string Root, string CommitId, IReadOnlyList<string> Parents,
    string Author, string Committer, string Message, string ContentTrust = "untrusted")
{
    public RevisionEndpoints Comparison(int parent = 1)
    {
        if (Parents.Count == 0)
        {
            if (parent != 1) throw new InspectionException("invalid_parent", "An initial commit has only the empty-tree comparison.");
            return new(Root, CommitId, CommitId, CommitId, CommitId, CommitId, RevisionComparisonMode.Direct, IsRootComparison: true);
        }
        if (parent < 1 || parent > Parents.Count) throw new InspectionException("invalid_parent", "Choose an existing parent using its one-based index.");
        var id = Parents[parent - 1];
        return new(Root, id, id, CommitId, CommitId, id, RevisionComparisonMode.Direct);
    }
}
