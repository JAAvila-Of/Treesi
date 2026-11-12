using System.Security.Cryptography;
using System.Text.Json;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Cli;
using Treesi.Domain;
using Treesi.Git;

namespace Treesi.Tests;

[TestFixture]
public sealed class DiffOptionsTests
{
    [TestCase(0)]
    [TestCase(3)]
    [TestCase(10)]
    [TestCase(50)]
    public async Task ContextMatchesGitAndPreservesSourceNumbersWithoutWritingTheIndex(int context)
    {
        using var fixture = new GitFixture();
        var lines = Enumerable.Range(1, 30).Select(index => "line " + index).ToArray();
        fixture.Write("sample.txt", string.Join('\n', lines) + "\n"); fixture.Git("add", "sample.txt");
        fixture.Git("-c", "commit.gpgSign=false", "-c", "core.hooksPath=", "commit", "-m", "test: create context fixture");
        lines[9] = "changed 10"; lines[19] = "changed 20";
        fixture.Write("sample.txt", string.Join('\n', lines) + "\n");
        fixture.Git("config", "diff.context", "25");
        fixture.Git("config", "diff.interHunkContext", "100");
        var expected = fixture.Git("diff", "--no-ext-diff", "--no-textconv", "--no-color", "--no-renames", "--ignore-submodules=all",
            "--inter-hunk-context=0", "--unified=" + context, "--", "sample.txt");
        fixture.Git("config", "diff.outputIndicatorNew", "x");
        fixture.Git("config", "diff.outputIndicatorOld", "y");
        fixture.Git("config", "diff.outputIndicatorContext", "!");
        var indexBefore = SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Root, ".git", "index")));
        var options = new DiffOptions(context);
        var result = await new InspectionService(new GitInspector()).DiffAsync(fixture.Root, "sample.txt", Comparison.WorkingTree, options: options);
        result.Patch.Test().Be(expected); result.Options.Test().Be(options);
        result.Hunks.SelectMany(hunk => hunk.Lines).Where(line => line.Kind == DiffLineKind.Removed).Select(line => line.OldLine).SequenceEqual(new int?[] { 10, 20 }).Test().BeTrue();
        if (context == 0) result.Hunks.SelectMany(hunk => hunk.Lines).Any(line => line.Kind == DiffLineKind.Context).Test().BeFalse();
        SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Root, ".git", "index"))).SequenceEqual(indexBefore).Test().BeTrue();
    }

    [TestCase(WhitespaceMode.Exact, null, 4)]
    [TestCase(WhitespaceMode.IgnoreTrailingSpace, "--ignore-space-at-eol", 3)]
    [TestCase(WhitespaceMode.IgnoreSpaceChange, "--ignore-space-change", 2)]
    [TestCase(WhitespaceMode.IgnoreAllSpace, "--ignore-all-space", 0)]
    public async Task WhitespaceModesMatchGitIncludingSignificantSpacesInStrings(WhitespaceMode mode, string? flag, int changes)
    {
        using var fixture = new GitFixture();
        fixture.Write("sample.txt", "trailing\n    amount = 1;\nspace = 2;\ntext = \"a b\";\n"); fixture.Git("add", "sample.txt");
        fixture.Write("sample.txt", "trailing   \n  amount = 1;\nspace=2;\ntext = \"ab\";\n");
        var arguments = new List<string> { "diff", "--no-ext-diff", "--no-textconv", "--no-color", "--unified=3" };
        if (flag is not null) arguments.Add(flag);
        arguments.AddRange(["--", "sample.txt"]);
        var expected = fixture.Git(arguments.ToArray());
        var result = await new GitInspector().DiffAsync(fixture.Root, "sample.txt", Comparison.WorkingTree, options: new(Whitespace: mode));
        result.Patch.Test().Be(expected);
        result.Hunks.SelectMany(hunk => hunk.Lines).Count(line => line.Kind == DiffLineKind.Added).Test().Be(changes);
        if (changes == 0) result.Patch.Test().Be("");
    }

    [Test]
    public async Task NewWhitespaceOnlyFilesStillShowTheirAddedLines()
    {
        using var fixture = new GitFixture(); fixture.Write("new.txt", " \n\t\n");
        var options = new DiffOptions(0, WhitespaceMode.IgnoreAllSpace);
        var reader = new GitInspector();
        var untracked = await reader.DiffAsync(fixture.Root, "new.txt", Comparison.WorkingTree, options: options);
        fixture.Git("add", "--intent-to-add", "--", "new.txt");
        var git = await reader.DiffAsync(fixture.Root, "new.txt", Comparison.WorkingTree, options: options);
        untracked.Hunks.SelectMany(hunk => hunk.Lines).Where(line => line.Kind == DiffLineKind.Added).Select(line => line.Text)
            .SequenceEqual(git.Hunks.SelectMany(hunk => hunk.Lines).Where(line => line.Kind == DiffLineKind.Added).Select(line => line.Text)).Test().BeTrue();
        untracked.Hunks.Single().Lines.Count.Test().Be(2); untracked.Options.Test().Be(options);
    }

    [Test]
    public async Task ValidationRunsBeforeProviderIoAndTelemetryPreservesTypedOptions()
    {
        var reader = new Recorder(); var service = new InstrumentedInspectionService(new InspectionService(reader));
        foreach (var options in new[] { new DiffOptions(-1), new DiffOptions(51), new DiffOptions(Whitespace: (WhitespaceMode)99) })
        {
            Func<Task> call = () => service.DiffAsync("unused", "file.txt", Comparison.WorkingTree, options: options);
            var assertion = await call.Test().ThrowAsync<InspectionException>();
            assertion.Which(error => error.Code.Test().Be("invalid_diff_options"));
        }
        reader.Calls.Test().Be(0);
        var selected = new DiffOptions(50, WhitespaceMode.IgnoreTrailingSpace);
        await service.DiffAsync("unused", "file.txt", Comparison.Head, options: selected);
        reader.Options.Test().Be(selected); reader.Calls.Test().Be(1);
        Func<Task> direct = () => new GitInspector().DiffAsync("unused", "file.txt", Comparison.WorkingTree, options: new(-1));
        var invalid = await direct.Test().ThrowAsync<InspectionException>();
        invalid.Which(error => error.Code.Test().Be("invalid_diff_options"));
    }

    [TestCase("--context", "-1")]
    [TestCase("--context", "51")]
    [TestCase("--context", "1.5")]
    [TestCase("--context", "999999999999999999999")]
    [TestCase("--whitespace", "unknown")]
    [TestCase("--exec", "command")]
    public async Task InvalidCliOptionsReturnUsageWithoutProviderIo(string flag, string value)
    {
        var reader = new Recorder(); using var output = new StringWriter();
        (await CommandLine.RunAsync(["diff", "unused", "file.txt", flag, value], output, new InspectionService(reader))).Test().Be(2);
        reader.Calls.Test().Be(0);
        using var json = JsonDocument.Parse(output.ToString());
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Test().Be("usage");
    }

    [Test]
    public async Task CliRejectsDuplicateOrMissingValuesAndReportsEffectiveOptions()
    {
        var reader = new Recorder(); var service = new InspectionService(reader);
        foreach (var flags in new string[][] { ["--context"], ["--context", "3", "--context", "10"], ["--whitespace", "exact", "--whitespace", "ignore-all-space"] })
        {
            using var invalid = new StringWriter();
            (await CommandLine.RunAsync(["diff", "unused", "file.txt", .. flags], invalid, service)).Test().Be(2);
        }
        reader.Calls.Test().Be(0);
        using var output = new StringWriter();
        (await CommandLine.RunAsync(["diff", "unused", "file.txt", "--whitespace", "ignore-space-change", "--comparison", "staged", "--context", "0"], output, service)).Test().Be(0);
        reader.Options.Test().Be(new DiffOptions(0, WhitespaceMode.IgnoreSpaceChange));
        reader.Comparison.Test().Be(Comparison.Staged);
        using var json = JsonDocument.Parse(output.ToString());
        var options = json.RootElement.GetProperty("data").GetProperty("options");
        options.GetProperty("contextLines").GetInt32().Test().Be(0);
        options.GetProperty("whitespace").GetString().Test().Be("ignoreSpaceChange");
        using var capabilities = new StringWriter();
        (await CommandLine.RunAsync(["capabilities"], capabilities, service)).Test().Be(0);
        using var caps = JsonDocument.Parse(capabilities.ToString());
        caps.RootElement.GetProperty("data").GetProperty("diffOptions").GetProperty("context").GetProperty("maximum").GetInt32().Test().Be(50);
    }

    private sealed class Recorder : IRepositoryReader
    {
        public int Calls { get; private set; }
        public DiffOptions? Options { get; private set; }
        public Comparison Comparison { get; private set; }
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException("No repository should be inspected."); }
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null)
        {
            Calls++; Options = options; Comparison = comparison;
            return Task.FromResult(new FileDiff(path, comparison, "", false, [], Options: options));
        }
    }
}
