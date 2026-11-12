using Treesi.Domain;

namespace Treesi.Git;

public static class StatusParser
{
    public static IReadOnlyList<FileChange> Parse(string output)
    {
        var records = output.Split('\0');
        var files = new List<FileChange>();
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.Length == 0 || record[0] == '#') continue;
            if (record.StartsWith("? ", StringComparison.Ordinal))
            {
                files.Add(new(record[2..], null, "?", "?"));
                continue;
            }
            if (record[0] == '!') continue;
            var fieldCount = record[0] switch { '1' => 9, '2' => 10, 'u' => 11, _ => 0 };
            if (fieldCount == 0) throw InvalidStatus();
            var fields = record.Split(' ', fieldCount, StringSplitOptions.None);
            if (fields.Length != fieldCount || fields[1].Length != 2) throw InvalidStatus();
            string? originalPath = null;
            if (record[0] == '2')
            {
                if (++i >= records.Length || records[i].Length == 0) throw InvalidStatus();
                originalPath = records[i];
            }
            files.Add(new(fields[^1], originalPath, fields[1][0].ToString(), fields[1][1].ToString()));
        }
        return files;
    }

    private static InspectionException InvalidStatus() => new("invalid_git_output", "Git returned an unsupported status record.");
}
