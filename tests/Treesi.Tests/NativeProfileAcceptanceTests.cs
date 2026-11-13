using JAAvila.FluentOperations;
using NUnit.Framework;
using Treesi.TestSupport;

namespace Treesi.Tests;

[TestFixture]
public sealed class NativeProfileAcceptanceTests
{
    [Test]
    public void FilterStacksAndActiveNegativeControlAreAllRequired()
    {
        NativeProfileAcceptance.Status(5000, 1000, 1000, 100, 0, 90, 0).Test().Be("supported");
        NativeProfileAcceptance.Status(5000, 1000, 1000, 100, 1, 90, 0).Test().Be("pid-filter-not-verified");
        NativeProfileAcceptance.Status(5000, 1000, 1000, 0, 0, 0, 0).Test().Be("no-target-samples");
        NativeProfileAcceptance.Status(5000, 1000, 1000, 100, 0, 0, 0).Test().Be("no-native-stacks");
        NativeProfileAcceptance.Status(5000, 1000, 1000, 100, 0, 90, 1).Test().Be("events-lost");
    }

    [Test]
    public void MissingControlsShortRunsAndInvalidEvidenceCannotPass()
    {
        foreach (var value in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
        {
            NativeProfileAcceptance.Status(5000, 1000, value, 100, 0, 90, 0).Test().Be("invalid-control");
            NativeProfileAcceptance.Status(5000, value, 1000, 100, 0, 90, 0).Test().Be("invalid-control");
            NativeProfileAcceptance.Status(value, 1000, 1000, 100, 0, 90, 0).Test().Be("invalid-control");
        }
        NativeProfileAcceptance.Status(4000, 1000, 1000, 100, 0, 90, 0).Test().Be("invalid-control");
        NativeProfileAcceptance.Status(5000, 1000, 1000, 100, 0, 101, 0).Test().Be("invalid-control");
        NativeProfileAcceptance.Status(5000, 1000, 1000, -1, 0, 0, 0).Test().Be("invalid-control");
        NativeProfileAcceptance.Status(5000, 1000, 1000, 100, -1, 90, 0).Test().Be("invalid-control");
        NativeProfileAcceptance.Status(5000, 1000, 1000, 100, 0, -1, 0).Test().Be("invalid-control");
        NativeProfileAcceptance.Status(5000, 1000, 1000, 100, 0, 90, -1).Test().Be("invalid-control");
    }
}
