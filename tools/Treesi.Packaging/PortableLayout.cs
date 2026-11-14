using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Treesi.Packaging;

public sealed record PackageEntryPoints(string Desktop, string Cli, string Legal, string? Integration = null);

public static class PortableLayout
{
    public static PackageEntryPoints EntryPoints(string rid) => rid switch
    {
        "win-x64" => new("Treesi.Desktop.exe", "Treesi.Cli.exe", "."),
        "linux-x64" => new("Treesi.Desktop", "Treesi.Cli", ".", "Treesi.Integration"),
        "osx-arm64" or "osx-x64" => new("Treesi.app/Contents/MacOS/Treesi.Desktop", "Treesi.app/Contents/MacOS/Treesi.Cli", "Treesi.app/Contents/Resources"),
        _ => throw new ArgumentException("Unsupported portable runtime identifier.", nameof(rid))
    };

    public static void Stage(string desktop, string cli, string legal, string root, string rid, string version, string bundleBuildVersion, string commit)
    {
        var paths = EntryPoints(rid);
        var mac = rid.StartsWith("osx-", StringComparison.Ordinal);
        var numericVersion = version.Split('-')[0];
        if (!Regex.IsMatch(numericVersion, "\\A(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\z")
            || !Regex.IsMatch(bundleBuildVersion, "\\A[1-9][0-9]{0,3}\\.(0|[1-9][0-9]?)\\.(0|[1-9][0-9]?)\\z")
            || !Regex.IsMatch(commit, "\\A[0-9a-f]{40}\\z")) throw new InvalidDataException("Invalid package version or source identity.");
        var binaries = mac ? PackageFiles.Within(root, "Treesi.app/Contents/MacOS") : root;
        var resources = mac ? PackageFiles.Within(root, paths.Legal) : root;
        PackageFiles.Merge(desktop, binaries); PackageFiles.Merge(cli, binaries); PackageFiles.Merge(legal, resources);
        foreach (var host in new[] { paths.Desktop, paths.Cli })
            if (!File.Exists(PackageFiles.Within(root, host))) throw new FileNotFoundException("Published application host is missing: " + host);
        if (paths.Integration is { } integration && !File.Exists(PackageFiles.Within(root, integration))) throw new FileNotFoundException("The Linux integration host is missing.");
        if (!mac) return;

        Directory.CreateDirectory(resources);
        var dictionary = new XElement("dict");
        foreach (var (key, value) in new[]
        {
            ("CFBundleIdentifier", "io.github.jaavila-of.treesi"), ("CFBundleName", "Treesi"), ("CFBundleDisplayName", "Treesi"),
            ("CFBundleExecutable", "Treesi.Desktop"), ("CFBundlePackageType", "APPL"), ("CFBundleInfoDictionaryVersion", "6.0"),
            ("CFBundleShortVersionString", numericVersion), ("CFBundleVersion", bundleBuildVersion), ("CFBundleDevelopmentRegion", "en"),
            ("TreesiProductVersion", version), ("TreesiSourceCommit", commit)
        }) dictionary.Add(new XElement("key", key), new XElement("string", value));
        dictionary.Add(new XElement("key", "NSHighResolutionCapable"), new XElement("true"));
        new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("plist", new XAttribute("version", "1.0"), dictionary))
            .Save(PackageFiles.Within(root, "Treesi.app/Contents/Info.plist"));
        File.WriteAllText(Path.Combine(resources, "build-info.json"), JsonSerializer.Serialize(new
        {
            sourceCommit = commit,
            productVersion = version,
            bundleBuildVersion,
            runtimeIdentifier = rid,
            signing = "No publisher signature or notarization. SDK apphosts may carry ad-hoc signatures."
        }, new JsonSerializerOptions { WriteIndented = true }));
        var guide = Path.Combine(resources, "README.txt");
        if (File.Exists(guide)) File.Copy(guide, Path.Combine(root, "README.txt"));
    }
}
