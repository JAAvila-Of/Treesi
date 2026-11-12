using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class SplitDiffTests
{
    [Test]
    public void UnequalReplacementsKeepContextAlignedAndOriginalNumbers()
    {
        var split = SplitDiff.Create(new FileDiff("sample", Comparison.WorkingTree, "", false,
        [new DiffHunk("@@ -10,3 +10,4 @@", [
            new DiffLine(DiffLineKind.Context, 10, 10, "context"),
            new DiffLine(DiffLineKind.Removed, 11, null, "old"),
            new DiffLine(DiffLineKind.Added, null, 11, "new"),
            new DiffLine(DiffLineKind.Added, null, 12, "extra"),
            new DiffLine(DiffLineKind.Context, 12, 13, "tail") ])]));
        Rows(split.Before.Text)[3].Test().Be("");
        Rows(split.After.Text)[3].Test().Be("+extra");
        Rows(split.Before.Text)[4].Test().Be(" tail");
        Rows(split.After.Text)[4].Test().Be(" tail");
        split.Before.Lines[5].OldLine!.Value.Test().Be(12);
        split.After.Lines[5].NewLine!.Value.Test().Be(13);
        split.Before.Lines.ContainsKey(4).Test().BeFalse();
    }

    [TestCase(DiffLineKind.Added)]
    [TestCase(DiffLineKind.Removed)]
    public void OneSidedChangesLeaveTheOppositeSideEmpty(DiffLineKind kind)
    {
        var split = SplitDiff.Create(new FileDiff("sample", Comparison.WorkingTree, "", false,
        [new DiffHunk("@@ -0,0 +1 @@", [new DiffLine(kind, kind == DiffLineKind.Removed ? 1 : null,
            kind == DiffLineKind.Added ? 1 : null, "content")])]));
        var absent = kind == DiffLineKind.Added ? split.Before : split.After;
        var present = kind == DiffLineKind.Added ? split.After : split.Before;
        Rows(absent.Text)[1].Test().Be("");
        present.Lines[2].Text.Test().Be("content");
        absent.Lines.Count.Test().Be(0);
    }

    [Test]
    public void MultipleHunksKeepGapsExplicitAndDoNotInventSourceLines()
    {
        var split = SplitDiff.Create(new FileDiff("sample", Comparison.Head, "", false,
        [new DiffHunk("@@ -1 +1 @@", [new DiffLine(DiffLineKind.Context, 1, 1, "first")]),
         new DiffHunk("@@ -900 +900 @@", [new DiffLine(DiffLineKind.Added, null, 900, "last")])]));
        string.Join(",", split.After.HunkLines).Test().Be("1,3");
        split.After.Lines[4].NewLine!.Value.Test().Be(900);
        Rows(split.Before.Text).Length.Test().Be(Rows(split.After.Text).Length);
    }

    private static string[] Rows(string text) => text.Replace("\r", "").Split('\n');

    [Test]
    public void MissingNewlineMetadataBelongsToItsOriginalSide()
    {
        var split = SplitDiff.Create(new FileDiff("sample", Comparison.WorkingTree, "", false,
        [new DiffHunk("@@ -1 +1 @@", [new DiffLine(DiffLineKind.Removed, 1, null, "old"),
            new DiffLine(DiffLineKind.Metadata, null, null, "\\ No newline at end of file"),
            new DiffLine(DiffLineKind.Added, null, 1, "new")])]));
        split.Before.Text.Contains("No newline", StringComparison.Ordinal).Test().BeTrue();
        split.After.Text.Contains("No newline", StringComparison.Ordinal).Test().BeFalse();
        split.Before.Lines[3].Kind.Test().Be(DiffLineKind.Metadata);
    }
}
