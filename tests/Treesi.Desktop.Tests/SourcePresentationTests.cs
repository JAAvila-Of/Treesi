using System.Globalization;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class SourcePresentationTests
{
    [Test]
    public void MultipleEditsHaveSeparateRangesAndUnrelatedLinesKeepOnlyTheirLineBackground()
    {
        var budget = InlineChanges.CellBudget;
        var changes = InlineChanges.Compare("var limit = 10; return oldName;", "var limit = 20; return newName;", ref budget);
        (changes.Before.Count >= 2).Test().BeTrue(); (changes.After.Count >= 2).Test().BeTrue();
        InlineChanges.Compare("abc", "XYZ", ref budget).Test().Be(InlineChanges.Empty);
        InlineChanges.Compare("identical", "identical", ref budget).Test().Be(InlineChanges.Empty);
        InlineChanges.Compare("", "added", ref budget).Before.Count.Test().Be(0);
        (budget >= 0 && budget < InlineChanges.CellBudget).Test().BeTrue();
    }

    [TestCase("a👩🏽‍💻z", "a👨🏽‍💻z")]
    [TestCase("cafe\u0301!", "cafe\u0300!")]
    [TestCase("a🇵🇪z", "a🇨🇱z")]
    public void ChangedRangesNeverSplitUnicodeTextElements(string before, string after)
    {
        var budget = InlineChanges.CellBudget; var changes = InlineChanges.Compare(before, after, ref budget);
        foreach (var (text, spans) in new[] { (before, changes.Before), (after, changes.After) })
        {
            var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToHashSet();
            foreach (var span in spans) { boundaries.Contains(span.Start).Test().BeTrue(); boundaries.Contains(span.Start + span.Length).Test().BeTrue(); }
            spans.Count.Test().BePositive();
        }
    }

    [Test]
    public void ExhaustedBudgetUsesLinearFallbackAndLongLinesAreSkipped()
    {
        var budget = 0;
        var before = new string('a', 300) + "old" + new string('z', 300);
        var after = new string('a', 300) + "new" + new string('z', 300);
        var fallback = InlineChanges.Compare(before, after, ref budget);
        fallback.Before.Single().Test().Be(new TextSpan(300, 3)); fallback.After.Single().Test().Be(new TextSpan(300, 3)); budget.Test().Be(0);
        InlineChanges.Compare(new string('x', 4097), "short", ref budget).Test().Be(InlineChanges.Empty);
    }

    [Test]
    public void LexerKeepsCommentsStringsAndEscapesOutOfKeywordColors()
    {
        var lexer = new FragmentLexer(FragmentLanguage.CSharp);
        var text = "public string name = \"return \\\"true\\\"\"; // false 123";
        var tokens = lexer.Scan(text);
        tokens.Count(token => token.Kind == CodeTokenKind.Keyword).Test().Be(2);
        tokens.Single(token => token.Kind == CodeTokenKind.String).Start.Test().Be(text.IndexOf('"'));
        tokens.Last().Kind.Test().Be(CodeTokenKind.Comment);
        var escaped = lexer.Scan("var @class = 42;");
        escaped.Count(token => token.Kind == CodeTokenKind.Keyword).Test().Be(1);
        escaped.Single(token => token.Kind == CodeTokenKind.Number).Length.Test().Be(2);
    }

    [Test]
    public void MultilineVerbatimAndRawStringsKeepStateAndJsonHasOnlyItsOwnKeywords()
    {
        var lexer = new FragmentLexer(FragmentLanguage.CSharp);
        lexer.Scan("var text = @\"first");
        lexer.Scan("public \"\"value\"\"\"); return true;").Count(token => token.Kind == CodeTokenKind.Keyword).Test().Be(2);
        lexer.Scan("var text = \"\"\"");
        lexer.Scan("return 42;").Single().Kind.Test().Be(CodeTokenKind.String);
        lexer.Scan("\"\"\"; return false;").Count(token => token.Kind == CodeTokenKind.Keyword).Test().Be(2);
        var json = new FragmentLexer(FragmentLanguage.Json).Scan("{\"true\": false, \"value\": 123, \"type\": \"public\"}");
        json.Count(token => token.Kind == CodeTokenKind.Keyword).Test().Be(1);
        json.Count(token => token.Kind == CodeTokenKind.Number).Test().Be(1);
        new FragmentLexer(FragmentLanguage.Plain).Scan("return true;").Count.Test().Be(0);
    }

    [Test]
    public void BeforeAndAfterStatesStaySeparateAndResetAtHunkBoundaries()
    {
        var removed = new DiffLine(DiffLineKind.Removed, 1, null, "/* old comment");
        var added = new DiffLine(DiffLineKind.Added, null, 1, "var count = 42;");
        var context = new DiffLine(DiffLineKind.Context, 2, 2, "return true;");
        var later = new DiffLine(DiffLineKind.Removed, 30, null, "return false;");
        var diff = new FileDiff("sample.cs", Comparison.Revisions, "", false, [new("@@ -1,2 +1,2 @@", [removed, added, context]), new("@@ -30 +30 @@", [later])]);
        var presentation = DiffPresentation.Create(diff);
        presentation.ForLine(removed)!.Tokens.Single().Kind.Test().Be(CodeTokenKind.Comment);
        presentation.ForLine(added)!.Tokens.Any(token => token.Kind == CodeTokenKind.Number).Test().BeTrue();
        presentation.ForLine(context)!.Tokens.All(token => token.Kind == CodeTokenKind.Keyword).Test().BeTrue();
        presentation.ForLine(later)!.Tokens.Any(token => token.Kind == CodeTokenKind.Keyword).Test().BeTrue();
    }

    [Test]
    public void MetadataAndUnpairedLinesGetNoInlineRangesAndOversizedLexingStopsTheFragment()
    {
        var oldLine = new DiffLine(DiffLineKind.Removed, 1, null, "return oldValue;");
        var marker = new DiffLine(DiffLineKind.Metadata, null, null, "\\ No newline at end of file");
        var newLine = new DiffLine(DiffLineKind.Added, null, 1, "return newValue;");
        var extra = new DiffLine(DiffLineKind.Added, null, 2, "return true;");
        var presentation = DiffPresentation.Create(new("example.cs", Comparison.Revisions, "", false, [new("@@ -1 +1,2 @@", [oldLine, marker, newLine, extra])]));
        presentation.ForLine(marker).Test().BeNull(); presentation.ForLine(extra)!.Changes.Count.Test().Be(0);
        presentation.ForLine(newLine)!.Changes.Count.Test().BePositive();
        var lexer = new FragmentLexer(FragmentLanguage.CSharp); lexer.Scan(new string('a', 4097)); lexer.Scan("return true;").Count.Test().Be(0);
        FragmentLexer.Detect("unknown.txt").Test().Be(FragmentLanguage.Plain); FragmentLexer.Detect("settings.JSON").Test().Be(FragmentLanguage.Json);
    }

    [Test]
    public void TotalCharacterBudgetBoundsRetainedStylesAndJsonDoesNotUseCSharpKeywords()
    {
        var lines = Enumerable.Range(1, 60).Select(row => new DiffLine(DiffLineKind.Context, row, row, new string('a', 4096))).ToArray();
        var presentation = DiffPresentation.Create(new("bounded.cs", Comparison.Head, "", false, [new("@@ -1,60 +1,60 @@", lines)]));
        var retained = lines.Where(line => presentation.ForLine(line) is not null).Sum(line => line.Text.Length);
        (retained <= DiffPresentation.CharacterBudget).Test().BeTrue(); presentation.ForLine(lines[^1]).Test().BeNull();
        var json = new DiffLine(DiffLineKind.Added, null, 1, "{\"public\": true, \"limit\": 42}");
        var styled = DiffPresentation.Create(new("settings.json", Comparison.Head, "", false, [new("@@ -0,0 +1 @@", [json])])).ForLine(json)!;
        styled.Tokens.Count(token => token.Kind == CodeTokenKind.Keyword).Test().Be(1);
        styled.Tokens.Count(token => token.Kind == CodeTokenKind.String).Test().Be(2);
    }
}
