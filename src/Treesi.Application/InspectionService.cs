using Treesi.Domain;

namespace Treesi.Application;

public sealed class InspectionService(IRepositoryReader reader) : IInspectionService
{
    public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        ValidatePath(path);
        return reader.InspectAsync(path, cancellationToken);
    }

    public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default)
    {
        ValidatePath(repository);
        ValidatePath(path);
        if (!Enum.IsDefined(comparison)) throw new InspectionException("invalid_comparison", "The comparison is not supported.");
        return reader.DiffAsync(repository, path, comparison, cancellationToken);
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
            throw new InspectionException("invalid_path", "A non-empty path without NUL characters is required.");
    }
}
