using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Packaging;

namespace Treesi.Tests;

[TestFixture]
public sealed class PackagingTests
{
    private string root = null!;

    [SetUp]
    public void SetUp() => root = Directory.CreateTempSubdirectory("treesi-package-tests-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(root, recursive: true);

    [Test]
    public void MergeDeduplicatesIdenticalRuntimeAndRefusesConflicts()
    {
        var first = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
        var stage = Path.Combine(root, "stage");
        File.WriteAllText(Path.Combine(first, "shared.dll"), "identical runtime");
        File.WriteAllText(Path.Combine(second, "shared.dll"), "identical runtime");
        File.WriteAllText(Path.Combine(second, "private.pdb"), "excluded symbols");
        PackageFiles.Merge(first, stage); PackageFiles.Merge(second, stage);
        Directory.GetFiles(stage).Length.Test().Be(1);
        File.WriteAllText(Path.Combine(second, "shared.dll"), "conflicting runtime");
        Action merge = () => PackageFiles.Merge(second, stage);
        merge.Test().Throw<InvalidDataException>();
        File.ReadAllText(Path.Combine(stage, "shared.dll")).Test().Be("identical runtime");
    }

    [Test]
    public void PackagePathsRefuseTraversalAbsolutePathsAndAlternateStreams()
    {
        foreach (var member in new[] { "../secret", "sub/../secret", "/secret", "C:/secret", "a\\b", "a//b", "a:stream", "." })
        { Action resolve = () => PackageFiles.Within(root, member); resolve.Test().Throw<InvalidDataException>(); }
    }

    [Test]
    public void ManifestAndArchiveDescribeExactSelectedBytes()
    {
        var stage = Directory.CreateDirectory(Path.Combine(root, "Treesi")).FullName;
        var file = Path.Combine(stage, "Treesi.Cli"); File.WriteAllText(file, "fixture apphost");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        PackageFiles.WriteInventory(stage, new string('a', 40), "linux-x64", "0.1.0-preview.1");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(stage, "package-manifest.json")));
        var files = manifest.RootElement.GetProperty("files"); files.GetArrayLength().Test().Be(1);
        files[0].GetProperty("sha256").GetString().Test().Be(PackageFiles.Hash(file));
        var output = Path.Combine(root, "package.zip"); PackageFiles.Archive(stage, output, true);
        using var zip = ZipFile.OpenRead(output);
        zip.Entries.Count.Test().Be(2);
        using var reader = new StreamReader(zip.GetEntry("Treesi/Treesi.Cli")!.Open());
        reader.ReadToEnd().Test().Be("fixture apphost");
        if (!OperatingSystem.IsWindows()) ((zip.GetEntry("Treesi/Treesi.Cli")!.ExternalAttributes >> 16) & 0x40).Test().Be(0x40);
    }

    [Test]
    public void RuntimeGraphChangesCannotPassAsReviewedDependencies()
    {
        LockedPackage package = new("Example", "1.0.0", "original");
        DependencyNotices.VerifyGraph([package], [package]);
        foreach (var candidate in new[] { Array.Empty<LockedPackage>(), new[] { package with { Version = "2.0.0" } }, new[] { package with { ContentHash = "changed" } } })
        { Action verify = () => DependencyNotices.VerifyGraph([package], candidate); verify.Test().Throw<InvalidDataException>(); }
    }

    [Test]
    public void NoticeCollectionPreservesOriginalBytesAndRejectsAlteredArchives()
    {
        var cache = Path.Combine(root, "cache"); var package = Directory.CreateDirectory(Path.Combine(cache, "example", "1.0.0")).FullName;
        var nupkg = Path.Combine(package, "example.1.0.0.nupkg");
        using (var zip = ZipFile.Open(nupkg, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("example.nuspec").Open())) writer.Write("<package><metadata><license type=\"expression\">MIT</license></metadata></package>");
            using var notice = new StreamWriter(zip.CreateEntry("licenses/THIRD-PARTY-NOTICES.txt").Open()); notice.Write("original copyright\noriginal permission\n");
        }
        var sources = Directory.CreateDirectory(Path.Combine(root, "sources")).FullName; File.WriteAllText(Path.Combine(sources, "sources.json"), "[]");
        string hash; using (var stream = File.OpenRead(nupkg)) hash = Convert.ToBase64String(SHA512.HashData(stream));
        LockedPackage locked = new("Example", "1.0.0", hash);
        var destination = Path.Combine(root, "notices"); DependencyNotices.Collect(cache, [locked], sources, destination);
        File.ReadAllText(Path.Combine(destination, "example/1.0.0/licenses/THIRD-PARTY-NOTICES.txt")).Test().Be("original copyright\noriginal permission\n");
        File.AppendAllText(nupkg, "altered");
        Action collect = () => DependencyNotices.Collect(cache, [locked], sources, Path.Combine(root, "rejected"));
        collect.Test().Throw<InvalidDataException>();
    }
}
