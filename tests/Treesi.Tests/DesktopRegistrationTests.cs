using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.Integration;

namespace Treesi.Tests;

[TestFixture]
public sealed class DesktopRegistrationTests
{
    private string root = null!;
    private string data = null!;
    private string installation = null!;
    private static readonly string Commit = new('a', 40);

    [SetUp]
    public void SetUp()
    {
        root = Directory.CreateTempSubdirectory("treesi-desktop-entry-").FullName;
        data = Path.Combine(root, "data"); installation = Path.Combine(root, "application with spaces");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    [Test]
    public void StatusDoesNotCreateFilesAndRepeatedRegistrationIsIdempotent()
    {
        var registry = new DesktopRegistration(data);
        registry.Status().Status.Test().Be("absent"); Directory.Exists(data).Test().BeFalse();
        registry.Register(installation, Commit).Status.Test().Be("registered");
        var before = File.ReadAllBytes(registry.EntryPath); var time = File.GetLastWriteTimeUtc(registry.EntryPath);
        registry.Register(installation, Commit).Status.Test().Be("registered");
        File.ReadAllBytes(registry.EntryPath).SequenceEqual(before).Test().BeTrue();
        File.GetLastWriteTimeUtc(registry.EntryPath).Test().Be(time);
    }

    [TestCase(1)]
    [TestCase(2)]
    public void UnregisterUsesEmbeddedIdentityAfterApplicationFolderWasMoved(int entryVersion)
    {
        var registry = new DesktopRegistration(data);
        Directory.CreateDirectory(installation);
        Directory.CreateDirectory(Path.GetDirectoryName(registry.EntryPath)!);
        File.WriteAllText(registry.EntryPath, DesktopRegistration.Render(installation, Commit, entryVersion));
        Directory.Move(installation, Path.Combine(root, "relocated application"));
        registry.Status().Installation.Test().Be(installation);
        registry.Unregister().Status.Test().Be("absent");
        registry.Unregister().Status.Test().Be("absent");
        Directory.Exists(Path.Combine(root, "relocated application")).Test().BeTrue();
    }

    [Test]
    public void LegacyDescriptorIsRecognizedButNotSilentlyReplaced()
    {
        var registry = new DesktopRegistration(data);
        Directory.CreateDirectory(Path.GetDirectoryName(registry.EntryPath)!);
        var legacy = DesktopRegistration.Render(installation, Commit, 1);
        File.WriteAllText(registry.EntryPath, legacy);
        registry.Status().Status.Test().Be("registered");
        Action register = () => registry.Register(installation, Commit);
        register.Test().Throw<DesktopRegistrationException>();
        File.ReadAllText(registry.EntryPath).Test().Be(legacy);
        registry.Unregister().Status.Test().Be("absent");
        registry.Register(installation, Commit).Status.Test().Be("registered");
        File.ReadAllText(registry.EntryPath).Test().Contain("X-Treesi-EntryVersion=2\n");
    }

    [TestCase(1)]
    [TestCase(2)]
    public void ChangedIconOrUnknownVersionCannotAuthorizeRemoval(int entryVersion)
    {
        var registry = new DesktopRegistration(data);
        Directory.CreateDirectory(Path.GetDirectoryName(registry.EntryPath)!);
        var original = DesktopRegistration.Render(installation, Commit, entryVersion);
        var icon = original.Split('\n').Single(line => line.StartsWith("Icon=", StringComparison.Ordinal));
        foreach (var modified in new[] { original.Replace(icon, "Icon=/other/application.png", StringComparison.Ordinal), original.Replace("X-Treesi-EntryVersion=" + entryVersion, "X-Treesi-EntryVersion=3", StringComparison.Ordinal) })
        {
            File.WriteAllText(registry.EntryPath, modified);
            registry.Status().Status.Test().Be("modified");
            Action remove = () => registry.Unregister();
            remove.Test().Throw<DesktopRegistrationException>();
            File.ReadAllText(registry.EntryPath).Test().Be(modified);
        }
    }

    [Test]
    public void ForeignOrModifiedEntriesCannotBeOverwrittenOrRemoved()
    {
        var registry = new DesktopRegistration(data);
        Directory.CreateDirectory(Path.GetDirectoryName(registry.EntryPath)!);
        foreach (var text in new[] { "[Desktop Entry]\nName=Other application\n", DesktopRegistration.Render(installation, Commit) + "X-User-Note=keep\n" })
        {
            File.WriteAllText(registry.EntryPath, text);
            registry.Status().Status.Test().Be("modified");
            Action register = () => registry.Register(installation, Commit);
            Action remove = () => registry.Unregister();
            register.Test().Throw<DesktopRegistrationException>(); remove.Test().Throw<DesktopRegistrationException>();
            File.ReadAllText(registry.EntryPath).Test().Be(text);
        }
    }

    [Test]
    public void DifferentCandidateMustBeUnregisteredBeforeReplacement()
    {
        var registry = new DesktopRegistration(data);
        registry.Register(installation, Commit);
        Action replacement = () => registry.Register(Path.Combine(root, "other candidate"), new string('b', 40));
        replacement.Test().Throw<DesktopRegistrationException>();
        registry.Status().SourceCommit.Test().Be(Commit);
    }

    [Test]
    public void LiteralPercentSequencesAndUnicodeDoNotChangeEmbeddedIdentity()
    {
        var registry = new DesktopRegistration(data);
        var special = Path.Combine(root, "revisión %2F and # marker");
        registry.Register(special, Commit).Installation.Test().Be(special);
        registry.Unregister().Status.Test().Be("absent");
    }

    [Test]
    public void UnregisterPreservesOtherMenuEntriesAndPreferences()
    {
        var registry = new DesktopRegistration(data); registry.Register(installation, Commit);
        var other = Path.Combine(data, "applications", "other.desktop"); var preferences = Path.Combine(root, "preferences.json");
        File.WriteAllText(other, "other entry"); File.WriteAllText(preferences, "saved state");
        registry.Unregister();
        File.ReadAllText(other).Test().Be("other entry"); File.ReadAllText(preferences).Test().Be("saved state");
    }

    [Test]
    public void RelativeXdgDataHomeIsIgnoredAndEmptyUsesHomeDefault()
    {
        foreach (var configured in new string?[] { null, "", "relative/data" })
            DesktopRegistration.ResolveDataHome(root, configured).Test().Be(Path.Combine(root, ".local", "share"));
        DesktopRegistration.ResolveDataHome(root, data).Test().Be(data);
    }

    [Test]
    public void UnsupportedExecutablePathsDoNotCreateAnEntry()
    {
        var registry = new DesktopRegistration(data);
        foreach (var invalid in new[] { Path.Combine(root, "bad=path"), Path.Combine(root, "line\nbreak"), "relative" })
        { Action register = () => registry.Register(invalid, Commit); register.Test().Throw<ArgumentException>(); }
        Directory.Exists(data).Test().BeFalse();
    }

    [Test]
    public void ExcessiveEntryAndDirectoryAtEntryPathAreRefused()
    {
        var registry = new DesktopRegistration(data);
        Directory.CreateDirectory(Path.GetDirectoryName(registry.EntryPath)!);
        File.WriteAllText(registry.EntryPath, new string('x', 32769));
        Action remove = () => registry.Unregister(); remove.Test().Throw<DesktopRegistrationException>();
        File.Delete(registry.EntryPath); Directory.CreateDirectory(registry.EntryPath);
        remove.Test().Throw<DesktopRegistrationException>();
        Directory.Exists(registry.EntryPath).Test().BeTrue();
    }
}
