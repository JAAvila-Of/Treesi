using System.Globalization;
using Microsoft.VisualBasic.FileIO;

namespace Treesi.TestSupport;

public sealed record SwapChainObservation(string SwapChain, int Rows, int DisplayedRows, int DroppedRows,
    IReadOnlyList<string> Modes, LatencySummary? PresentIntervals, LatencySummary? DisplayChangeIntervals);
public sealed record PresentCaptureSummary(string Status, int Rows, IReadOnlyList<SwapChainObservation> SwapChains)
{
    public static PresentCaptureSummary FromCsv(string csv, int ownedProcessId)
    {
        if (ownedProcessId < 1 || csv.Length > 16 * 1024 * 1024) throw new ArgumentException("The capture PID and bounded CSV must be valid.");
        if (string.IsNullOrWhiteSpace(csv)) return new("no-events", 0, []);
        using var parser = new TextFieldParser(new StringReader(csv)) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var header = parser.ReadFields() ?? throw new FormatException("The frame capture has no header.");
        if (header.Distinct(StringComparer.OrdinalIgnoreCase).Count() != header.Length) throw new FormatException("The frame capture has duplicate columns.");
        int Column(string name) => Array.FindIndex(header, item => item.Equals(name, StringComparison.OrdinalIgnoreCase)) is var index && index >= 0 ? index : throw new FormatException("A required frame capture column is missing.");
        var pid = Column("ProcessID"); var swapChain = Column("SwapChainAddress"); var dropped = Column("Dropped");
        var present = Column("MsBetweenPresents"); var display = Column("MsBetweenDisplayChange"); var mode = Column("PresentMode");
        var records = new List<(string Chain, bool Dropped, string Mode, double? Present, double? Display)>();
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields() ?? throw new FormatException("The frame capture has an incomplete row.");
            if (row.Length != header.Length || !int.TryParse(row[pid], NumberStyles.None, CultureInfo.InvariantCulture, out var processId) || processId != ownedProcessId
                || string.IsNullOrWhiteSpace(row[swapChain]) || row[dropped] is not ("0" or "1"))
                throw new FormatException("The frame capture contains an invalid row or another process.");
            if (records.Count == 10000) throw new FormatException("The frame capture exceeds 10,000 rows.");
            records.Add((row[swapChain], row[dropped] == "1", row[mode], Interval(row[present]), Interval(row[display])));
        }
        var chains = records.GroupBy(item => item.Chain, StringComparer.Ordinal).Select(group =>
        {
            var presents = group.Where(item => item.Present is > 0).Select(item => item.Present!.Value).ToArray();
            var displays = group.Where(item => !item.Dropped && item.Display is > 0).Select(item => item.Display!.Value).ToArray();
            return new SwapChainObservation(group.Key, group.Count(), group.Count(item => !item.Dropped), group.Count(item => item.Dropped),
                group.Select(item => item.Mode).Distinct(StringComparer.Ordinal).ToArray(), presents.Length == 0 ? null : LatencySummary.From(presents), displays.Length == 0 ? null : LatencySummary.From(displays));
        }).ToArray();
        return new(records.Count == 0 ? "no-events" : chains.Any(item => item.DisplayChangeIntervals is not null) ? "display-events-captured" : "presents-without-display-intervals", records.Count, chains);
    }

    private static double? Interval(string value)
    {
        if (value is "NA" or "") return null;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !double.IsFinite(result) || result < 0)
            throw new FormatException("The frame capture has an invalid interval.");
        return result;
    }
}
