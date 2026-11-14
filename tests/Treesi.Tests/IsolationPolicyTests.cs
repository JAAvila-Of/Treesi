using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.TestSupport;

namespace Treesi.Tests;

[TestFixture]
public sealed class IsolationPolicyTests
{
    [Test]
    public void LinuxNativeAdmissionRequiresHostedLinuxOptInAndLocalVirtualDisplay()
    {
        var admitted = new LabEnvironment(true, "github-hosted", "Linux", false, 0, false, "linux-native-ui", ":177");
        IsolatedExecution.Refusal("linux-native-ui", admitted).Test().BeNull();
        foreach (var denied in new[] { admitted with { GitHubActions = false }, admitted with { RunnerEnvironment = "self-hosted" }, admitted with { RunnerOs = "Windows" },
            admitted with { Mode = "native-ui" }, admitted with { Display = null }, admitted with { Display = ":0" }, admitted with { Display = "remote:177" }, admitted with { MacOs = true } })
            IsolatedExecution.Refusal("linux-native-ui", denied).Test().NotBeNull();
    }

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

    [Test]
    public void NativePerformanceRequiresItsOwnHostedInteractiveOptIn()
    {
        var hosted = new LabEnvironment(true, "github-hosted", "Windows", true, 1, false, "native-performance");
        IsolatedExecution.Refusal("native-performance", hosted).Test().BeNull();
        foreach (var denied in new[] { hosted with { Mode = "native-ui" }, hosted with { GitHubActions = false }, hosted with { RunnerEnvironment = "self-hosted" }, hosted with { SessionId = 0 }, hosted with { Interactive = false }, hosted with { RunnerOs = "Linux" } })
            IsolatedExecution.Refusal("native-performance", denied).Test().NotBeNull();
        IsolatedExecution.Refusal("native-ui", hosted).Test().NotBeNull();
    }
}
