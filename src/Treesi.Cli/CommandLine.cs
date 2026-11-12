using System.Diagnostics;
using System.Globalization;
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

    public static async Task<int> RunAsync(string[] args, TextWriter output, IInspectionService inspector, CancellationToken token = default, IRevisionService? revisions = null)
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
                    commands = revisions is null ? new[] { "capabilities", "inspect", "diff" } : ["capabilities", "inspect", "diff", "references", "compare", "revision-diff"],
                    comparisons = new[] { "working", "staged", "head" },
                    revisionComparisons = new { available = revisions is not null, modes = new[] { "direct", "merge-base" }, referenceLimit = RevisionLimits.References, fileLimit = RevisionLimits.Files },
                    diffOptions = new
                    {
                        context = new { minimum = 0, maximum = DiffOptions.MaximumContextLines, @default = 3 },
                        whitespace = new[] { "exact", "ignore-trailing-space", "ignore-space-change", "ignore-all-space" },
                        defaultWhitespace = "exact"
                    },
                    limits = new { gitOutputBytes = 2097152, newFileBytes = 1048576, commandTimeoutSeconds = 15 },
                    transports = new[] { "cli-json" },
                    contentTrust = "untrusted",
                    excluded = revisions is null ? new[] { "submoduleContents", "executableFilters", "nonUtf8Text", "revisionToRevisionDiff" } : ["submoduleContents", "executableFilters", "nonUtf8Text"]
                },
                ["inspect", var path] => await inspector.InspectAsync(path, token),
                ["diff", var repository, var path, .. var flags] => await ReadDiffAsync(inspector, repository, path, flags, token),
                ["references", var repository] => await RequireRevisions(revisions).ReferencesAsync(repository, token),
                ["compare", var repository, var from, var to, .. var flags] => await CompareAsync(RequireRevisions(revisions), repository, from, to, flags, token),
                ["revision-diff", var repository, var from, var to, var path, .. var flags] => await ReadRevisionDiffAsync(RequireRevisions(revisions), repository, from, to, path, flags, token),
                _ => throw Usage()
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

    private static async Task<FileDiff> ReadDiffAsync(IInspectionService inspector, string repository, string path, string[] flags, CancellationToken token)
    {
        var (comparison, options) = ParseDiffFlags(flags);
        return await inspector.DiffAsync(repository, path, comparison, token, options);
    }

    private static (Comparison Comparison, DiffOptions Options) ParseDiffFlags(string[] flags)
    {
        if (flags.Length % 2 != 0) throw Usage();
        var comparison = Comparison.WorkingTree;
        var options = DiffOptions.Default;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < flags.Length; index += 2)
        {
            var flag = flags[index]; var value = flags[index + 1];
            if (!seen.Add(flag)) throw new InspectionException("usage", "Each diff option may be specified only once.");
            switch (flag)
            {
                case "--comparison": comparison = ParseComparison(value); break;
                case "--context":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var context) || context > DiffOptions.MaximumContextLines)
                        throw new InspectionException("usage", "Context must be a whole number between 0 and 50.");
                    options = options with { ContextLines = context }; break;
                case "--whitespace":
                    options = options with
                    {
                        Whitespace = value switch
                        {
                            "exact" => WhitespaceMode.Exact,
                            "ignore-trailing-space" => WhitespaceMode.IgnoreTrailingSpace,
                            "ignore-space-change" => WhitespaceMode.IgnoreSpaceChange,
                            "ignore-all-space" => WhitespaceMode.IgnoreAllSpace,
                            _ => throw new InspectionException("usage", "Whitespace must be exact, ignore-trailing-space, ignore-space-change, or ignore-all-space.")
                        }
                    };
                    break;
                default: throw Usage();
            }
        }
        return (comparison, options);
    }

    private static IRevisionService RequireRevisions(IRevisionService? service) => service ?? throw new InspectionException("capability_unavailable", "This host has no revision comparison service.");

    private static (RevisionComparisonMode Mode, DiffOptions Options) ParseRevisionFlags(string[] flags, bool allowDiffOptions)
    {
        if (flags.Length % 2 != 0) throw Usage();
        var mode = RevisionComparisonMode.Direct; var seenMode = false; var diffFlags = new List<string>();
        for (var index = 0; index < flags.Length; index += 2)
        {
            if (flags[index] == "--mode")
            {
                if (seenMode) throw Usage(); seenMode = true;
                mode = flags[index + 1] switch { "direct" => RevisionComparisonMode.Direct, "merge-base" => RevisionComparisonMode.MergeBase, _ => throw Usage() };
            }
            else if (allowDiffOptions && flags[index] is "--context" or "--whitespace") diffFlags.AddRange([flags[index], flags[index + 1]]);
            else throw Usage();
        }
        return (mode, ParseDiffFlags(diffFlags.ToArray()).Options);
    }

    private static async Task<RevisionComparison> CompareAsync(IRevisionService service, string repository, string from, string to, string[] flags, CancellationToken token)
    {
        var (mode, _) = ParseRevisionFlags(flags, false);
        var endpoints = await service.ResolveAsync(repository, new(from, to, mode), token);
        return await service.CompareAsync(endpoints, token);
    }

    private static async Task<FileDiff> ReadRevisionDiffAsync(IRevisionService service, string repository, string from, string to, string path, string[] flags, CancellationToken token)
    {
        var (mode, options) = ParseRevisionFlags(flags, true);
        var endpoints = await service.ResolveAsync(repository, new(from, to, mode), token);
        return await service.DiffAsync(endpoints, path, options, token);
    }

    private static InspectionException Usage() => new("usage", "Usage: capabilities | inspect <path> | references <repository> | compare <repository> <base> <target> [--mode direct|merge-base] | revision-diff <repository> <base> <target> <relative-file> [--mode direct|merge-base] [--context 0..50] [--whitespace <mode>] | diff <repository> <relative-file> [--comparison working|staged|head] [--context 0..50] [--whitespace exact|ignore-trailing-space|ignore-space-change|ignore-all-space]");
}
