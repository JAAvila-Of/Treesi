using System.Text.Json;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Packaging;

namespace Treesi.Tests;

[TestFixture]
public sealed class WindowsInstallerTests
{
    private string root = null!;
    private string payload = null!;

    [SetUp]
    public void SetUp()
    {
        root = Directory.CreateTempSubdirectory("treesi-installer-tests-").FullName;
        payload = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
        foreach (var member in new[] { "Treesi.Desktop.exe", "Treesi.Cli.exe", "LICENSE-MIT", "LICENSE-APACHE", "docs/$release-note.txt" })
        {
            var path = PackageFiles.Within(payload, member);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "original fixture bytes");
        }
        PackageFiles.WriteInventory(payload, new string('a', 40), "win-x64", "0.1.0-preview.1");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    [Test]
    public void InstallerCarriesExactlyTheReviewedPayloadAndItsAdditionalNotices()
    {
        var plan = WindowsInstaller.ReadPayload(payload);
        plan.Files.Count.Test().Be(6);
        var license = Path.Combine(root, "COPYING"); File.WriteAllText(license, "original NSIS license");
        var template = Path.Combine(root, "template.nsi"); File.WriteAllText(template, "; fixture template");
        var output = Path.Combine(root, "installer");
        WindowsInstaller.Generate(payload, license, template, output);
        var staged = Path.Combine(output, "payload");
        foreach (var file in plan.Files) PackageFiles.Hash(PackageFiles.Within(staged, file.Path)).Test().Be(file.Sha256);
        File.ReadAllText(Path.Combine(staged, "third-party/nsis/COPYING.txt")).Test().Be("original NSIS license");
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(staged, "installation-files.json")));
        inventory.RootElement.GetProperty("files").GetArrayLength().Test().Be(7);
        Directory.GetFiles(payload, "*", SearchOption.AllDirectories).Length.Test().Be(6);
    }

    [Test]
    public void ChangedBytesAndUnlistedFilesCannotEnterAnInstaller()
    {
        var file = Path.Combine(payload, "Treesi.Cli.exe");
        File.WriteAllText(file, "modified fixture bytes");
        Action tampered = () => WindowsInstaller.ReadPayload(payload);
        tampered.Test().Throw<InvalidDataException>();
        File.WriteAllText(file, "original fixture bytes");
        File.WriteAllText(Path.Combine(payload, "unreviewed.txt"), "extra file");
        tampered.Test().Throw<InvalidDataException>();
    }

    [TestCase("../outside.txt")]
    [TestCase("AUX.txt")]
    [TestCase("trailing. ")]
    [TestCase("bad\ncommand.txt")]
    [TestCase("file:stream")]
    public void UnsafeWindowsMembersAreRefusedBeforeStaging(string member)
    {
        File.WriteAllText(Path.Combine(payload, "package-manifest.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = "1",
            runtimeIdentifier = "win-x64",
            productVersion = "0.1.0-preview.1",
            sourceCommit = new string('a', 40),
            files = new[] { new { path = member, bytes = 1, sha256 = "invalid" } }
        }));
        Action read = () => WindowsInstaller.ReadPayload(payload);
        read.Test().Throw<InvalidDataException>();
    }

    [Test]
    public void OtherPlatformCannotBePackagedAsAWindowsInstaller()
    {
        PackageFiles.WriteInventory(payload, new string('a', 40), "osx-arm64", "0.1.0-preview.1");
        Action read = () => WindowsInstaller.ReadPayload(payload);
        read.Test().Throw<InvalidDataException>();
    }
}
