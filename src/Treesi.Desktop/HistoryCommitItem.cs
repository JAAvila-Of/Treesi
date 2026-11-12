using Treesi.Domain;

namespace Treesi.Desktop;

public sealed record HistoryCommitItem(CommitSummary Commit)
{
    public string Subject => Preview(Commit.Subject, 256);
    public string Caption => $"{Commit.CommitId[..8]} · {Preview(Commit.Author, 80)}";
    public string AuthoredAt => Preview(Commit.AuthoredAt, 40);
    internal static string Preview(string value, int limit) => value.Length <= limit ? value : value[..limit] + "…";
}
