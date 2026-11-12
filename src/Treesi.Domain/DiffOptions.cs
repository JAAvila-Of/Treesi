namespace Treesi.Domain;

public enum WhitespaceMode { Exact, IgnoreTrailingSpace, IgnoreSpaceChange, IgnoreAllSpace }

public sealed record DiffOptions(int ContextLines = 3, WhitespaceMode Whitespace = WhitespaceMode.Exact, bool FullContext = false)
{
    public const int MaximumContextLines = 50;
    public static DiffOptions Default { get; } = new();

    public void Validate()
    {
        if (ContextLines is < 0 or > MaximumContextLines || !Enum.IsDefined(Whitespace))
            throw new InspectionException("invalid_diff_options", "Context must be between 0 and 50 lines and the whitespace mode must be supported.");
    }
}
