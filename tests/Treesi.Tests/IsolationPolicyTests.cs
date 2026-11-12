using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.TestSupport;

namespace Treesi.Tests;

[TestFixture]
public sealed class IsolationPolicyTests
{
    [Test]
    public void DeveloperDesktopCannotRunNativeTestsEvenWithAnOptIn()
    {
        var desktop = new LabEnvironment(false, null, "Windows", true, 1, false, "native-ui");
        IsolatedExecution.Refusal("native-ui", desktop).Test().NotBeNull();
    }

    [Test]
    public void HostedNonInteractiveSessionCannotRunNativeTests()
    {
        var hosted = new LabEnvironment(true, "github-hosted", "Windows", false, 0, false, "native-ui");
        IsolatedExecution.Refusal("native-ui", hosted).Test().NotBeNull();
    }

    [Test]
    public void HostedInteractiveVmCanRunOptedInNativeTests()
    {
        var hosted = new LabEnvironment(true, "github-hosted", "Windows", true, 1, false, "native-ui");
        IsolatedExecution.Refusal("native-ui", hosted).Test().BeNull();
    }

    [Test]
    public void SelfHostedRunnerIsNotTreatedAsAnIsolatedDesktop()
    {
        var hosted = new LabEnvironment(true, "self-hosted", "Windows", true, 1, false, "native-ui");
        IsolatedExecution.Refusal("native-ui", hosted).Test().NotBeNull();
    }

    [Test]
    public void BenchmarksNeedExplicitOptInEvenOnTheMacLab()
    {
        var mac = new LabEnvironment(false, null, null, true, 1, true, null);
        IsolatedExecution.Refusal("benchmarks", mac).Test().NotBeNull();
        IsolatedExecution.Refusal("benchmarks", mac with { Mode = "benchmarks" }).Test().BeNull();
    }

    [Test]
    public void SyntheticFixtureDoesNotDependOnTheProductRepository()
    {
        using var fixture = new SyntheticRepository(2);
        File.ReadAllText(fixture.SamplePath).Test().Contain("changed");
        Directory.Exists(Path.Combine(fixture.Root, ".git")).Test().BeTrue();
    }
}
