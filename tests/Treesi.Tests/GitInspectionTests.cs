using System.Diagnostics.Metrics;
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
public sealed class GitInspectionTests
{
    [Test]
    public async Task DirectFileOpenRetainsItsRepositoryPrefix()
    {
        using var fixture = new GitFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "src"));
        fixture.Write("src/nested file.txt", "new file\n");
        var snapshot = await Service().InspectAsync(Path.Combine(fixture.Root, "src", "nested file.txt"));
        snapshot.SelectedPath.Test().Be("src/nested file.txt");
    }

    private static IInspectionService Service() => new InstrumentedInspectionService(new InspectionService(new GitInspector()));
    private static Task<int> Cli(string[] args, TextWriter output) => CommandLine.RunAsync(args, output, Service());

    [Test]
    public async Task StatusDistinguishesStagedWorkingAndUntrackedChanges()
    {
        using var fixture = new GitFixture();
        fixture.Write("sample.txt", "first\nstaged\nlast\n"); fixture.Git("add", "sample.txt");
        fixture.Write("sample.txt", "first\nworking\nlast\n"); fixture.Write("new file ü.txt", "new\n");
        var snapshot = await Service().InspectAsync(Path.Combine(fixture.Root, "sample.txt"));
        snapshot.SelectedPath.Test().Be("sample.txt");
        var modified = snapshot.Files.Single(file => file.Path == "sample.txt");
        modified.IndexStatus.Test().Be("M"); modified.WorkingTreeStatus.Test().Be("M");
        snapshot.Files.Single(file => file.Path == "new file ü.txt").IsUntracked.Test().BeTrue();
    }

    [TestCase(Comparison.WorkingTree, "staged", "working")]
    [TestCase(Comparison.Staged, "old", "staged")]
    [TestCase(Comparison.Head, "old", "working")]
    public async Task ComparisonsUseTheCorrectEndpoints(Comparison comparison, string removed, string added)
    {
        using var fixture = new GitFixture();
        fixture.Write("sample.txt", "first\nstaged\nlast\n"); fixture.Git("add", "sample.txt");
        fixture.Write("sample.txt", "first\nworking\nlast\n");
        var result = await Service().DiffAsync(fixture.Root, "sample.txt", comparison);
        result.Hunks.Count.Test().Be(1);
        var lines = result.Hunks.Single().Lines;
        lines.Any(line => line.Kind == DiffLineKind.Removed && line.Text == removed && line.OldLine == 2).Test().BeTrue();
        lines.Any(line => line.Kind == DiffLineKind.Added && line.Text == added && line.NewLine == 2).Test().BeTrue();
        result.ContentTrust.Test().Be("untrusted");
    }

    [Test]
    public async Task PathsWithPathspecCharactersAreLiteral()
    {
        using var fixture = new GitFixture();
        fixture.Write("[a].txt", "one\n"); fixture.Write("a.txt", "unrelated\n");
        fixture.Git("add", "--", "."); fixture.Git("-c", "commit.gpgSign=false", "commit", "-m", "test: add literal paths");
        fixture.Write("[a].txt", "two\n"); fixture.Write("a.txt", "DO NOT INCLUDE\n");
        var result = await Service().DiffAsync(fixture.Root, "[a].txt", Comparison.WorkingTree);
        result.Patch.Test().Contain("+two").NotContain("DO NOT INCLUDE");
    }

    [Test]
    public async Task InspectionDoesNotChangeIndexConfigHeadOrWorktree()
    {
        using var fixture = new GitFixture(); fixture.Write("sample.txt", "changed\n");
        var paths = new[] { ".git/index", ".git/config", ".git/HEAD", ".git/refs/heads/main", "sample.txt" };
        var before = paths.ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Root, path)))));
        await Service().DiffAsync(fixture.Root, "sample.txt", Comparison.WorkingTree);
        foreach (var path in paths) Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Root, path)))).Test().Be(before[path]);
        File.Exists(Path.Combine(fixture.Root, ".git/index.lock")).Test().BeFalse();
    }

    [Test]
    public async Task ExecutableFiltersAreRefusedBeforeStatusOrDiff()
    {
        using var fixture = new GitFixture();
        fixture.Git("config", "filter.evil.clean", "touch filter-marker");
        fixture.Write(".gitattributes", "*.txt filter=evil\n"); fixture.Write("sample.txt", "changed\n");
        Func<Task> action = () => Service().InspectAsync(fixture.Root);
        var assertion = await action.Test().ThrowAsync<InspectionException>();
        assertion.Which(error => error.Code.Test().Be("unsafe_configuration"));
        File.Exists(Path.Combine(fixture.Root, "filter-marker")).Test().BeFalse();
    }

    [Test]
    public async Task ExternalDiffTextconvAndFsmonitorAreNotExecuted()
    {
        using var fixture = new GitFixture();
        fixture.Git("config", "diff.external", "touch external-marker");
        fixture.Git("config", "diff.evil.textconv", "touch textconv-marker");
        fixture.Git("config", "core.fsmonitor", "touch fsmonitor-marker");
        fixture.Write(".gitattributes", "*.txt diff=evil\n"); fixture.Write("sample.txt", "changed\n");
        var result = await Service().DiffAsync(fixture.Root, "sample.txt", Comparison.WorkingTree);
        result.Patch.Test().Contain("+changed");
        foreach (var name in new[] { "external-marker", "textconv-marker", "fsmonitor-marker" })
            File.Exists(Path.Combine(fixture.Root, name)).Test().BeFalse();
    }

    [TestCase("../outside.txt")]
    [TestCase("../../outside.txt")]
    public async Task DiffCannotEscapeTheRepository(string path)
    {
        using var fixture = new GitFixture();
        Func<Task> action = () => Service().DiffAsync(fixture.Root, path, Comparison.WorkingTree);
        var assertion = await action.Test().ThrowAsync<InspectionException>();
        assertion.Which(error => error.Code.Test().Be("invalid_path"));
    }

    [Test]
    public async Task NewFilePreviewPreservesNoFinalNewlineAndDoesNotStage()
    {
        using var fixture = new GitFixture(); fixture.Write("new.txt", "first\nlast");
        var result = await Service().DiffAsync(fixture.Root, "new.txt", Comparison.WorkingTree);
        result.Patch.Test().Contain("\\ No newline at end of file");
        result.Hunks.Single().Lines.Count(line => line.Kind == DiffLineKind.Added).Test().Be(2);
        fixture.Git("diff", "--cached", "--name-only").Test().BeEmpty();
    }

    [Test]
    public async Task NewBinaryFileIsNotParsedAsText()
    {
        using var fixture = new GitFixture(); File.WriteAllBytes(Path.Combine(fixture.Root, "binary.bin"), [1, 0, 2]);
        var result = await Service().DiffAsync(fixture.Root, "binary.bin", Comparison.WorkingTree);
        result.IsBinary.Test().BeTrue(); result.Hunks.Count.Test().Be(0);
    }

    [Test]
    public async Task LargeNewFileFailsExplicitly()
    {
        using var fixture = new GitFixture(); fixture.Write("large.txt", new string('x', 1048577));
        Func<Task> action = () => Service().DiffAsync(fixture.Root, "large.txt", Comparison.WorkingTree);
        var assertion = await action.Test().ThrowAsync<InspectionException>();
        assertion.Which(error => error.Code.Test().Be("output_limit"));
    }

    [Test]
    public async Task CancellationIsNotReportedAsAnEmptyDiff()
    {
        using var fixture = new GitFixture(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Func<Task> action = () => Service().InspectAsync(fixture.Root, cancellation.Token);
        await action.Test().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task CliReturnsVersionedStructuredDiff()
    {
        using var fixture = new GitFixture(); fixture.Write("sample.txt", "new\n"); using var output = new StringWriter();
        (await Cli(["diff", fixture.Root, "sample.txt"], output)).Test().Be(0);
        using var json = JsonDocument.Parse(output.ToString()); var root = json.RootElement;
        root.GetProperty("schemaVersion").GetString().Test().Be("1"); root.GetProperty("ok").GetBoolean().Test().BeTrue();
        root.GetProperty("data").GetProperty("contentTrust").GetString().Test().Be("untrusted");
        root.GetProperty("data").GetProperty("hunks").GetArrayLength().Test().BePositive();
        root.GetProperty("error").ValueKind.Test().Be(JsonValueKind.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CliUsageErrorsAreMachineReadable(bool invalidComparison)
    {
        string[] arguments = invalidComparison ? ["diff", ".", "a", "--comparison", "typo"] : ["unknown"];
        using var output = new StringWriter(); (await Cli(arguments, output)).Test().Be(2);
        using var json = JsonDocument.Parse(output.ToString());
        json.RootElement.GetProperty("ok").GetBoolean().Test().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Test().Be("usage");
    }

    [Test]
    public async Task CapabilitiesDoNotNeedARepository()
    {
        using var output = new StringWriter(); (await Cli(["capabilities"], output)).Test().Be(0);
        using var json = JsonDocument.Parse(output.ToString());
        json.RootElement.GetProperty("data").GetProperty("readOnly").GetBoolean().Test().BeTrue();
    }

    [Test]
    public async Task MetricsExposeNoRepositoryPathsOrContents()
    {
        using var fixture = new GitFixture(); var measurements = new List<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) => { if (instrument.Meter.Name == InspectionTelemetry.Name) meterListener.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) => measurements.Add(tags.ToArray())); listener.Start();
        await Service().InspectAsync(fixture.Root);
        measurements.Count.Test().BePositive();
        foreach (var tag in measurements.SelectMany(item => item)) (tag.Key is "operation" or "outcome").Test().BeTrue();
    }

    [Test]
    public void RenameRecordsKeepOriginalPathsAndEmbeddedNewlines()
    {
        var files = StatusParser.Parse("2 R. N... 100644 100644 100644 abc def R100 new name\n.txt\0old name.txt\0");
        files.Count.Test().Be(1); files.Single().Path.Test().Be("new name\n.txt"); files.Single().OriginalPath.Test().Be("old name.txt");
    }

    [Test]
    public async Task MissingFilesAreNotReportedAsUnchanged()
    {
        using var fixture = new GitFixture();
        Func<Task> action = () => Service().DiffAsync(fixture.Root, "missing.txt", Comparison.WorkingTree);
        var assertion = await action.Test().ThrowAsync<InspectionException>();
        assertion.Which(error => error.Code.Test().Be("path_not_found"));
    }

    [Test]
    public async Task HeadComparisonRefusesUnbornRepositoriesEvenForNewFiles()
    {
        using var fixture = new GitFixture(); fixture.Git("update-ref", "-d", "refs/heads/main");
        fixture.Git("read-tree", "--empty");
        Func<Task> action = () => Service().DiffAsync(fixture.Root, "sample.txt", Comparison.Head);
        var assertion = await action.Test().ThrowAsync<InspectionException>();
        assertion.Which(error => error.Code.Test().Be("unborn_head"));
    }

    [Test]
    public async Task EmptyNewFilesHaveNoInventedHunks()
    {
        using var fixture = new GitFixture(); fixture.Write("empty.txt", "");
        var result = await Service().DiffAsync(fixture.Root, "empty.txt", Comparison.WorkingTree);
        result.IsBinary.Test().BeFalse(); result.Hunks.Count.Test().Be(0);
    }

    [Test]
    public async Task TrackedBinaryFilesAreIdentified()
    {
        using var fixture = new GitFixture();
        File.WriteAllBytes(Path.Combine(fixture.Root, "binary.bin"), [0, 1, 2]);
        fixture.Git("add", "binary.bin");
        File.WriteAllBytes(Path.Combine(fixture.Root, "binary.bin"), [0, 3, 4]);
        var result = await Service().DiffAsync(fixture.Root, "binary.bin", Comparison.WorkingTree);
        result.IsBinary.Test().BeTrue(); result.Hunks.Count.Test().Be(0);
    }

    [Test]
    public async Task InvalidUtf8FailsWithoutReplacingBytes()
    {
        using var fixture = new GitFixture(); File.WriteAllBytes(Path.Combine(fixture.Root, "invalid.txt"), [0xFF, 0xFE, 0x61]);
        Func<Task> action = () => Service().DiffAsync(fixture.Root, "invalid.txt", Comparison.WorkingTree);
        var assertion = await action.Test().ThrowAsync<InspectionException>();
        assertion.Which(error => error.Code.Test().Be("unsupported_encoding"));
    }
}
