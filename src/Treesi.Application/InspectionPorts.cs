using Treesi.Domain;

namespace Treesi.Application;

// Driving port for desktop, CLI, and future tool protocol adapters.
public interface IInspectionService
{
    Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default);
    Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null);
}

// Driven port implemented by a version-control provider.
public interface IRepositoryReader
{
    Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default);
    Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null);
}
