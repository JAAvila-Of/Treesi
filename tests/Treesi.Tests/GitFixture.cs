using System.Diagnostics;
using System.Text;

namespace Treesi.Tests;

internal sealed class GitFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "treesi-tests-" + Guid.NewGuid().ToString("N"));

    public GitFixture(bool sha256 = false)
    {
        Directory.CreateDirectory(Root);
        if (sha256) Git("init", "--initial-branch=main", "--object-format=sha256");
        else Git("init", "--initial-branch=main");
        Git("config", "user.name", "Treesi Test");
        Git("config", "user.email", "tests@example.invalid");
        Git("config", "core.autocrlf", "false");
        Write("sample.txt", "first\nold\nlast\n");
        Git("add", "--", "sample.txt");
        Git("-c", "commit.gpgSign=false", "-c", "core.hooksPath=", "commit", "-m", "test: create fixture");
    }

    public void Write(string name, string text) => File.WriteAllText(Path.Combine(Root, name), text, new UTF8Encoding(false));

    public string Git(params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(true); throw new TimeoutException("Fixture Git timed out."); }
        if (process.ExitCode != 0) throw new InvalidOperationException(error.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        var full = Path.GetFullPath(Root);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("treesi-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture cleanup escaped the temporary test directory.");
        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(full, recursive: true);
    }
}
