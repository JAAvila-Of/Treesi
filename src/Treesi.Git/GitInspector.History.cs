using System.Globalization;
using Treesi.Application;
using Treesi.Domain;

namespace Treesi.Git;

public sealed partial class GitInspector : IHistoryReader
{
    public async Task<HistoryPage> PageAsync(string repository, HistoryQuery query, CancellationToken cancellationToken = default)
    {
        query.Validate(repository);
        var root = await FindRevisionRootAsync(repository, cancellationToken);
        var start = await ResolveCommitAsync(root, query.Start, cancellationToken);
        var args = new List<string> { "log", "--topo-order", "--full-history", "--no-decorate", "--no-notes", "--no-show-signature", "--no-use-mailmap", "--encoding=UTF-8",
            "--format=format:%H%x00%an%x00%aI%x00%s", "-z", "--no-patch", "--max-count=" + (query.Limit + 1).ToString(CultureInfo.InvariantCulture),
            "--skip=" + query.Offset.ToString(CultureInfo.InvariantCulture), start, "--" };
        if (query.Path is not null) args.Add(query.Path);
        var result = await RunHistoricalAsync(root, args, cancellationToken);
        var fields = result.Output.Length == 0 ? [] : result.Output.Split('\0');
        if (fields.Length % 4 != 0) throw new InspectionException("invalid_git_output", "Git returned incomplete history records.");
        var items = new List<CommitSummary>();
        for (var index = 0; index < fields.Length; index += 4)
        {
            if (!RevisionEndpoints.IsCommitId(fields[index])) throw new InspectionException("invalid_git_output", "Git returned an invalid history commit ID.");
            items.Add(new(fields[index], fields[index + 3], fields[index + 1], fields[index + 2]));
        }
        var next = items.Count > query.Limit && query.Offset + query.Limit <= HistoryQuery.MaximumOffset ? (int?)(query.Offset + query.Limit) : null;
        return new(root, start, query.Path, query.Offset, query.Limit, items.Take(query.Limit).ToArray(), next);
    }

    public async Task<CommitDetail> CommitAsync(string repository, string revision, CancellationToken cancellationToken = default)
    {
        new HistoryQuery(revision).Validate(repository);
        var root = await FindRevisionRootAsync(repository, cancellationToken);
        var id = await ResolveCommitAsync(root, revision, cancellationToken);
        return await ReadCommitAsync(root, id, cancellationToken);
    }

    private async Task<CommitDetail> ReadCommitAsync(string root, string id, CancellationToken token)
    {
        // Raw parents remain accurate at shallow boundaries and are not rewritten by path-limited traversal.
        var raw = (await RunHistoricalAsync(root, ["cat-file", "commit", id], token)).Output;
        var separator = raw.IndexOf("\n\n", StringComparison.Ordinal);
        if (separator < 0 || raw.Contains('\0')) throw new InspectionException("invalid_git_output", "The commit has unsupported metadata.");
        var headers = raw[..separator].Split('\n');
        var parents = headers.Where(line => line.StartsWith("parent ", StringComparison.Ordinal)).Select(line => line[7..]).ToArray();
        var author = headers.FirstOrDefault(line => line.StartsWith("author ", StringComparison.Ordinal));
        var committer = headers.FirstOrDefault(line => line.StartsWith("committer ", StringComparison.Ordinal));
        if (parents.Any(parent => !RevisionEndpoints.IsCommitId(parent)) || author is null || committer is null)
            throw new InspectionException("invalid_git_output", "The commit has invalid parent or identity headers.");
        return new(root, id, parents, author[7..], committer[10..], raw[(separator + 2)..]);
    }
}
