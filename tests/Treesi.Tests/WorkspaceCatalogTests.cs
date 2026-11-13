using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Domain;

namespace Treesi.Tests;

[TestFixture]
public sealed class WorkspaceCatalogTests
{
    private static string Root(string name) => Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "workspace-catalog", name);

    [Test]
    public void GroupsHaveStableIdentitiesAndIndependentMembershipWithoutRepositoryAccess()
    {
        var store = new Store(); var catalog = new WorkspaceCatalog(store);
        var studio = catalog.Create(" Studio "); catalog.AddRepository(studio.Id, Root("shared"));
        var personal = catalog.Create("Personal"); catalog.AddRepository(personal.Id, Root("shared"));
        catalog.Select(studio.Id); catalog.Rename(studio.Id, "Research");
        catalog.Document.SelectedWorkspaceId.Test().Be(studio.Id);
        catalog.Document.Workspaces[0].Name.Test().Be("Research");
        var saves = store.Saves;
        catalog.AddRepository(studio.Id, Root("shared") + Path.DirectorySeparatorChar).Test().BeFalse();
        store.Saves.Test().Be(saves);
        catalog.RemoveRepository(studio.Id, Root("shared"));
        catalog.Document.Workspaces[0].RepositoryRoots.Count.Test().Be(0);
        catalog.Document.Workspaces[1].RepositoryRoots.Single().Test().Be(Root("shared"));
        var next = new WorkspaceCatalog(store);
        next.Document.SelectedWorkspaceId.Test().Be(studio.Id);
        next.Document.Workspaces[0].Id.Test().Be(studio.Id);
        next.Remove(studio.Id);
        next.Document.SelectedWorkspaceId.Test().BeNull();
        next.Document.Workspaces.Single().Id.Test().Be(personal.Id);
        store.Loads.Test().Be(2);
    }

    [Test]
    public void NamesLimitsAndRootReplacementRejectInvalidChangesWithoutTruncation()
    {
        var catalog = new WorkspaceCatalog(); var first = catalog.Create("Studio");
        foreach (var name in new[] { "", "  ", "studio", "Recent repositories", "line\nfeed", new string('x', 65) })
        { Action invalid = () => catalog.Create(name); invalid.Test().Throw<InspectionException>(); }
        catalog.AddRepository(first.Id, Root("first")); catalog.AddRepository(first.Id, Root("second"));
        Action duplicate = () => catalog.ReplaceRepository(first.Id, Root("first"), Root("second")); duplicate.Test().Throw<InspectionException>();
        catalog.Document.Workspaces[0].RepositoryRoots[0].Test().Be(Root("first"));
        catalog.ReplaceRepository(first.Id, Root("first"), Root("located"));
        catalog.Document.Workspaces[0].RepositoryRoots[0].Test().Be(Root("located"));
        foreach (var root in new[] { "relative", Root("bad") + "\0", new string('x', 4097) })
        { Action invalid = () => catalog.AddRepository(first.Id, root); invalid.Test().Throw<InspectionException>(); }
        for (var i = 2; i < WorkspaceCatalog.MaximumRepositories; i++) catalog.AddRepository(first.Id, Root("repo-" + i));
        Action overRoots = () => catalog.AddRepository(first.Id, Root("overflow")); overRoots.Test().Throw<InspectionException>();
        for (var i = 1; i < WorkspaceCatalog.MaximumWorkspaces; i++) catalog.Create("Group " + i);
        Action overGroups = () => catalog.Create("Overflow"); overGroups.Test().Throw<InspectionException>();
        catalog.Document.Workspaces.Count.Test().Be(16); catalog.Document.Workspaces[0].RepositoryRoots.Count.Test().Be(32);
    }

    [Test]
    public void LoadedSnapshotsAreDefensiveAndRejectInconsistentIdentityOrSelection()
    {
        var roots = new List<string> { Root("one") }; var id = Guid.NewGuid();
        var groups = new List<LocalWorkspace> { new(id, "One", roots) };
        var catalog = new WorkspaceCatalog(new Store { Document = new(groups, id) });
        roots.Clear(); groups.Clear();
        catalog.Document.Workspaces.Single().RepositoryRoots.Single().Test().Be(Root("one"));
        ((ICollection<LocalWorkspace>)catalog.Document.Workspaces).IsReadOnly.Test().BeTrue();
        ((ICollection<string>)catalog.Document.Workspaces[0].RepositoryRoots).IsReadOnly.Test().BeTrue();
        foreach (var document in new WorkspaceCatalogDocument[]
        {
            new([new(Guid.Empty, "Invalid", [])]), new([new(id, "One", []), new(id, "Two", [])]),
            new([new(id, "One", []), new(Guid.NewGuid(), "one", [])]), new([], id),
            new([new(id, "One", [Root("duplicate"), Root("duplicate")])]), new([new(id, "One", ["relative"])])
        })
        { Action invalid = () => WorkspaceCatalog.Normalize(document); invalid.Test().Throw<InspectionException>(); }
    }

    [Test]
    public void FailedSavesKeepSessionStateAndBlockedLoadsNeverOverwriteTheirStore()
    {
        var store = new Store { SaveSucceeds = false }; var catalog = new WorkspaceCatalog(store);
        catalog.Create("Session only");
        catalog.SessionOnly.Test().BeTrue(); catalog.Document.Workspaces.Count.Test().Be(1);
        store.Document.Workspaces.Count.Test().Be(0);
        var protectedStore = new Store { CanSave = false };
        var protectedCatalog = new WorkspaceCatalog(protectedStore);
        var group = protectedCatalog.Create("Temporary"); protectedCatalog.AddRepository(group.Id, Root("local"));
        protectedCatalog.SessionOnly.Test().BeTrue(); protectedStore.Saves.Test().Be(0);
        protectedStore.Document.Workspaces.Count.Test().Be(0);
    }

    private sealed class Store : IWorkspaceCatalogStore
    {
        public WorkspaceCatalogDocument Document { get; set; } = WorkspaceCatalogDocument.Empty;
        public bool CanSave { get; init; } = true;
        public bool SaveSucceeds { get; init; } = true;
        public int Loads { get; private set; }
        public int Saves { get; private set; }
        public WorkspaceCatalogLoad Load() { Loads++; return new(Document, CanSave); }
        public bool Save(WorkspaceCatalogDocument document) { Saves++; if (!SaveSucceeds) return false; Document = document; return true; }
    }
}
