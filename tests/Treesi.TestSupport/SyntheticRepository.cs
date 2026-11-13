using System.Diagnostics;
using System.Text;

namespace Treesi.TestSupport;

public sealed class SyntheticRepository : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "treesi-lab-" + Guid.NewGuid().ToString("N"));
    public string SamplePath => Path.Combine(Root, "sample.txt");

    public SyntheticRepository(int fileCount = 10, bool revisionHistory = false)
    {
        Directory.CreateDirectory(Root);
        Git("-c", "init.templateDir=", "init", "--initial-branch=main");
        Git("config", "user.name", "Treesi Lab"); Git("config", "user.email", "lab@example.invalid");
        Git("config", "core.autocrlf", "false");
        for (var i = 0; i < fileCount; i++) File.WriteAllText(Path.Combine(Root, $"file-{i:D6}.txt"), $"Synthetic fixture {i}\n", new UTF8Encoding(false));
        File.WriteAllText(SamplePath, "first\noriginal\nlast\n", new UTF8Encoding(false));
        Git("add", "--", ".");
        Git("-c", "commit.gpgSign=false", "-c", "core.hooksPath=", "commit", "-m", "test: create isolated fixture");
        if (revisionHistory)
        {
            File.WriteAllText(SamplePath, "first\nhistorical\nlast\n", new UTF8Encoding(false));
            Git("add", "--", "sample.txt");
            Git("-c", "commit.gpgSign=false", "-c", "core.hooksPath=", "commit", "-m", "test: create historical change");
        }
        File.WriteAllText(SamplePath, "first\nchanged\nlast\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(Root, "new.txt"), "Synthetic new file\n", new UTF8Encoding(false));
    }

    public string AddLinkedWorktree()
    {
        var linked = Path.Combine(Root, "linked-review");
        Git("-c", "core.hooksPath=", "worktree", "add", "-b", "feature/linked-review", linked);
        File.WriteAllText(Path.Combine(linked, "sample.txt"), "first\nlinked change\nlast\n", new UTF8Encoding(false));
        return linked;
    }

    public string AddLinearHistory(int commitCount)
    {
        if (commitCount is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(commitCount));
        var parent = Git("rev-parse", "HEAD").Trim();
        var input = new StringBuilder();
        for (var index = 1; index <= commitCount; index++)
        {
            var message = $"perf: synthetic revision {index}\n";
            var content = $"first\nrevision {index}\nlast\n";
            input.Append("commit refs/heads/benchmark-history\nmark :").Append(index).Append('\n');
            input.Append("committer Treesi Lab <lab@example.invalid> ").Append(1700000000L + index).Append(" +0000\n");
            input.Append("data ").Append(Encoding.UTF8.GetByteCount(message)).Append('\n').Append(message);
            input.Append("from ").Append(index == 1 ? parent : ":" + (index - 1)).Append('\n');
            input.Append("M 100644 inline sample.txt\ndata ").Append(Encoding.UTF8.GetByteCount(content)).Append('\n').Append(content).Append('\n');
        }
        input.Append("done\n");
        GitWithInput(input.ToString(), "-c", "core.hooksPath=", "fast-import", "--quiet", "--done");
        return Git("rev-parse", "refs/heads/benchmark-history").Trim();
    }

    private string Git(params string[] arguments) => GitWithInput(null, arguments);

    private string GitWithInput(string? input, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        { WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        async Task SendInputAsync() { if (input is not null) await process.StandardInput.WriteAsync(input); process.StandardInput.Close(); }
        var write = SendInputAsync();
        if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException("Fixture creation exceeded its Git timeout."); }
        Task.WaitAll(output, error, write);
        if (process.ExitCode != 0) throw new InvalidOperationException("Synthetic repository creation failed.");
        return output.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        var full = Path.GetFullPath(Root);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !Path.GetFileName(full).StartsWith("treesi-lab-", StringComparison.Ordinal))
            throw new InvalidOperationException("Cleanup escaped the synthetic lab directory.");
        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(full, recursive: true);
    }
}
