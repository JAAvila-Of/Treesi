using System.Diagnostics;
using System.Diagnostics.Metrics;
using Treesi.Domain;

namespace Treesi.Application;

public static class InspectionTelemetry
{
    public const string Name = "Treesi.Inspection";
    public static readonly ActivitySource Activities = new(Name, "0.1.0");
    public static readonly Meter Meter = new(Name, "0.1.0");
    public static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("treesi.inspection.duration", "ms");
    public static readonly Counter<long> Operations = Meter.CreateCounter<long>("treesi.inspection.operations", "{operation}");
}

// Decorates use cases without coupling observability to a provider or transport.
public sealed class InstrumentedInspectionService(IInspectionService inner) : IInspectionService
{
    public Task<RepositorySnapshot> InspectAsync(string path, CancellationToken cancellationToken = default) =>
        MeasureAsync("inspect", () => inner.InspectAsync(path, cancellationToken));

    public Task<FileDiff> DiffAsync(string repository, string path, Comparison comparison, CancellationToken cancellationToken = default, DiffOptions? options = null) =>
        MeasureAsync("diff", () => inner.DiffAsync(repository, path, comparison, cancellationToken, options));

    internal static async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        using var activity = InspectionTelemetry.Activities.StartActivity(operation);
        var start = Stopwatch.GetTimestamp();
        var outcome = "success";
        try { return await action(); }
        catch (OperationCanceledException) { outcome = "cancelled"; activity?.SetStatus(ActivityStatusCode.Error, outcome); throw; }
        catch (Exception) { outcome = "error"; activity?.SetStatus(ActivityStatusCode.Error, outcome); throw; }
        finally
        {
            var tags = new TagList { { "operation", operation }, { "outcome", outcome } };
            InspectionTelemetry.Duration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, tags);
            InspectionTelemetry.Operations.Add(1, tags);
        }
    }
}
