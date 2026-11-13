using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Treesi.Packaging;

public static class PackageFiles
{
    public static string Within(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(part => part is ".." or "." or ""))
            throw new InvalidDataException("The package member path is not relative and normalized.");
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("The package member escapes its root.");
        return target;
    }

    public static string Hash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }

    public static void Merge(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked staging roots are not permitted.");
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Links are not permitted in package staging.");
            if (entry is DirectoryInfo directory)
            {
                Merge(directory.FullName, Within(destination, directory.Name));
                continue;
            }
            if (entry is not FileInfo file || file.Extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            var relative = Path.GetRelativePath(source, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
            var target = Within(destination, relative);
            if (File.Exists(target))
            {
                if (Hash(file.FullName) != Hash(target)) throw new InvalidDataException("Published applications contain conflicting shared files: " + relative);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file.FullName, target);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, File.GetUnixFileMode(file.FullName));
        }
    }

    public static void WriteInventory(string root, string commit, string rid, string version)
    {
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => new
        {
            path = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
            bytes = new FileInfo(path).Length,
            sha256 = Hash(path)
        }).ToArray();
        File.WriteAllText(Path.Combine(root, "package-manifest.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = "1",
            sourceCommit = commit,
            runtimeIdentifier = rid,
            productVersion = version,
            productLicense = "MIT OR Apache-2.0",
            signing = "Unsigned experimental build; not notarized or publisher-authenticated.",
            files,
            scope = "Inventory excludes this manifest. Checksums establish byte integrity, not publisher authenticity. Git is required separately."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    public static void Archive(string root, string output, bool zip)
    {
        if (File.Exists(output)) throw new IOException("The archive already exists.");
        if (!zip)
        {
            using var stream = File.Create(output); using var gzip = new GZipStream(stream, CompressionLevel.SmallestSize);
            TarFile.CreateFromDirectory(root, gzip, includeBaseDirectory: true);
            return;
        }
        using var archive = ZipFile.Open(output, ZipArchiveMode.Create);
        foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetFileName(root) + "/" + Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            var entry = archive.CreateEntryFromFile(path, relative, CompressionLevel.SmallestSize);
            if (!OperatingSystem.IsWindows()) entry.ExternalAttributes = ((int)File.GetUnixFileMode(path) | 0x8000) << 16;
        }
    }
}
