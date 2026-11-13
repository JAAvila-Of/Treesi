namespace Treesi.Domain;

public sealed record LocalWorkspace(Guid Id, string Name, IReadOnlyList<string> RepositoryRoots);
