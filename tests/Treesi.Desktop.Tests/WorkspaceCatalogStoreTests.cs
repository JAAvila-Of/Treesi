using System.Text.Json;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Desktop.Workspaces;
using Treesi.Domain;

namespace Treesi.Desktop.Tests;

[TestFixture]
public sealed class WorkspaceCatalogStoreTests
{
    private static string Root(string name) => Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "catalog-store", name);

    [Test]
    public void JsonCatalogRoundTripsOnlyGroupMetadataAndSelection()
    {
        var directory = Directory.CreateTempSubdirectory("treesi-catalog-");
        var path = Path.Combine(directory.FullName, "groups.json"); var store = new JsonWorkspaceCatalogStore(path);
        try
        {
            store.Load().CanSave.Test().BeTrue();
            var catalog = new WorkspaceCatalog(store); var group = catalog.Create("Studio");
            catalog.AddRepository(group.Id, Root("uninspected"));
            var loaded = new WorkspaceCatalog(store);
            loaded.Document.SelectedWorkspaceId.Test().Be(group.Id);
            loaded.Document.Workspaces.Single().RepositoryRoots.Single().Test().Be(Root("uninspected"));
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            json.RootElement.GetProperty("Version").GetInt32().Test().Be(1);
            var stored = json.RootElement.GetProperty("Catalog").GetProperty("Workspaces")[0];
            string.Join(',', stored.EnumerateObject().Select(item => item.Name)).Test().Be("Id,Name,RepositoryRoots");
            Directory.GetFiles(directory.FullName, "*.tmp").Length.Test().Be(0);
        }
        finally { directory.Delete(true); }
    }

    [Test]
    public void CorruptFutureAndOversizedCatalogsArePreservedWhileNewGroupsStayInMemory()
    {
        var directory = Directory.CreateTempSubdirectory("treesi-catalog-"); var path = Path.Combine(directory.FullName, "groups.json");
        try
        {
            foreach (var invalid in new[]
            {
                "not json", "null", "{\"Version\":99,\"Catalog\":{\"Workspaces\":[]}}",
                "{\"Version\":1,\"Catalog\":null}", "{\"Version\":1,\"Catalog\":{\"Workspaces\":null}}",
                "{\"Version\":1,\"Catalog\":{\"Workspaces\":[null]}}", new string(' ', JsonWorkspaceCatalogStore.MaximumBytes + 1)
            })
            {
                File.WriteAllText(path, invalid);
                var store = new JsonWorkspaceCatalogStore(path); store.Load().CanSave.Test().BeFalse();
                var catalog = new WorkspaceCatalog(store); catalog.Create("Temporary");
                catalog.SessionOnly.Test().BeTrue(); File.ReadAllText(path).Test().Be(invalid);
            }
        }
        finally { directory.Delete(true); }
    }

    [Test]
    public void SizeRefusalAndUnwritableDestinationKeepExistingDataAndCleanTemporaryFiles()
    {
        var directory = Directory.CreateTempSubdirectory("treesi-catalog-"); var path = Path.Combine(directory.FullName, "groups.json");
        try
        {
            var store = new JsonWorkspaceCatalogStore(path); var catalog = new WorkspaceCatalog(store); catalog.Create("Saved");
            var original = File.ReadAllText(path);
            var large = new WorkspaceCatalogDocument(Enumerable.Range(0, 16).Select(i => new LocalWorkspace(Guid.NewGuid(), "Group " + i, [Root(i + new string('界', 3500))])).ToArray());
            store.Save(large).Test().BeFalse(); File.ReadAllText(path).Test().Be(original);
            var blocked = Path.Combine(directory.FullName, "blocked.json"); Directory.CreateDirectory(blocked);
            new JsonWorkspaceCatalogStore(blocked).Save(catalog.Document).Test().BeFalse();
            Directory.Exists(blocked).Test().BeTrue(); Directory.GetFiles(directory.FullName, "*.tmp").Length.Test().Be(0);
        }
        finally { directory.Delete(true); }
    }
}
