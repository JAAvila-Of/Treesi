using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Treesi.Packaging;

public sealed record InstallerFile(string Path, long Bytes, string Sha256);
public sealed record InstallerPayload(string Version, string Commit, IReadOnlyList<InstallerFile> Files);

public static class WindowsInstaller
{
    public static InstallerPayload ReadPayload(string root)
    {
        var actual = Scan(root).Take(10002).Select(path => System.IO.Path.GetRelativePath(root, path).Replace(System.IO.Path.DirectorySeparatorChar, '/')).ToArray();
        var physical = actual.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (actual.Length > 10001 || physical.Count != actual.Length) throw new InvalidDataException("Excessive or case-conflicting installer members.");
        var manifestPath = System.IO.Path.Combine(root, "package-manifest.json");
        if (new FileInfo(manifestPath).Length > 4 * 1024 * 1024) throw new InvalidDataException("Package manifest exceeds its size limit.");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = document.RootElement;
        var version = manifest.GetProperty("productVersion").GetString()!;
        var commit = manifest.GetProperty("sourceCommit").GetString()!;
        if (manifest.GetProperty("runtimeIdentifier").GetString() != "win-x64" || manifest.GetProperty("schemaVersion").GetString() != "1"
            || version.Length > 64 || !Regex.IsMatch(version, "\\A[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?\\z")
            || !Regex.IsMatch(commit, "\\A[0-9a-f]{40}\\z")) throw new InvalidDataException("Invalid Windows candidate identity.");
        var files = new List<InstallerFile>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in manifest.GetProperty("files").EnumerateArray())
        {
            var relative = entry.GetProperty("path").GetString()!;
            ValidateMember(relative);
            if (!names.Add(relative) || files.Count >= 10000) throw new InvalidDataException("Duplicate or excessive installer members.");
            var path = PackageFiles.Within(root, relative);
            var size = entry.GetProperty("bytes").GetInt64();
            var hash = entry.GetProperty("sha256").GetString()!;
            total += size;
            if (size < 0 || total > 600L * 1024 * 1024 || new FileInfo(path).Length != size || PackageFiles.Hash(path) != hash)
                throw new InvalidDataException("Installer input differs from the package inventory: " + relative);
            files.Add(new(relative, size, hash));
        }
        if (files.Sum(file => file.Bytes) > 600L * 1024 * 1024 || names.Contains("package-manifest.json")) throw new InvalidDataException("Invalid installer payload size or manifest membership.");
        foreach (var required in new[] { "Treesi.Desktop.exe", "Treesi.Cli.exe", "LICENSE-MIT", "LICENSE-APACHE" })
            if (!names.Contains(required)) throw new InvalidDataException("The Windows candidate is missing " + required);
        if (!physical.SetEquals(names.Append("package-manifest.json"))) throw new InvalidDataException("The candidate contains unlisted files.");
        files.Add(new("package-manifest.json", new FileInfo(manifestPath).Length, PackageFiles.Hash(manifestPath)));
        return new(version, commit, files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray());
    }

    public static void Generate(string root, string nsisLicense, string template, string work)
    {
        work = System.IO.Path.GetFullPath(work);
        var input = ReadPayload(root);
        if (Directory.Exists(work)) throw new IOException("Installer staging requires a new directory.");
        Directory.CreateDirectory(work);
        var payload = System.IO.Path.Combine(work, "payload");
        PackageFiles.Merge(root, payload);
        var license = PackageFiles.Within(payload, "third-party/nsis/COPYING.txt");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(license)!);
        File.Copy(nsisLicense, license);
        var files = input.Files.Append(new InstallerFile("third-party/nsis/COPYING.txt", new FileInfo(license).Length, PackageFiles.Hash(license))).ToArray();
        var inventory = System.IO.Path.Combine(payload, "installation-files.json");
        File.WriteAllText(inventory, JsonSerializer.Serialize(new
        {
            product = "TreesiPreview",
            input.Commit,
            input.Version,
            files,
            scope = "Owned payload paths only. Uninstall.exe and installation.ini are generated at install time. Unknown files, user preferences and repositories are retained."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        var members = files.Select(file => file.Path).Append("installation-files.json").Order(StringComparer.Ordinal).ToArray();
        var directories = members.SelectMany(Parents).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path.Count(character => character == '/')).ThenBy(path => path, StringComparer.Ordinal).ToArray();
        var include = new StringBuilder();
        include.AppendLine("!define SOURCE_COMMIT " + Quote(input.Commit));
        include.AppendLine("!define PRODUCT_VERSION " + Quote(input.Version));
        include.AppendLine("OutFile " + Quote(System.IO.Path.Combine(work, $"Treesi-{input.Version}-win-x64-{input.Commit[..8]}-setup.exe")));
        include.AppendLine("!macro InstallPayload");
        foreach (var member in members)
        {
            var parent = System.IO.Path.GetDirectoryName(member.Replace('/', System.IO.Path.DirectorySeparatorChar))!.Replace(System.IO.Path.DirectorySeparatorChar, '\\');
            include.AppendLine("  ClearErrors");
            include.AppendLine("  SetOutPath " + Installed(parent));
            include.AppendLine("  ${If} ${Errors}\n    MessageBox MB_OK|MB_ICONSTOP \"An installation directory could not be created. Use Uninstall.exe before retrying.\" /SD IDOK\n    SetErrorLevel 3\n    Quit\n  ${EndIf}");
            include.AppendLine("  File " + Quote("/oname=" + System.IO.Path.GetFileName(member)) + " " + Quote(PackageFiles.Within(payload, member)));
            include.AppendLine("  ${If} ${Errors}\n    MessageBox MB_OK|MB_ICONSTOP \"Installation could not finish. Use Uninstall.exe in the Treesi Preview folder before retrying.\" /SD IDOK\n    SetErrorLevel 3\n    Quit\n  ${EndIf}");
        }
        include.AppendLine("!macroend\n!macro CheckOwnedDirectories");
        foreach (var directory in new[] { "" }.Concat(directories))
        {
            include.AppendLine("  System::Call 'kernel32::GetFileAttributesW(w " + Installed(directory) + ") i.r0'");
            include.AppendLine("  ${If} $0 != -1\n    IntOp $0 $0 & 0x400\n    ${If} $0 != 0\n      MessageBox MB_OK|MB_ICONSTOP \"An installation directory is a link. Nothing was removed.\" /SD IDOK\n      SetErrorLevel 2\n      Quit\n    ${EndIf}\n  ${EndIf}");
        }
        include.AppendLine("!macroend\n!macro CheckPayloadNotInUse");
        foreach (var member in members)
        {
            include.AppendLine("  ${If} ${FileExists} " + Installed(member));
            include.AppendLine("  System::Call 'kernel32::CreateFileW(w " + Installed(member) + ", i 0x80000000, i 0, p 0, i 3, i 0, p 0) p.r0'");
            include.AppendLine("  ${If} $0 == -1\n    MessageBox MB_OK|MB_ICONSTOP \"Close Treesi and other applications using its files, then retry uninstalling. Nothing was removed.\" /SD IDOK\n    SetErrorLevel 2\n    Quit\n  ${EndIf}\n  System::Call 'kernel32::CloseHandle(p r0)'\n  ${EndIf}");
        }
        include.AppendLine("!macroend\n!macro RemovePayload");
        foreach (var member in members) include.AppendLine("  Delete " + Installed(member));
        foreach (var directory in directories.Reverse()) include.AppendLine("  RMDir " + Installed(directory));
        include.AppendLine("!macroend");
        File.WriteAllText(System.IO.Path.Combine(work, "payload.nsh"), include.ToString(), new UTF8Encoding(false));
        File.Copy(template, System.IO.Path.Combine(work, "installer.nsi"));
    }

    public static string Quote(string value)
    {
        if (value.Any(char.IsControl)) throw new InvalidDataException("Control characters are not permitted in installer strings.");
        return "\"" + value.Replace("$", "$$", StringComparison.Ordinal).Replace("\"", "$\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string Installed(string relative) => "\"$INSTDIR" + (relative.Length == 0 ? "" : "\\" + relative.Replace('/', '\\').Replace("$", "$$", StringComparison.Ordinal)) + "\"";

    private static IEnumerable<string> Parents(string member)
    {
        for (var slash = member.LastIndexOf('/'); slash > 0; slash = member.LastIndexOf('/', slash - 1)) yield return member[..slash];
    }

    private static void ValidateMember(string member)
    {
        _ = PackageFiles.Within(System.IO.Path.GetTempPath(), member);
        if (member.Length > 240 || member.Any(character => char.IsControl(character) || "\"<>|?*".Contains(character))
            || member.Split('/').Any(part => part.EndsWith('.') || part.EndsWith(' ') || Regex.IsMatch(part, "\\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|\\z)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Invalid Windows installer member.");
    }

    private static IEnumerable<string> Scan(string directory, int depth = 0)
    {
        if (depth > 64) throw new InvalidDataException("Installer payload directory depth exceeds its limit.");
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked payload directories are refused.");
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked payload members are refused.");
            if (entry is DirectoryInfo child) { foreach (var file in Scan(child.FullName, depth + 1)) yield return file; }
            else yield return entry.FullName;
        }
    }
}
