using System.Text.Json;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Desktop.Workspaces;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class WorkspaceHistoryTests
{
    private static string Root(string name) => Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace", name);

    [Test]
    public void HistoryIsBoundedDeduplicatedAndForgottenEntriesStayForgotten()
    {
        var history = new WorkspaceHistory(null);
        for (var i = 0; i < 12; i++) history.Remember(new WorkspaceEntry(Root("repo-" + i)));
        history.Entries.Count.Test().Be(8);
        var entry = new WorkspaceEntry(Root("repo-9"), "src/example.cs", Comparison.Head, true, false);
        history.Remember(entry);
        history.Entries[0].Test().Be(entry);
        history.Entries.Count.Test().Be(8);
        history.Forget(entry.Root);
        history.Update(entry);
        history.Entries.Any(item => item.Root == entry.Root).Test().BeFalse();
        history.Clear(); history.Update(entry);
        history.Entries.Count.Test().Be(0);
    }

    [Test]
    public void MetadataValidationRejectsUnusablePathsAndUnsupportedComparisons()
    {
        var valid = new WorkspaceEntry(Root("valid"), "src/example.cs");
        var entries = WorkspaceHistory.Normalize([
            null!, new("relative/path"), new(Root("bad") + "\0"), new(Root("bad"), "../escape.cs"),
            new(Root("bad"), "folder/../../escape.cs"), new(Root("bad"), "folder\\..\\escape.cs"),
            new(Root("bad"), Root("absolute.cs")), new(Root("bad"), "src/code.cs", (Comparison)99),
            new(new string('x', 4097)), valid, valid with { Root = valid.Root + Path.DirectorySeparatorChar }
        ]);
        entries.Count.Test().Be(1); entries[0].Test().Be(valid);
    }

    [Test]
    public void JsonStoreRoundTripsMetadataAndRejectsCorruptOversizedAndUnknownDocuments()
    {
        var directory = Directory.CreateTempSubdirectory("treesi-workspaces-");
        var path = Path.Combine(directory.FullName, "workspaces.json");
        var store = new JsonWorkspaceHistoryStore(path);
        try
        {
            store.Load().Count.Test().Be(0);
            var entry = new WorkspaceEntry(Root("uninspected"), "src/code.cs", Comparison.Staged, true, false);
            store.Save([entry]).Test().BeTrue(); store.Load().Single().Test().Be(entry);
            var oversized = Enumerable.Range(0, 8).Select(index => new WorkspaceEntry(Root(index + new string('界', 3900)), "src/" + new string('界', 3900))).ToArray();
            store.Save(oversized).Test().BeFalse(); store.Load().Single().Test().Be(entry);
            using (var document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                document.RootElement.GetProperty("Version").GetInt32().Test().Be(1);
                document.RootElement.GetProperty("Entries")[0].EnumerateObject().Count().Test().Be(5);
            }
            foreach (var invalid in new[] { "not json", "null", "{\"Version\":99,\"Entries\":[]}", "{\"Version\":1,\"Entries\":null}", new string(' ', JsonWorkspaceHistoryStore.MaximumBytes + 1) })
            {
                File.WriteAllText(path, invalid); store.Load().Count.Test().Be(0);
            }
            Directory.GetFiles(directory.FullName, "*.tmp").Length.Test().Be(0);
        }
        finally { directory.Delete(true); }
    }

    [Test]
    public void UnwritableDestinationDoesNotDestroyExistingSettingsOrLeaveTemporaryFiles()
    {
        var directory = Directory.CreateTempSubdirectory("treesi-workspaces-");
        var path = Path.Combine(directory.FullName, "blocked.json");
        Directory.CreateDirectory(path);
        try
        {
            var store = new JsonWorkspaceHistoryStore(path);
            store.Save([new WorkspaceEntry(Root("repo"))]).Test().BeFalse();
            Directory.Exists(path).Test().BeTrue();
            Directory.GetFiles(directory.FullName, "*.tmp").Length.Test().Be(0);
        }
        finally { directory.Delete(true); }
    }
}
