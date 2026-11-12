namespace Treesi.Domain;

public enum RevisionComparisonMode { Direct, MergeBase }
public static class RevisionLimits
{
    public const int References = 200;
    public const int Files = 10000;
}

public sealed record RevisionSelection(string Base, string Target, RevisionComparisonMode Mode = RevisionComparisonMode.Direct)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) throw new InspectionException("invalid_revision_mode", "Choose direct or merge-base comparison.");
        ValidateName(Base); ValidateName(Target);
    }

    private static void ValidateName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.StartsWith('-') || value.Contains("..", StringComparison.Ordinal)
            || value.Contains("@{", StringComparison.Ordinal) || value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || ":{}[]?*\\".Contains(character)))
            throw new InspectionException("invalid_revision", "Use a local reference, commit ID, or bounded ~n/^n ancestry. Ranges, reflogs and searches are not supported.");
        var suffix = value.IndexOfAny(['~', '^']);
        if (suffix == 0) throw new InspectionException("invalid_revision", "A reference or commit ID must precede ancestry operators.");
        if (suffix < 0) return;
        var operators = 0;
        for (var index = suffix; index < value.Length;)
        {
            if (value[index++] is not ('~' or '^') || ++operators > 8)
                throw new InspectionException("invalid_revision", "Use at most eight ancestry operators, each with a value from 0 to 999.");
            var digits = 0;
            while (index < value.Length && value[index] is >= '0' and <= '9') { index++; digits++; }
            if (digits > 3) throw new InspectionException("invalid_revision", "Each ancestry value is limited to 999.");
        }
    }
}

public sealed record RevisionEndpoints(string Root, string BaseName, string BaseCommitId, string TargetName,
    string TargetCommitId, string EffectiveBaseCommitId, RevisionComparisonMode Mode, bool IsRootComparison = false)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Root) || Root.Contains('\0') || !Path.IsPathFullyQualified(Root)
            || !IsCommitId(BaseCommitId) || !IsCommitId(TargetCommitId) || !IsCommitId(EffectiveBaseCommitId)
            || !Enum.IsDefined(Mode) || (Mode == RevisionComparisonMode.Direct && EffectiveBaseCommitId != BaseCommitId))
            throw new InspectionException("invalid_revision_comparison", "A resolved comparison requires an absolute root and complete commit IDs.");
        new RevisionSelection(BaseName, TargetName, Mode).Validate();
        if (IsRootComparison && (Mode != RevisionComparisonMode.Direct || BaseCommitId != TargetCommitId || EffectiveBaseCommitId != TargetCommitId))
            throw new InspectionException("invalid_revision_comparison", "An initial-commit comparison must pin one commit and use the empty tree.");
    }

    public static bool IsCommitId(string? value) => value is { Length: 40 or 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed record RevisionReference(string Name, string ObjectId, string Kind)
{
    public override string ToString() => Name;
}
public sealed record RevisionReferences(IReadOnlyList<RevisionReference> Items, bool HasMore, int Limit = 200, string ContentTrust = "untrusted");
public sealed record RevisionFileChange(string Path, string Status);
public sealed record RevisionComparison(RevisionEndpoints Endpoints, IReadOnlyList<RevisionFileChange> Files, string ContentTrust = "untrusted");

public static class RevisionPath
{
    public static string Normalize(string root, string path)
    {
        if (!Path.IsPathFullyQualified(root) || string.IsNullOrWhiteSpace(path) || path.Contains('\0') || path.Contains('\\') || path.StartsWith('/') || Path.IsPathRooted(path)
            || path.Split('/').Any(part => part is "." or ".." or ""))
            throw new InspectionException("invalid_path", "Use a literal repository-relative Git file path with forward slashes, without traversal or backslashes.");
        return path;
    }
}
