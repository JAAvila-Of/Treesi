using Treesi.Domain;
using System.Globalization;

namespace Treesi.Desktop;

public sealed record HistoryCommitItem(CommitSummary Commit)
{
    public string Subject => Preview(Commit.Subject, 256);
    public string Caption => $"{Commit.CommitId[..8]} · {Preview(Commit.Author, 80)}";
    public string AuthoredAt => Preview(Commit.AuthoredAt, 40);
    internal static string Preview(string value, int limit)
    {
        if (value.Length <= limit) return value;
        var end = 0;
        while (end < value.Length)
        {
            var length = StringInfo.GetNextTextElementLength(value.AsSpan(end));
            if (end + length > limit) break;
            end += length;
        }
        return value[..end] + "…";
    }
}
