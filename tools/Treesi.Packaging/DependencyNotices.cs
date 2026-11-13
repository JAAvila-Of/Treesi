using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using NuGet.Packaging;

namespace Treesi.Packaging;

public sealed record LockedPackage(string Id, string Version, string ContentHash);

public static class DependencyNotices
{
    public static IReadOnlyList<LockedPackage> ReadLocks(IEnumerable<string> paths)
    {
        var packages = new Dictionary<string, LockedPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var group in document.RootElement.GetProperty("dependencies").EnumerateObject())
                foreach (var member in group.Value.EnumerateObject())
                {
                    if (member.Value.GetProperty("type").GetString() == "Project") continue;
                    var package = new LockedPackage(member.Name, member.Value.GetProperty("resolved").GetString()!, member.Value.GetProperty("contentHash").GetString()!);
                    if (packages.TryGetValue(package.Id, out var existing) && existing != package)
                        throw new InvalidDataException("Conflicting locked package: " + package.Id);
                    packages[package.Id] = package;
                }
        }
        return packages.Values.OrderBy(package => package.Id, StringComparer.Ordinal).ToArray();
    }

    public static void VerifyGraph(IReadOnlyList<LockedPackage> baseline, IReadOnlyList<LockedPackage> candidate)
    {
        if (baseline.Count != candidate.Count || baseline.Any(package => !candidate.Contains(package)))
            throw new InvalidDataException("Runtime restore changed the reviewed dependency graph.");
    }

    public static void Collect(string cache, IReadOnlyList<LockedPackage> packages, string sources, string destination)
    {
        Directory.CreateDirectory(destination);
        using var fallback = JsonDocument.Parse(File.ReadAllText(Path.Combine(sources, "sources.json")));
        var records = new List<object>();
        foreach (var package in packages)
        {
            var id = package.Id.ToLowerInvariant(); var version = package.Version.ToLowerInvariant();
            var archivePath = PackageFiles.Within(cache, $"{id}/{version}/{id}.{version}.nupkg");
            using (var reader = new PackageArchiveReader(archivePath))
            {
                if (reader.GetContentHash(CancellationToken.None) != package.ContentHash)
                    throw new InvalidDataException("NuGet archive differs from its reviewed lock: " + package.Id);
                if (reader.IsSignedAsync(CancellationToken.None).GetAwaiter().GetResult())
                {
                    var signature = reader.GetPrimarySignatureAsync(CancellationToken.None).GetAwaiter().GetResult();
                    reader.ValidateIntegrityAsync(signature!.SignatureContent, CancellationToken.None).GetAwaiter().GetResult();
                }
            }
            using var archive = ZipFile.OpenRead(archivePath);
            var prefix = PackageFiles.Within(destination, $"{id}/{version}");
            Directory.CreateDirectory(prefix);
            var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            using var specStream = nuspec.Open();
            var spec = XDocument.Load(specStream);
            var metadata = spec.Root!.Elements().Single(element => element.Name.LocalName == "metadata");
            var license = metadata.Elements().Single(element => element.Name.LocalName == "license");
            var notices = archive.Entries.Where(entry => IsNotice(entry.FullName)).ToArray();
            foreach (var entry in notices.Append(nuspec)) CopyEntry(entry, prefix);
            string? upstream = null;
            if (notices.Length == 0)
            {
                var original = fallback.RootElement.EnumerateArray().Single(item => item.GetProperty("packages").EnumerateArray().Any(value => value.GetString() == package.Id));
                var repository = metadata.Elements().Single(element => element.Name.LocalName == "repository");
                var revision = (string?)repository.Attribute("commit");
                if (revision is null || !original.GetProperty("source").GetString()!.Contains("/" + revision + "/", StringComparison.Ordinal))
                    throw new InvalidDataException("Upstream license source does not match the package source revision: " + package.Id);
                var file = PackageFiles.Within(sources, original.GetProperty("file").GetString()!);
                if (PackageFiles.Hash(file) != original.GetProperty("sha256").GetString()) throw new InvalidDataException("Upstream license hash mismatch.");
                File.Copy(file, Path.Combine(prefix, "UPSTREAM-LICENSE.txt"));
                upstream = original.GetProperty("source").GetString();
            }
            records.Add(new { package.Id, package.Version, package.ContentHash, license = license.Value, licenseType = (string?)license.Attribute("type"), upstream });
        }
        File.WriteAllText(Path.Combine(destination, "dependencies.json"), JsonSerializer.Serialize(new
        {
            scope = "Conservative union of the resolved Desktop and CLI package graphs; includes build-only and other-platform packages. Original texts are retained per package. Runtime notices are retained separately.",
            packages = records
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool IsNotice(string path)
    {
        var name = path.Split('/').Last();
        return name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LICENCE", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("COPYING", StringComparison.OrdinalIgnoreCase) || name.Contains("NOTICE", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyEntry(ZipArchiveEntry entry, string root)
    {
        if (entry.FullName.EndsWith('/')) return;
        var path = PackageFiles.Within(root, entry.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        entry.ExtractToFile(path);
    }
}
