using System.Globalization;
using Treesi.Application;
using Treesi.Domain;

namespace Treesi.Git;

public sealed partial class GitInspector : IGraphReader
{
    public async Task<CommitGraphPage> ReadGraphAsync(string repository, GraphQuery query, CancellationToken cancellationToken = default)
    {
        query.Validate(repository);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(commandTimeout ?? TimeSpan.FromSeconds(15));
        try
        {
            var token = deadline.Token;
            var root = await FindRevisionRootAsync(repository, token);
            var scope = query.Scope ?? await ReadGraphScopeAsync(root, query.Reference, token);
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            if (!comparer.Equals(root, scope.Root)) throw new InspectionException("invalid_graph", "The pinned graph belongs to another working tree.");
            var shallow = (await RunHistoricalAsync(root, ["rev-parse", "--is-shallow-repository"], token)).Output.Trim() == "true";
            if (scope.Tips.Count == 0) return new(scope, [], query.Limit, false, shallow);
            var args = new List<string> { "log", "--topo-order", "--no-decorate", "--no-notes", "--no-show-signature", "--no-use-mailmap", "--encoding=UTF-8", "--format=raw", "--no-patch", "--max-count=" + (query.Limit + 1).ToString(CultureInfo.InvariantCulture) };
            args.AddRange(scope.Tips); args.Add("--");
            var output = (await RunHistoricalAsync(root, args, token)).Output;
            var commits = ParseGraph(output);
            return new(scope, commits.Take(query.Limit).ToArray(), query.Limit, commits.Count > query.Limit, shallow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InspectionException("timeout", "The graph exceeded the inspection time limit. Narrow the reference selection."); }
    }

    private async Task<GraphScope> ReadGraphScopeAsync(string root, string? reference, CancellationToken token)
    {
        var output = (await RunHistoricalAsync(root, ["for-each-ref", "--count=201", "--sort=refname", "--format=%(refname)%00%(objectname)%00%(objecttype)%00%(*objectname)%00%(*objecttype)%00", "refs/heads/", "refs/remotes/", "refs/tags/"], token)).Output;
        var refs = new List<GraphReference>(); var limited = false;
        var head = await _git.RunAsync(root, ["--no-replace-objects", "rev-parse", "--verify", "--end-of-options", "HEAD^{commit}"], token);
        if (head.ExitCode == 0) refs.Add(new("HEAD", ParseCommitId(head.Output), "head"));
        else
        {
            var symbolic = await _git.RunAsync(root, ["--no-replace-objects", "symbolic-ref", "-q", "HEAD"], token);
            if (symbolic.ExitCode != 0) throw new InspectionException("revision_not_found", "HEAD does not resolve to an available local commit.");
        }
        var fields = output.Split('\0');
        if (fields.Length % 5 != 1 || fields[^1] is not ("" or "\n")) throw GraphOutput();
        for (var i = 0; i + 4 < fields.Length; i += 5)
        {
            var name = fields[i].TrimStart('\n');
            var kind = name.StartsWith("refs/heads/", StringComparison.Ordinal) ? "branch" : name.StartsWith("refs/remotes/", StringComparison.Ordinal) ? "remote" : name.StartsWith("refs/tags/", StringComparison.Ordinal) ? "tag" : throw GraphOutput();
            var id = fields[i + 2] == "commit" ? fields[i + 1] : fields[i + 2] == "tag" && fields[i + 4] == "commit" ? fields[i + 3] : null;
            if (id is null) { limited = true; continue; }
            if (!RevisionEndpoints.IsCommitId(id) || name.Length > 1024 || name.Any(char.IsControl)) throw GraphOutput();
            if (refs.Count == GraphQuery.MaximumReferences) { limited = true; continue; }
            refs.Add(new(name, id, kind));
        }
        var tips = reference is null ? refs.Select(item => item.CommitId).Distinct(StringComparer.Ordinal).ToArray() : [await ResolveCommitAsync(root, reference, token)];
        return new(root, tips, refs.ToArray(), reference, limited);
    }

    private static IReadOnlyList<GraphCommit> ParseGraph(string output)
    {
        if (output.Contains('\0')) throw GraphOutput();
        var lines = output.Split('\n'); var commits = new List<GraphCommit>(); var index = 0;
        while (index < lines.Length)
        {
            while (index < lines.Length && lines[index].Length == 0) index++;
            if (index == lines.Length) break;
            if (!lines[index].StartsWith("commit ", StringComparison.Ordinal)) throw GraphOutput();
            var id = lines[index++][7..]; if (!RevisionEndpoints.IsCommitId(id)) throw GraphOutput();
            var parents = new List<string>(); string? author = null;
            while (index < lines.Length && lines[index].Length != 0)
            {
                var line = lines[index++];
                if (line.StartsWith("parent ", StringComparison.Ordinal)) parents.Add(line[7..]);
                if (line.StartsWith("author ", StringComparison.Ordinal)) author = line[7..];
            }
            if (author is null || parents.Count > GraphQuery.MaximumParents || parents.Any(parent => !RevisionEndpoints.IsCommitId(parent))) throw GraphOutput();
            var lastSpace = author.LastIndexOf(' '); var timeSpace = lastSpace > 0 ? author.LastIndexOf(' ', lastSpace - 1) : -1;
            if (timeSpace < 0 || !long.TryParse(author[(timeSpace + 1)..lastSpace], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)) throw GraphOutput();
            string date;
            try { date = DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("O", CultureInfo.InvariantCulture); }
            catch (ArgumentOutOfRangeException) { throw GraphOutput(); }
            var identity = author[..timeSpace]; var email = identity.LastIndexOf(" <", StringComparison.Ordinal);
            var name = email >= 0 ? identity[..email] : identity;
            var subject = "";
            while (index < lines.Length && !lines[index].StartsWith("commit ", StringComparison.Ordinal))
            {
                var line = lines[index++];
                if (line.Length == 0) continue;
                if (!line.StartsWith("    ", StringComparison.Ordinal)) throw GraphOutput();
                if (subject.Length == 0 && !string.IsNullOrWhiteSpace(line[4..])) subject = line[4..].Trim();
            }
            commits.Add(new(id, parents.ToArray(), subject, name, date));
        }
        return commits;
    }

    private static InspectionException GraphOutput() => new("invalid_git_output", "Git returned unsupported or incomplete graph metadata.");
}
