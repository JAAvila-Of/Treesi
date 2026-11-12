namespace Treesi.Desktop;

public enum FragmentLanguage { Plain, CSharp, Json }
public enum CodeTokenKind { Keyword, String, Number, Comment }
public readonly record struct CodeToken(int Start, int Length, CodeTokenKind Kind);

// A finite lexer for visible fragments, not a semantic language parser.
public sealed class FragmentLexer(FragmentLanguage language)
{
    public const int MaximumLineLength = 4096;
    private bool _blockComment, _verbatim, _stopped;
    private int _rawQuotes;
    private static readonly HashSet<string> Keywords = new(("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while add alias and ascending async await by descending dynamic equals file from get global group init into join let managed nameof not notnull on or orderby partial record remove required scoped select set unmanaged value var when where with yield").Split(' '), StringComparer.Ordinal);

    public static FragmentLanguage Detect(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    { ".cs" or ".csx" => FragmentLanguage.CSharp, ".json" => FragmentLanguage.Json, _ => FragmentLanguage.Plain };

    public IReadOnlyList<CodeToken> Scan(string text)
    {
        if (_stopped || language == FragmentLanguage.Plain) return [];
        if (text.Length > MaximumLineLength) { _stopped = true; return []; }
        var tokens = new List<CodeToken>(); var i = 0;
        void Add(int start, CodeTokenKind kind) { if (i > start) tokens.Add(new(start, i - start, kind)); }
        while (i < text.Length)
        {
            var start = i;
            if (_blockComment)
            {
                var end = text.IndexOf("*/", i, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2; _blockComment = end < 0; Add(start, CodeTokenKind.Comment); continue;
            }
            if (_rawQuotes > 0)
            {
                while (i < text.Length)
                {
                    if (text[i++] != '"') continue;
                    var quotes = 1; while (i < text.Length && text[i] == '"') { i++; quotes++; }
                    if (quotes >= _rawQuotes) { _rawQuotes = 0; break; }
                }
                Add(start, CodeTokenKind.String); continue;
            }
            if (_verbatim)
            {
                while (i < text.Length)
                {
                    if (text[i++] != '"') continue;
                    if (i < text.Length && text[i] == '"') { i++; continue; }
                    _verbatim = false; break;
                }
                Add(start, CodeTokenKind.String); continue;
            }
            if (language == FragmentLanguage.CSharp && i + 1 < text.Length && text[i] == '/' && text[i + 1] is '/' or '*')
            {
                if (text[i + 1] == '/') { i = text.Length; Add(start, CodeTokenKind.Comment); continue; }
                _blockComment = true; i += 2;
                var end = text.IndexOf("*/", i, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2; _blockComment = end < 0; Add(start, CodeTokenKind.Comment); continue;
            }
            if (language == FragmentLanguage.CSharp && text[i] == '@' && i + 1 < text.Length && text[i + 1] == '"')
            {
                i += 2; _verbatim = true;
                while (i < text.Length)
                {
                    if (text[i++] != '"') continue;
                    if (i < text.Length && text[i] == '"') { i++; continue; }
                    _verbatim = false; break;
                }
                Add(start, CodeTokenKind.String); continue;
            }
            if (text[i] == '"' || (language == FragmentLanguage.CSharp && text[i] == '\''))
            {
                var quote = text[i++];
                var count = 1; while (quote == '"' && i < text.Length && text[i] == '"') { count++; i++; }
                if (language == FragmentLanguage.CSharp && count >= 3) { _rawQuotes = count; Add(start, CodeTokenKind.String); continue; }
                if (count > 1) { Add(start, CodeTokenKind.String); continue; }
                while (i < text.Length)
                {
                    var character = text[i++];
                    if (character == '\\' && i < text.Length) { i++; continue; }
                    if (character == quote) break;
                }
                Add(start, CodeTokenKind.String); continue;
            }
            if (char.IsDigit(text[i]))
            {
                i++;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '.' or '_')) i++;
                Add(start, CodeTokenKind.Number); continue;
            }
            if (char.IsLetter(text[i]) || text[i] is '_' or '@')
            {
                i++; while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                var word = text[start..i];
                if (language == FragmentLanguage.CSharp ? Keywords.Contains(word) : word is "true" or "false" or "null") Add(start, CodeTokenKind.Keyword);
                continue;
            }
            i++;
        }
        return tokens;
    }
}
