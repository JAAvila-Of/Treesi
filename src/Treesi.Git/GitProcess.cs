using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Treesi.Domain;

namespace Treesi.Git;

internal sealed record GitResult(int ExitCode, string Output);

internal sealed class GitProcess(string executable, TimeSpan timeout)
{
    internal const int OutputLimit = 2 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<GitResult> RunAsync(string directory, IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        start.Environment["GIT_ALLOW_PROTOCOL"] = "";
        start.Environment["GIT_PAGER"] = "";
        start.Environment["LC_ALL"] = "C";
        foreach (var arg in new[] { "--no-pager", "--no-optional-locks", "--literal-pathspecs",
                     "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false",
                     "-c", "core.quotePath=false", "-c", "color.ui=false",
                     "-c", "diff.external=", "-c", "maintenance.auto=false", "-c", "gc.auto=0" }.Concat(arguments))
            start.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = start };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start()) throw new InspectionException("git_unavailable", "Git could not be started.");
            process.StandardInput.Close();
            var output = ReadAsync(process.StandardOutput.BaseStream, OutputLimit, process, deadline.Token);
            var error = ReadAsync(process.StandardError.BaseStream, 64 * 1024, process, deadline.Token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            return new(process.ExitCode, StrictUtf8.GetString(await output.ConfigureAwait(false)));
        }
        catch (Win32Exception)
        {
            throw new InspectionException("git_unavailable", "Git was not found. Install Git and make it available on PATH.");
        }
        catch (DecoderFallbackException)
        {
            throw new InspectionException("unsupported_encoding", "Git returned data that is not valid UTF-8.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InspectionException("timeout", "Git exceeded the inspection time limit. No complete result is available.");
        }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    private static async Task<byte[]> ReadAsync(Stream stream, int limit, Process process, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (await stream.ReadAsync(buffer, token).ConfigureAwait(false) is var count && count > 0)
        {
            if (output.Length + count > limit)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw new InspectionException("output_limit", "Inspection output exceeded the 2 MiB safety limit. Narrow the selection.");
            }
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
