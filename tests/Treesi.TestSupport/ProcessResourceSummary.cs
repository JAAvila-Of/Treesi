namespace Treesi.TestSupport;

public sealed record ProcessResourceSample(double ElapsedMs, double CpuMs, long WorkingSetBytes, long PrivateCommitBytes, long PeakWorkingSetBytes);
public sealed record ProcessResourceSummary(double DurationMs, double CpuMs, double OneCoreCpuPercent, double MachineCpuPercent,
    long MinimumWorkingSetBytes, long MaximumWorkingSetBytes, long MaximumPrivateCommitBytes, long LifetimePeakWorkingSetBytes)
{
    public static ProcessResourceSummary From(IReadOnlyList<ProcessResourceSample> samples, int logicalProcessors)
    {
        if (logicalProcessors < 1 || samples.Count is < 2 or > 10000
            || samples.Any(item => !double.IsFinite(item.ElapsedMs) || !double.IsFinite(item.CpuMs) || item.ElapsedMs < 0 || item.CpuMs < 0
                || item.WorkingSetBytes < 0 || item.PrivateCommitBytes < 0 || item.PeakWorkingSetBytes < 0)
            || samples.Zip(samples.Skip(1)).Any(pair => pair.Second.ElapsedMs <= pair.First.ElapsedMs || pair.Second.CpuMs < pair.First.CpuMs))
            throw new ArgumentException("Resource samples must be ordered, finite and nonnegative, with at least two distinct timestamps.", nameof(samples));
        var duration = samples[^1].ElapsedMs - samples[0].ElapsedMs;
        var cpu = samples[^1].CpuMs - samples[0].CpuMs;
        return new(duration, cpu, cpu / duration * 100, cpu / duration * 100 / logicalProcessors,
            samples.Min(item => item.WorkingSetBytes), samples.Max(item => item.WorkingSetBytes), samples.Max(item => item.PrivateCommitBytes), samples.Max(item => item.PeakWorkingSetBytes));
    }
}
