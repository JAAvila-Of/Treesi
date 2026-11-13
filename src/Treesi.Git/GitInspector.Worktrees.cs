using Treesi.Application;
using Treesi.Domain;

namespace Treesi.Git;

public sealed partial class GitInspector : IWorktreeReader
{
    public async Task<RepositoryWorktrees> ReadWorktreesAsync(string repository, CancellationToken cancellationToken = default)
    {
        var root = await FindRevisionRootAsync(repository, cancellationToken);
        var result = await _git.RunAsync(root, ["--no-replace-objects", "worktree", "list", "--porcelain", "-z"], cancellationToken);
        if (result.ExitCode != 0) throw new InspectionException("git_failed", "Git could not list registered worktrees. Check local repository availability and retry.");
        var fields = result.Output.Split('\0'); var items = new List<RepositoryWorktree>();
        var record = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (field.Length == 0)
            {
                if (record.Count == 0) continue;
                if (!record.TryGetValue("worktree", out var path) || !record.ContainsKey("bare") && !record.ContainsKey("HEAD")) throw InvalidWorktrees();
                var head = record.GetValueOrDefault("HEAD");
                if (head is not null && head.All(character => character == '0') && head.Length is 40 or 64) head = null;
                items.Add(new(path, head, record.GetValueOrDefault("branch"), record.ContainsKey("bare"), record.ContainsKey("detached"),
                    record.ContainsKey("locked"), record.GetValueOrDefault("locked"), record.ContainsKey("prunable"), record.GetValueOrDefault("prunable")));
                if (items.Count > RepositoryWorktrees.MaximumEntries) throw new InspectionException("output_limit", "The worktree list exceeds 128 entries.");
                record.Clear(); continue;
            }
            var space = field.IndexOf(' '); var key = space < 0 ? field : field[..space]; var value = space < 0 ? "" : field[(space + 1)..];
            if (key is not ("worktree" or "HEAD" or "branch" or "bare" or "detached" or "locked" or "prunable")
                || record.Count == 0 && key != "worktree" || key is "bare" or "detached" && value.Length != 0 || !record.TryAdd(key, value)) throw InvalidWorktrees();
        }
        if (record.Count != 0 || !result.Output.EndsWith("\0\0", StringComparison.Ordinal)) throw InvalidWorktrees();
        return new(root, items);
    }

    private static InspectionException InvalidWorktrees() => new("invalid_git_output", "Git returned unsupported or incomplete worktree metadata.");
}
