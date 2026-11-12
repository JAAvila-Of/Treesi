using System.Globalization;
using Treesi.Application;
using Treesi.Domain;

namespace Treesi.Cli;

public static partial class CommandLine
{
    private static string[] AvailableCommands(IRevisionService? revisions, IHistoryService? history)
    {
        var commands = new List<string> { "capabilities", "inspect", "diff" };
        if (revisions is not null) commands.AddRange(["references", "compare", "revision-diff"]);
        if (history is not null) commands.AddRange(["history", "commit"]);
        if (history is not null && revisions is not null) commands.AddRange(["commit-files", "commit-diff"]);
        return commands.ToArray();
    }

    private static IHistoryService RequireHistory(IHistoryService? service) => service ?? throw new InspectionException("capability_unavailable", "This host has no history service.");

    private static async Task<HistoryPage> ReadHistoryAsync(IHistoryService service, string repository, string[] flags, CancellationToken token)
    {
        var query = new HistoryQuery(); var seen = new HashSet<string>(StringComparer.Ordinal);
        if (flags.Length % 2 != 0) throw HistoryUsage();
        for (var index = 0; index < flags.Length; index += 2)
        {
            if (!seen.Add(flags[index])) throw HistoryUsage();
            var value = flags[index + 1];
            query = flags[index] switch
            {
                "--start" => query with { Start = value },
                "--path" => query with { Path = value },
                "--offset" => query with { Offset = ParseHistoryInteger(value) },
                "--limit" => query with { Limit = ParseHistoryInteger(value) },
                _ => throw HistoryUsage()
            };
        }
        return await service.PageAsync(repository, query, token);
    }

    private static int ParseHistoryInteger(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : throw HistoryUsage();

    private static (int Parent, DiffOptions Options) ParseCommitFlags(string[] flags, bool allowDiff)
    {
        if (flags.Length % 2 != 0) throw HistoryUsage();
        var parent = 1; var seen = new HashSet<string>(StringComparer.Ordinal); var diff = new List<string>();
        for (var index = 0; index < flags.Length; index += 2)
        {
            if (!seen.Add(flags[index])) throw HistoryUsage();
            if (flags[index] == "--parent") parent = ParseHistoryInteger(flags[index + 1]);
            else if (allowDiff && flags[index] is "--context" or "--whitespace") diff.AddRange([flags[index], flags[index + 1]]);
            else throw HistoryUsage();
        }
        return (parent, ParseDiffFlags(diff.ToArray()).Options);
    }

    private static async Task<RevisionComparison> ReadCommitFilesAsync(IHistoryService history, IRevisionService revisions, string repository, string revision, string[] flags, CancellationToken token)
    {
        var (parent, _) = ParseCommitFlags(flags, false);
        var detail = await history.CommitAsync(repository, revision, token);
        return await revisions.CompareAsync(detail.Comparison(parent), token);
    }

    private static async Task<FileDiff> ReadCommitDiffAsync(IHistoryService history, IRevisionService revisions, string repository, string revision, string path, string[] flags, CancellationToken token)
    {
        var (parent, options) = ParseCommitFlags(flags, true);
        var detail = await history.CommitAsync(repository, revision, token);
        return await revisions.DiffAsync(detail.Comparison(parent), path, options, token);
    }

    private static InspectionException HistoryUsage() => new("usage", "Usage: history <repository> [--start <revision>] [--path <literal-file>] [--offset 0..10000] [--limit 1..50] | commit <repository> <revision> | commit-files <repository> <revision> [--parent <one-based-index>] | commit-diff <repository> <revision> <literal-file> [--parent <one-based-index>] [--context 0..50] [--whitespace <mode>]");
}
