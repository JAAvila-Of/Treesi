using Treesi.Domain;

namespace Treesi.Desktop.Graph;

public sealed record GraphSegment(int From, int To, bool Incoming, bool Through);
public sealed record GraphRow(GraphCommit Commit, int Lane, int LaneCount, IReadOnlyList<GraphSegment> Segments, string References, bool Last, bool Continues)
{
    public string Subject => HistoryCommitItem.Preview(Commit.Subject, 256);
    public string Author => HistoryCommitItem.Preview(Commit.Author, 80);
    public string Identity => Commit.CommitId[..8];
    public string Date => Commit.AuthoredAt.Length >= 10 ? Commit.AuthoredAt[..10] : Commit.AuthoredAt;
    public string AccessibleName => $"{Subject}. {Identity}. {Commit.Parents.Count} parents. {Author}.";
    public string ReferenceLabel => HistoryCommitItem.Preview(References, 80);
}

public sealed record GraphReferenceChoice(GraphReference Reference)
{
    public string Name => GraphLayout.ShortReference(Reference.Name);
    public string Kind => Reference.Kind;
}

public static class GraphLayout
{
    public const int MaximumLanes = 12;
    public static IReadOnlyList<GraphRow> Create(CommitGraphPage page)
    {
        var lanes = new List<string?>(); var rows = new List<GraphRow>(); var width = 1;
        foreach (var commit in page.Commits)
        {
            var before = lanes.ToArray(); var lane = lanes.IndexOf(commit.CommitId);
            if (lane < 0) lane = Allocate(commit.CommitId);
            var segments = new List<GraphSegment>();
            for (var i = 0; i < before.Length; i++)
                if (before[i] is not null && i != lane) segments.Add(new(i, i, false, true));
            if (lane < before.Length && before[lane] == commit.CommitId) segments.Add(new(lane, lane, true, false));
            lanes[lane] = null;
            foreach (var parent in commit.Parents)
            {
                var target = lanes.IndexOf(parent);
                if (target < 0)
                {
                    if (lanes[lane] is null) { lanes[lane] = parent; target = lane; }
                    else target = Allocate(parent);
                }
                segments.Add(new(lane, target, false, false));
            }
            width = Math.Max(width, lanes.Count);
            while (lanes.Count > 0 && lanes[^1] is null) lanes.RemoveAt(lanes.Count - 1);
            var refs = string.Join(" · ", page.Scope.References.Where(item => item.CommitId == commit.CommitId).Select(item => ShortReference(item.Name)));
            rows.Add(new(commit, lane, width, segments, refs, false, false));
        }
        return rows.Select((row, index) => row with { LaneCount = width, Last = index == rows.Count - 1, Continues = index == rows.Count - 1 && lanes.Any(item => item is not null) }).ToArray();

        int Allocate(string id)
        {
            var slot = lanes.IndexOf(null);
            if (slot >= 0) { lanes[slot] = id; return slot; }
            if (lanes.Count == MaximumLanes) throw new InspectionException("graph_complexity", "More than 12 simultaneous lanes are needed. Select a reference to inspect a narrower history.");
            lanes.Add(id); return lanes.Count - 1;
        }
    }

    public static string ShortReference(string name) => name.StartsWith("refs/heads/", StringComparison.Ordinal) ? name[11..] :
        name.StartsWith("refs/remotes/", StringComparison.Ordinal) ? name[13..] : name.StartsWith("refs/tags/", StringComparison.Ordinal) ? "◇ " + name[10..] : name;
}
