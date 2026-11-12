using Treesi.Domain;

namespace Treesi.Git;

public sealed partial class GitInspector
{
    public const int ReferenceLimit = RevisionLimits.References;
    public const int RevisionFileLimit = RevisionLimits.Files;

    public async Task<RevisionReferences> ReferencesAsync(string repository, CancellationToken cancellationToken = default)
    {
        var root = await FindRevisionRootAsync(repository, cancellationToken);
        var result = await RunHistoricalAsync(root, ["for-each-ref", "--count=201", "--sort=refname", "--format=%(refname)%00%(objectname)%00", "refs/heads/", "refs/tags/"], cancellationToken);
        var items = new List<RevisionReference>();
        var head = await _git.RunAsync(root, ["--no-replace-objects", "rev-parse", "--verify", "--end-of-options", "HEAD^{commit}"], cancellationToken);
        if (head.ExitCode == 0) items.Add(new("HEAD", ParseCommitId(head.Output), "head"));
        var fields = result.Output.Split('\0');
        if (fields.Length % 2 != 1 || fields[^1] is not ("" or "\n"))
            throw new InspectionException("invalid_git_output", "Git returned an incomplete reference list.");
        for (var index = 0; index + 1 < fields.Length; index += 2)
        {
            var name = fields[index].TrimStart('\n'); var id = fields[index + 1];
            if (!(name.StartsWith("refs/heads/", StringComparison.Ordinal) || name.StartsWith("refs/tags/", StringComparison.Ordinal)) || !RevisionEndpoints.IsCommitId(id))
                throw new InspectionException("invalid_git_output", "Git returned an invalid reference record.");
            items.Add(new(name, id, name.StartsWith("refs/heads/", StringComparison.Ordinal) ? "branch" : "tag"));
        }
        return new(items.Take(ReferenceLimit).ToArray(), items.Count > ReferenceLimit, ReferenceLimit);
    }

    public async Task<RevisionEndpoints> ResolveAsync(string repository, RevisionSelection selection, CancellationToken cancellationToken = default)
    {
        selection.Validate();
        var root = await FindRevisionRootAsync(repository, cancellationToken);
        var from = await ResolveCommitAsync(root, selection.Base, cancellationToken);
        var to = await ResolveCommitAsync(root, selection.Target, cancellationToken);
        var effective = from;
        if (selection.Mode == RevisionComparisonMode.MergeBase)
        {
            var common = await _git.RunAsync(root, ["--no-replace-objects", "merge-base", "--all", from, to], cancellationToken);
            if (common.ExitCode != 0 || string.IsNullOrWhiteSpace(common.Output))
                throw new InspectionException("no_merge_base", "No common ancestor is available locally. History may be unrelated or shallow; no fetch was attempted.");
            var bases = common.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (bases.Length != 1) throw new InspectionException("ambiguous_merge_base", "Several merge bases exist. Choose explicit commits or a direct comparison.");
            effective = ParseCommitId(bases[0]);
        }
        return new(root, selection.Base, from, selection.Target, to, effective, selection.Mode);
    }

    public async Task<RevisionComparison> CompareAsync(RevisionEndpoints endpoints, CancellationToken cancellationToken = default)
    {
        await CheckRevisionEndpointsAsync(endpoints, cancellationToken);
        var result = await RunHistoricalAsync(endpoints.Root, ["diff", "--name-status", "-z", "--no-renames", "--no-relative", "--no-ext-diff", "--no-textconv", "--ignore-submodules=all",
            endpoints.EffectiveBaseCommitId, endpoints.TargetCommitId, "--"], cancellationToken);
        var files = new List<RevisionFileChange>();
        if (result.Output.Length == 0) return new(endpoints, files);
        var fields = result.Output.Split('\0');
        if (fields.Length % 2 != 1 || fields[^1] != "") throw new InspectionException("invalid_git_output", "Git returned an incomplete revision file list.");
        for (var index = 0; index < fields.Length - 1; index += 2)
        {
            if (fields[index] is not ("A" or "D" or "M" or "T") || fields[index + 1].Length == 0)
                throw new InspectionException("invalid_git_output", "Git returned an unsupported revision file record.");
            files.Add(new(fields[index + 1], fields[index]));
            if (files.Count > RevisionFileLimit) throw new InspectionException("output_limit", "The comparison exceeds 10,000 files. Inspect a specific file with revision-diff.");
        }
        return new(endpoints, files);
    }

    public async Task<FileDiff> DiffAsync(RevisionEndpoints endpoints, string path, DiffOptions? options = null, CancellationToken cancellationToken = default)
    {
        endpoints.Validate(); options ??= DiffOptions.Default; options.Validate();
        var gitPath = RevisionPath.Normalize(endpoints.Root, path);
        await CheckRevisionEndpointsAsync(endpoints, cancellationToken);
        var before = await HistoricalFileExistsAsync(endpoints.Root, endpoints.EffectiveBaseCommitId, gitPath, cancellationToken);
        var after = await HistoricalFileExistsAsync(endpoints.Root, endpoints.TargetCommitId, gitPath, cancellationToken);
        if (!before && !after) throw new InspectionException("path_not_found", "The file is absent from both compared trees.");
        var args = CreateDiffArguments(options);
        args.AddRange(["--no-relative", endpoints.EffectiveBaseCommitId, endpoints.TargetCommitId, "--", gitPath]);
        var result = await RunHistoricalAsync(endpoints.Root, args, cancellationToken);
        var binary = result.Output.Contains("\nBinary files ", StringComparison.Ordinal);
        return new(gitPath, Comparison.Revisions, result.Output, binary, PatchParser.Parse(result.Output), Options: options, Revisions: endpoints);
    }

    private async Task<string> FindRevisionRootAsync(string repository, CancellationToken token)
    {
        var path = Path.GetFullPath(repository);
        var directory = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
        if (!Directory.Exists(directory)) throw new InspectionException("path_not_found", "The repository path is not available.");
        var result = await _git.RunAsync(directory, ["--no-replace-objects", "rev-parse", "--show-toplevel"], token);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output)) throw new InspectionException("not_repository", "Choose a local Git working tree.");
        var root = result.Output.TrimEnd('\r', '\n');
        await CheckConfigurationAsync(root, token);
        return root;
    }

    private async Task<bool> HistoricalFileExistsAsync(string root, string commit, string path, CancellationToken token)
    {
        var result = await RunHistoricalAsync(root, ["ls-tree", "-z", "--full-tree", commit, "--", path], token);
        if (result.Output.Length == 0) return false;
        var records = result.Output.Split('\0');
        var tab = records[0].IndexOf('\t');
        if (records.Length != 2 || records[1] != "" || tab < 0 || records[0][(tab + 1)..] != path)
            throw new InspectionException("invalid_git_output", "Git returned an ambiguous historical path.");
        var entry = records[0][..tab].Split(' ');
        if (entry.Length != 3 || !RevisionEndpoints.IsCommitId(entry[2])) throw new InspectionException("invalid_git_output", "Git returned an invalid tree entry.");
        if (entry[1] == "tree") throw new InspectionException("invalid_path", "Select a historical file rather than a directory or file-to-directory transition.");
        if (entry[1] == "commit") throw new InspectionException("unsupported_submodule", "Submodule entries are not traversed by revision comparison.");
        if (entry[1] != "blob") throw new InspectionException("invalid_git_output", "The historical entry is not a supported blob.");
        return true;
    }

    private async Task CheckRevisionEndpointsAsync(RevisionEndpoints endpoints, CancellationToken token)
    {
        endpoints.Validate();
        var root = await FindRevisionRootAsync(endpoints.Root, token);
        if (!string.Equals(Path.GetFullPath(root), Path.GetFullPath(endpoints.Root), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InspectionException("invalid_revision_comparison", "The resolved root must be the Git working tree root.");
    }

    private async Task<string> ResolveCommitAsync(string root, string name, CancellationToken token)
    {
        var result = await _git.RunAsync(root, ["--no-replace-objects", "rev-parse", "--verify", "--end-of-options", name + "^{commit}"], token);
        if (result.ExitCode != 0) throw new InspectionException("revision_not_found", "A revision does not resolve to a local commit. Check its name and available objects; no fetch was attempted.");
        return ParseCommitId(result.Output);
    }

    private async Task<GitResult> RunHistoricalAsync(string root, IEnumerable<string> args, CancellationToken token)
    {
        var result = await _git.RunAsync(root, ["--no-replace-objects", .. args], token);
        if (result.ExitCode != 0) throw new InspectionException("revision_unavailable", "Git could not read the compared objects. They may be missing or no longer available locally; no fetch was attempted.");
        return result;
    }

    private static string ParseCommitId(string output)
    {
        var id = output.TrimEnd('\r', '\n');
        if (!RevisionEndpoints.IsCommitId(id)) throw new InspectionException("invalid_git_output", "Git returned an invalid commit ID.");
        return id;
    }
}
