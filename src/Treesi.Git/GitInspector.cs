using System.Text;
using Treesi.Application;
using Treesi.Domain;

namespace Treesi.Git;

public sealed partial class GitInspector(string gitExecutable = "git", TimeSpan? commandTimeout = null) : IRepositoryReader, IRevisionReader
{
    private readonly GitProcess _git = new(gitExecutable, commandTimeout ?? TimeSpan.FromSeconds(15));
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var isFile = File.Exists(fullPath);
        var directory = isFile ? Path.GetDirectoryName(fullPath)! : fullPath;
        if (!Directory.Exists(directory)) throw new InspectionException("path_not_found", "The selected path does not exist.");
        var rootResult = await RunAsync(directory, ["rev-parse", "--show-toplevel"], cancellationToken);
        var root = rootResult.Output.TrimEnd('\r', '\n');
        if (root.Length == 0) throw new InspectionException("not_repository", "Open a Git working tree or a file inside one.");
        await CheckConfigurationAsync(root, cancellationToken);
        string? selectedPath = null;
        if (isFile)
        {
            // Git resolves aliases such as macOS /var -> /private/var when discovering the working tree.
            // Ask Git for the prefix in the original directory rather than mixing physical and logical paths.
            var prefix = await RunAsync(directory, ["rev-parse", "--show-prefix"], cancellationToken);
            selectedPath = prefix.Output.TrimEnd('\r', '\n') + Path.GetFileName(fullPath);
        }
        var status = await RunAsync(root, ["status", "--porcelain=v2", "-z", "--untracked-files=all", "--ignore-submodules=all"], cancellationToken);
        return new RepositorySnapshot(root, selectedPath, StatusParser.Parse(status.Output));
    }

    public async Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison,
        CancellationToken cancellationToken = default, DiffOptions? options = null)
    {
        if (comparison is not (Comparison.WorkingTree or Comparison.Staged or Comparison.Head)) throw new InspectionException("invalid_comparison", "Use the revision port to compare commits.");
        options ??= DiffOptions.Default;
        options.Validate();
        var snapshot = await InspectAsync(repository, cancellationToken);
        var root = Path.GetFullPath(snapshot.Root);
        if (Path.IsPathRooted(path) || string.IsNullOrEmpty(path))
            throw new InspectionException("invalid_path", "Select a repository-relative file path.");
        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InspectionException("invalid_path", "The file must be inside the repository.");
        var gitPath = relative.Replace('\\', '/');
        RejectLinks(root, fullPath);
        var change = snapshot.Files.FirstOrDefault(file => file.Path == gitPath);
        if (Directory.Exists(fullPath))
            throw new InspectionException("invalid_path", "Select a file rather than a directory for this comparison.");
        if (!File.Exists(fullPath) && change is null)
            throw new InspectionException("path_not_found", "The selected file does not exist and has no recorded change.");
        if (comparison == Comparison.Head)
        {
            var head = await _git.RunAsync(root, ["rev-parse", "--verify", "HEAD"], cancellationToken);
            if (head.ExitCode != 0) throw new InspectionException("unborn_head", "There is no HEAD commit yet. Use working or staged comparison.");
        }
        if (change?.IsUntracked == true)
        {
            if (comparison == Comparison.Staged) return new FileDiff(gitPath, comparison, "", false, [], Options: options);
            return (await ReadUntrackedAsync(fullPath, gitPath, comparison, cancellationToken)) with { Options = options };
        }
        var args = CreateDiffArguments(options);
        if (comparison == Comparison.Staged) args.Add("--cached");
        if (comparison == Comparison.Head)
        {
            args.Add("HEAD");
        }
        args.Add("--");
        args.Add(gitPath);
        var result = await RunAsync(root, args, cancellationToken);
        var binary = result.Output.Contains("\nBinary files ", StringComparison.Ordinal);
        return new FileDiff(gitPath, comparison, result.Output, binary, PatchParser.Parse(result.Output), Options: options);
    }

    private static List<string> CreateDiffArguments(DiffOptions options)
    {
        var args = new List<string> { "diff", "--no-ext-diff", "--no-textconv", "--no-color", "--no-renames", "--ignore-submodules=all",
            "--inter-hunk-context=0", "--output-indicator-new=+", "--output-indicator-old=-", "--output-indicator-context= ",
            "--unified=" + options.ContextLines.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var flag = options.Whitespace switch
        {
            WhitespaceMode.IgnoreTrailingSpace => "--ignore-space-at-eol",
            WhitespaceMode.IgnoreSpaceChange => "--ignore-space-change",
            WhitespaceMode.IgnoreAllSpace => "--ignore-all-space",
            _ => null
        };
        if (flag is not null) args.Add(flag);
        return args;
    }

    private async Task CheckConfigurationAsync(string root, CancellationToken token)
    {
        var config = await RunAsync(root, ["config", "--includes", "--null", "--list"], token);
        foreach (var item in config.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = item.IndexOf('\n');
            var key = separator < 0 ? item : item[..separator];
            var value = separator < 0 ? "" : item[(separator + 1)..];
            if (key.StartsWith("filter.", StringComparison.OrdinalIgnoreCase) && value.Length > 0 &&
                (key.EndsWith(".clean", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".smudge", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".process", StringComparison.OrdinalIgnoreCase)))
                throw new InspectionException("unsafe_configuration", "This repository configures executable content filters. Restricted inspection does not run them.");
        }
    }

    private async Task<GitResult> RunAsync(string root, IEnumerable<string> args, CancellationToken token)
    {
        var result = await _git.RunAsync(root, args, token);
        if (result.ExitCode != 0)
            throw new InspectionException("git_failed", "Git could not complete this inspection. Check that the path is a trusted, accessible Git working tree.");
        return result;
    }

    private static void RejectLinks(string root, string path)
    {
        for (var current = path; !string.Equals(current, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); current = Path.GetDirectoryName(current)!)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InspectionException("unsupported_link", "Link traversal is not supported by restricted file inspection.");
        }
    }

    private static async Task<FileDiff> ReadUntrackedAsync(string fullPath, string path, Comparison comparison, CancellationToken token)
    {
        await using var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 8192, true);
        if (input.Length > GitProcess.OutputLimit / 2)
            throw new InspectionException("output_limit", "This new file exceeds the 1 MiB preview limit.");
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > GitProcess.OutputLimit / 2)
                throw new InspectionException("output_limit", "This new file exceeds the 1 MiB preview limit.");
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        if (bytes.Contains((byte)0)) return new FileDiff(path, comparison, "Binary file (untracked)", true, []);
        string text;
        try { text = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { throw new InspectionException("unsupported_encoding", "This file is not valid UTF-8. Alternate encodings are not supported yet."); }
        var lines = text.Split('\n');
        var lineCount = text.Length == 0 ? 0 : lines.Length - (text.EndsWith('\n') ? 1 : 0);
        var patch = new StringBuilder("--- /dev/null\n+++ new file\n");
        if (lineCount > 0) patch.Append($"@@ -0,0 +1,{lineCount} @@\n");
        for (var i = 0; i < lineCount; i++) patch.Append('+').Append(lines[i]).Append('\n');
        if (text.Length > 0 && !text.EndsWith('\n')) patch.Append("\\ No newline at end of file\n");
        return new FileDiff(path, comparison, patch.ToString(), false, PatchParser.Parse(patch.ToString()));
    }

}
