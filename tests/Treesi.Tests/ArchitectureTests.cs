using JAAvila.FluentOperations;
using JAAvila.FluentOperations.Architecture;
using NUnit.Framework;
using Treesi.Application;
using Treesi.Domain;
using Treesi.Git;
using Treesi.Cli;

namespace Treesi.Tests;

[TestFixture]
public sealed class ArchitectureTests
{
    [OneTimeSetUp]
    public void EnableDeepDependencyScanning() => ArchitectureScannerConfig.UseCecilDependencyScanning();

    [OneTimeTearDown]
    public void ResetDeepDependencyScanning() => ArchitectureScannerConfig.Reset();

    [Test]
    public void DomainHasNoApplicationOrAdapterDependencies()
    {
        var domain = typeof(FileDiff).Assembly;
        foreach (var forbidden in new[] { "Treesi.Application", "Treesi.Git", "Treesi.Cli", "Treesi.Desktop", "Avalonia" })
            domain.Test().NotReferenceAssembly(forbidden);
        foreach (var type in domain.GetTypes().Where(type => type.IsPublic))
            type.Test().OnlyHaveDependenciesOn("Treesi.Domain");
    }

    [Test]
    public void ApplicationDependsOnlyOnItsPortsAndDomain()
    {
        foreach (var type in typeof(InspectionService).Assembly.GetTypes().Where(type => type.IsPublic))
            type.Test().OnlyHaveDependenciesOn("Treesi.Domain", "Treesi.Application");
        typeof(InspectionService).Test().ImplementInterface<IInspectionService>();
        typeof(InstrumentedInspectionService).Test().ImplementInterface<IInspectionService>();
        typeof(GitInspector).Test().ImplementInterface<IRepositoryReader>();
        typeof(CommandLine).Test().NotHaveDependencyOn("Treesi.Git");
    }

    [Test]
    public async Task UseCasesCanRunWithoutGitOrDesktop()
    {
        var adapter = new InMemoryReader();
        IInspectionService service = new InspectionService(adapter);
        var snapshot = await service.InspectAsync("fixture");
        snapshot.Root.Test().Be("fixture");
        adapter.Calls.Test().Be(1);
    }

    private sealed class InMemoryReader : IRepositoryReader
    {
        public int Calls { get; private set; }
        public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new RepositorySnapshot(path, null, []));
        }
        public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null) =>
            Task.FromResult(new FileDiff(path, comparison, "", false, []));
    }
}
