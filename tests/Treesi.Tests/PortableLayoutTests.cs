using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Packaging;

namespace Treesi.Tests;

[TestFixture]
public sealed class PortableLayoutTests
{
    private string root = null!;
    private string desktop = null!;
    private string cli = null!;
    private string legal = null!;
    private static readonly string Commit = new('a', 40);

    [SetUp]
    public void SetUp()
    {
        root = Directory.CreateTempSubdirectory("treesi-layout-tests-").FullName;
        desktop = Directory.CreateDirectory(Path.Combine(root, "desktop")).FullName;
        cli = Directory.CreateDirectory(Path.Combine(root, "cli")).FullName;
        legal = Directory.CreateDirectory(Path.Combine(root, "legal")).FullName;
        File.WriteAllText(Path.Combine(desktop, "Treesi.Desktop"), "fixture desktop");
        File.WriteAllText(Path.Combine(cli, "Treesi.Cli"), "fixture cli");
        foreach (var directory in new[] { desktop, cli }) File.WriteAllText(Path.Combine(directory, "runtime.dll"), "shared runtime");
        File.WriteAllText(Path.Combine(legal, "LICENSE-MIT"), "fixture original license");
        File.WriteAllText(Path.Combine(legal, "README.txt"), "fixture guide");
        if (!OperatingSystem.IsWindows())
            foreach (var (directory, host) in new[] { (desktop, "Treesi.Desktop"), (cli, "Treesi.Cli") })
                File.SetUnixFileMode(Path.Combine(directory, host), UnixFileMode.UserRead | UnixFileMode.UserExecute);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    [TestCase("osx-arm64")]
    [TestCase("osx-x64")]
    public void MacBundleRetainsSharedRuntimeLicenseAndIdentityAfterRelocation(string rid)
    {
        var stage = Path.Combine(root, "stage");
        PortableLayout.Stage(desktop, cli, legal, stage, rid, "0.1.0-preview.1", "1.0.0", Commit);
        var moved = Path.Combine(root, "relocated bundle with spaces.app");
        Directory.Move(Path.Combine(stage, "Treesi.app"), moved);
        var dictionary = XDocument.Load(Path.Combine(moved, "Contents/Info.plist")).Root!.Element("dict")!;
        var keys = dictionary.Elements("key").ToDictionary(key => key.Value, key => ((XElement)key.NextNode!).Value);
        keys["CFBundleExecutable"].Test().Be("Treesi.Desktop");
        keys["CFBundleIdentifier"].Test().Be("io.github.jaavila-of.treesi");
        keys["CFBundleVersion"].Test().Be("1.0.0"); keys["CFBundleShortVersionString"].Test().Be("0.1.0");
        keys["TreesiSourceCommit"].Test().Be(Commit); keys["TreesiProductVersion"].Test().Be("0.1.0-preview.1");
        ((XElement)dictionary.Elements("key").Single(key => key.Value == "NSHighResolutionCapable").NextNode!).Name.LocalName.Test().Be("true");
        Directory.GetFiles(moved, "runtime.dll", SearchOption.AllDirectories).Length.Test().Be(1);
        File.ReadAllText(Path.Combine(moved, "Contents/Resources/LICENSE-MIT")).Test().Be("fixture original license");
        using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(moved, "Contents/Resources/build-info.json")));
        identity.RootElement.GetProperty("runtimeIdentifier").GetString().Test().Be(rid);
        foreach (var host in new[] { "Treesi.Desktop", "Treesi.Cli" })
        {
            File.Exists(Path.Combine(moved, "Contents/MacOS", host)).Test().BeTrue();
            if (!OperatingSystem.IsWindows()) ((int)File.GetUnixFileMode(Path.Combine(moved, "Contents/MacOS", host)) & 0x40).Test().Be(0x40);
        }
    }

    [Test]
    public void MacArchiveInventoryIncludesTheBundleAndItsLegalMaterial()
    {
        var stage = Path.Combine(root, "Treesi");
        PortableLayout.Stage(desktop, cli, legal, stage, "osx-arm64", "0.1.0-preview.1", "1.0.0", Commit);
        PackageFiles.WriteInventory(stage, Commit, "osx-arm64", "0.1.0-preview.1");
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(stage, "package-manifest.json")));
        inventory.RootElement.GetProperty("entryPoints").GetProperty("cli").GetString().Test().Be("Treesi.app/Contents/MacOS/Treesi.Cli");
        var zip = Path.Combine(root, "bundle.zip"); PackageFiles.Archive(stage, zip, true);
        using var archive = ZipFile.OpenRead(zip);
        (archive.GetEntry("Treesi/Treesi.app/Contents/Info.plist") is not null).Test().BeTrue();
        (archive.GetEntry("Treesi/Treesi.app/Contents/Resources/LICENSE-MIT") is not null).Test().BeTrue();
        (archive.GetEntry("Treesi/Treesi.Cli") is null).Test().BeTrue();
    }

    [TestCase("0.1.0")]
    [TestCase("1.100.0")]
    [TestCase("1.0.0-preview.1")]
    public void InvalidAppleBuildNumbersCannotProduceABundle(string build)
    {
        Action stage = () => PortableLayout.Stage(desktop, cli, legal, Path.Combine(root, "stage"), "osx-x64", "0.1.0-preview.1", build, Commit);
        stage.Test().Throw<InvalidDataException>();
        Directory.Exists(Path.Combine(root, "stage")).Test().BeFalse();
    }

    [Test]
    public void MissingApplicationHostIsRefused()
    {
        File.Delete(Path.Combine(cli, "Treesi.Cli"));
        Action stage = () => PortableLayout.Stage(desktop, cli, legal, Path.Combine(root, "stage"), "osx-x64", "0.1.0-preview.1", "1.0.0", Commit);
        stage.Test().Throw<FileNotFoundException>();
    }

    [Test]
    public void LinuxKeepsFlatExecutablesAndLegalFiles()
    {
        File.WriteAllText(Path.Combine(desktop, "Treesi.Integration"), "fixture integration helper");
        var stage = Path.Combine(root, "stage");
        PortableLayout.Stage(desktop, cli, legal, stage, "linux-x64", "0.1.0-preview.1", "1.0.0", Commit);
        File.Exists(Path.Combine(stage, "Treesi.Desktop")).Test().BeTrue();
        File.Exists(Path.Combine(stage, "LICENSE-MIT")).Test().BeTrue();
        Directory.Exists(Path.Combine(stage, "Treesi.app")).Test().BeFalse();
    }
}
