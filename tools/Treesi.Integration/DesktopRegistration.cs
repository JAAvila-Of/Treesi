using System.Text;
using System.Text.RegularExpressions;

namespace Treesi.Integration;

public sealed record DesktopRegistrationState(string Status, string EntryPath, string? Installation = null, string? SourceCommit = null);

public sealed class DesktopRegistrationException(string code, string message) : IOException(message)
{
    public string Code { get; } = code;
}

public sealed class DesktopRegistration
{
    public const string FileName = "io.github.jaavila-of.treesi.preview.desktop";
    private const int MaximumEntryBytes = 32768;
    private const string RootKey = "X-Treesi-RootBase64=";
    private const string CommitKey = "X-Treesi-Commit=";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string dataHome;
    public string EntryPath { get; }

    public DesktopRegistration(string dataHome)
    {
        this.dataHome = Absolute(dataHome);
        if (this.dataHome == Path.GetPathRoot(this.dataHome)) throw new ArgumentException("The data directory cannot be a filesystem root.");
        EntryPath = Path.Combine(this.dataHome, "applications", FileName);
    }

    public static string ResolveDataHome(string home, string? configured) => !string.IsNullOrEmpty(configured) && Path.IsPathFullyQualified(configured)
        ? Absolute(configured) : Path.Combine(Absolute(home), ".local", "share");

    public DesktopRegistrationState Status()
    {
        ValidateStorage();
        if (!File.Exists(EntryPath)) return new("absent", EntryPath);
        var bytes = ReadEntry();
        return Identity(bytes) is { } identity ? new("registered", EntryPath, identity.Root, identity.Commit) : new("modified", EntryPath);
    }

    public DesktopRegistrationState Register(string installation, string commit)
    {
        var text = Render(installation, commit);
        // GLib checks the executable before expanding %% in its Exec field.
        // Keep Render permissive so exact legacy entries remain removable.
        if (installation.Contains('%')) throw new DesktopRegistrationException("unsupported_location", "GLib cannot load menu entries for executable locations containing '%'. Move the portable folder to a location without '%' before registering.");
        var wanted = Utf8.GetBytes(text);
        if (wanted.Length > MaximumEntryBytes) throw new ArgumentException("The generated desktop entry exceeds its size limit.");
        ValidateStorage();
        if (File.Exists(EntryPath))
        {
            if (!ReadEntry().AsSpan().SequenceEqual(wanted)) throw Conflict();
            return Status();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
        ValidateStorage();
        // CreateNew prevents overwriting an entry created between the checks.
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(EntryPath, options))
        { stream.Write(wanted); stream.Flush(flushToDisk: true); }
        return Status();
    }

    public DesktopRegistrationState Unregister()
    {
        ValidateStorage();
        if (!File.Exists(EntryPath)) return new("absent", EntryPath);
        var original = ReadEntry();
        if (Identity(original) is null) throw Conflict();
        ValidateStorage();
        if (!ReadEntry().AsSpan().SequenceEqual(original)) throw Conflict();
        File.Delete(EntryPath);
        return new("absent", EntryPath);
    }

    public static string Render(string installation, string commit, int entryVersion = 2)
    {
        var root = Absolute(installation);
        if (entryVersion is not (1 or 2)) throw new ArgumentException("Unsupported desktop entry version.");
        if (!Regex.IsMatch(commit, "\\A[0-9a-f]{40}\\z")) throw new ArgumentException("Invalid source commit.");
        var executable = Path.Combine(root, "Treesi.Desktop");
        if (executable.Contains('=')) throw new ArgumentException("Desktop executable paths containing '=' are not supported by the desktop entry specification.");
        var argument = "\"" + executable.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal).Replace("$", "\\$", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal) + "\"";
        var command = argument.Replace("\\", "\\\\", StringComparison.Ordinal);
        var icon = entryVersion == 1 ? "application-x-executable" : Path.Combine(root, "treesi-icon.png")
            .Replace("\\", "\\\\", StringComparison.Ordinal).Replace(" ", "\\s", StringComparison.Ordinal);
        return "[Desktop Entry]\nType=Application\nVersion=1.0\nName=Treesi Preview\nComment=Inspect local Git changes\nExec=" + command
            + "\nIcon=" + icon + "\nTerminal=false\nStartupNotify=false\nCategories=Development;\nKeywords=Git;Diff;History;\nX-Treesi-EntryVersion=" + entryVersion + "\n" + RootKey
            + Convert.ToBase64String(Utf8.GetBytes(root)) + "\n" + CommitKey + commit + "\n";
    }

    public static void RefuseLink(string path, bool directory)
    {
        FileSystemInfo entry = directory ? new DirectoryInfo(path) : new FileInfo(path);
        if (entry.LinkTarget is not null) throw new DesktopRegistrationException("linked_path", "Linked integration directories and files are refused.");
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return; }
        if ((attributes & FileAttributes.ReparsePoint) != 0 || ((attributes & FileAttributes.Directory) != 0) != directory)
            throw new DesktopRegistrationException("unexpected_path", "An integration path has an unexpected type.");
    }

    private void ValidateStorage()
    {
        RefuseLink(dataHome, true);
        RefuseLink(Path.GetDirectoryName(EntryPath)!, true);
        RefuseLink(EntryPath, false);
    }

    private byte[] ReadEntry()
    {
        using var input = new FileStream(EntryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[MaximumEntryBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = input.Read(buffer.AsSpan(read));
            if (count == 0) break;
            read += count;
        }
        if (read > MaximumEntryBytes) throw new DesktopRegistrationException("entry_too_large", "The desktop entry exceeds its size limit.");
        return buffer.AsSpan(0, read).ToArray();
    }

    private static (string Root, string Commit)? Identity(byte[] bytes)
    {
        try
        {
            var lines = Utf8.GetString(bytes).Split('\n');
            var encodedRoot = lines.Single(line => line.StartsWith(RootKey, StringComparison.Ordinal))[RootKey.Length..];
            var commit = lines.Single(line => line.StartsWith(CommitKey, StringComparison.Ordinal))[CommitKey.Length..];
            var root = Utf8.GetString(Convert.FromBase64String(encodedRoot));
            var version = lines.Single(line => line.StartsWith("X-Treesi-EntryVersion=", StringComparison.Ordinal))["X-Treesi-EntryVersion=".Length..];
            return version is "1" or "2" && bytes.AsSpan().SequenceEqual(Utf8.GetBytes(Render(root, commit, int.Parse(version, System.Globalization.CultureInfo.InvariantCulture)))) ? (root, commit) : null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException) { return null; }
    }

    private static string Absolute(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Length > 4096 || path.Any(char.IsControl)) throw new ArgumentException("An absolute path without control characters is required.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static DesktopRegistrationException Conflict() => new("entry_conflict", "An existing or modified desktop entry was retained. Remove or rename it explicitly before registering another candidate.");
}
