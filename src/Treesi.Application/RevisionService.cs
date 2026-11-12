using Treesi.Domain;

namespace Treesi.Application;

public interface IRevisionService
{
    Task<RevisionReferences> ReferencesAsync(string repository, CancellationToken cancellationToken = default);
    Task<RevisionEndpoints> ResolveAsync(string repository, RevisionSelection selection, CancellationToken cancellationToken = default);
    Task<RevisionComparison> CompareAsync(RevisionEndpoints endpoints, CancellationToken cancellationToken = default);
    Task<FileDiff> DiffAsync(RevisionEndpoints endpoints, string path, DiffOptions? options = null, CancellationToken cancellationToken = default);
}

public interface IRevisionReader
{
    Task<RevisionReferences> ReferencesAsync(string repository, CancellationToken cancellationToken = default);
    Task<RevisionEndpoints> ResolveAsync(string repository, RevisionSelection selection, CancellationToken cancellationToken = default);
    Task<RevisionComparison> CompareAsync(RevisionEndpoints endpoints, CancellationToken cancellationToken = default);
    Task<FileDiff> DiffAsync(RevisionEndpoints endpoints, string path, DiffOptions? options = null, CancellationToken cancellationToken = default);
}

public sealed class RevisionService(IRevisionReader reader) : IRevisionService
{
    private static void ValidateRepository(string repository)
    {
        if (string.IsNullOrWhiteSpace(repository) || repository.Contains('\0'))
            throw new InspectionException("invalid_path", "Choose a repository path without NUL characters.");
    }

    public Task<RevisionReferences> ReferencesAsync(string repository, CancellationToken cancellationToken = default)
    {
        ValidateRepository(repository);
        return reader.ReferencesAsync(repository, cancellationToken);
    }
    public async Task<RevisionEndpoints> ResolveAsync(string repository, RevisionSelection selection, CancellationToken cancellationToken = default)
    {
        ValidateRepository(repository); selection.Validate();
        var result = await reader.ResolveAsync(repository, selection, cancellationToken);
        result.Validate();
        if (result.BaseName != selection.Base || result.TargetName != selection.Target || result.Mode != selection.Mode)
            throw new InspectionException("invalid_revision_comparison", "The provider returned a different revision selection.");
        return result;
    }
    public async Task<RevisionComparison> CompareAsync(RevisionEndpoints endpoints, CancellationToken cancellationToken = default)
    {
        endpoints.Validate(); var result = await reader.CompareAsync(endpoints, cancellationToken);
        if (result.Endpoints != endpoints) throw new InspectionException("invalid_revision_comparison", "The provider returned a different comparison scope.");
        return result;
    }
    public async Task<FileDiff> DiffAsync(RevisionEndpoints endpoints, string path, DiffOptions? options = null, CancellationToken cancellationToken = default)
    {
        endpoints.Validate(); options ??= DiffOptions.Default; options.Validate();
        var normalized = RevisionPath.Normalize(endpoints.Root, path);
        var result = await reader.DiffAsync(endpoints, normalized, options, cancellationToken);
        if (result.Comparison != Comparison.Revisions || result.Revisions != endpoints || result.Options != options || result.Path != normalized)
            throw new InspectionException("invalid_revision_comparison", "The provider returned a diff for another scope or options.");
        return result;
    }
}

public sealed class InstrumentedRevisionService(IRevisionService inner) : IRevisionService
{
    public Task<RevisionReferences> ReferencesAsync(string repository, CancellationToken cancellationToken = default) =>
        InstrumentedInspectionService.MeasureAsync("references", () => inner.ReferencesAsync(repository, cancellationToken));
    public Task<RevisionEndpoints> ResolveAsync(string repository, RevisionSelection selection, CancellationToken cancellationToken = default) =>
        InstrumentedInspectionService.MeasureAsync("resolve-revisions", () => inner.ResolveAsync(repository, selection, cancellationToken));
    public Task<RevisionComparison> CompareAsync(RevisionEndpoints endpoints, CancellationToken cancellationToken = default) =>
        InstrumentedInspectionService.MeasureAsync("compare-revisions", () => inner.CompareAsync(endpoints, cancellationToken));
    public Task<FileDiff> DiffAsync(RevisionEndpoints endpoints, string path, DiffOptions? options = null, CancellationToken cancellationToken = default) =>
        InstrumentedInspectionService.MeasureAsync("revision-diff", () => inner.DiffAsync(endpoints, path, options, cancellationToken));
}
