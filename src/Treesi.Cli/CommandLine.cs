using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Treesi.Application;
using Treesi.Domain;

namespace Treesi.Cli;

public static class CommandLine
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<int> RunAsync(string[] args, TextWriter output, IInspectionService inspector, CancellationToken token = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var start = Stopwatch.GetTimestamp();
        object? data = null;
        object? error = null;
        var exitCode = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            data = args switch
            {
                ["capabilities"] => new
                {
                    product = "Treesi",
                    version = "0.1.0",
                    provider = "git",
                    readOnly = true,
                    commands = new[] { "capabilities", "inspect", "diff" },
                    comparisons = new[] { "working", "staged", "head" },
                    limits = new { gitOutputBytes = 2097152, newFileBytes = 1048576, commandTimeoutSeconds = 15 },
                    transports = new[] { "cli-json" },
                    contentTrust = "untrusted",
                    excluded = new[] { "submoduleContents", "executableFilters", "nonUtf8Text", "revisionToRevisionDiff" }
                },
                ["inspect", var path] => await inspector.InspectAsync(path, token),
                ["diff", var repository, var path] => await inspector.DiffAsync(repository, path, Comparison.WorkingTree, token),
                ["diff", var repository, var path, "--comparison", var comparison] =>
                    await inspector.DiffAsync(repository, path, ParseComparison(comparison), token),
                _ => throw new InspectionException("usage", "Usage: capabilities | inspect <path> | diff <repository> <relative-file> [--comparison working|staged|head]")
            };
        }
        catch (InspectionException exception)
        {
            exitCode = exception.Code is "usage" ? 2 : 1;
            error = new { code = exception.Code, message = exception.Message };
        }
        catch (OperationCanceledException)
        {
            exitCode = 130;
            error = new { code = "cancelled", message = "Inspection was cancelled. No complete result is available." };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            exitCode = 1;
            error = new { code = "path_unavailable", message = "The selected path could not be read." };
        }
        catch (Exception)
        {
            exitCode = 1;
            error = new { code = "internal_error", message = "Inspection failed unexpectedly. No complete result is available." };
        }
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            schemaVersion = "1",
            requestId,
            contentTrust = "untrusted",
            ok = exitCode == 0,
            data,
            error,
            metrics = new { durationMs = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 3) }
        }, Json));
        return exitCode;
    }

    private static Comparison ParseComparison(string value) => value switch
    {
        "working" => Comparison.WorkingTree,
        "staged" => Comparison.Staged,
        "head" => Comparison.Head,
        _ => throw new InspectionException("usage", "Comparison must be working, staged, or head.")
    };
}
