namespace Treesi.TestSupport;

public sealed record ThreadCpuSample(int ThreadId, long CreatedUtcTicks, double ElapsedMs, double UserCpuMs, double KernelCpuMs, string? Description = null);
public sealed record ThreadCpuDelta(int ThreadId, long CreatedUtcTicks, string? Description, double DurationMs, double UserCpuMs, double KernelCpuMs, double OneCoreCpuPercent);
public sealed record ThreadCpuWindow(IReadOnlyList<ThreadCpuSample> Before, IReadOnlyList<ThreadCpuSample> After,
    IReadOnlyList<ThreadCpuDelta> Matched, IReadOnlyList<int> StartedThreadIds, IReadOnlyList<int> EndedThreadIds)
{
    public static ThreadCpuWindow From(IReadOnlyList<ThreadCpuSample> before, IReadOnlyList<ThreadCpuSample> after)
    {
        static bool Invalid(IReadOnlyList<ThreadCpuSample> items) => items.Count > 4096 || items.Select(item => item.ThreadId).Distinct().Count() != items.Count
            || items.Any(item => item.ThreadId <= 0 || item.CreatedUtcTicks <= 0 || !double.IsFinite(item.ElapsedMs) || item.ElapsedMs < 0
                || !double.IsFinite(item.UserCpuMs) || item.UserCpuMs < 0 || !double.IsFinite(item.KernelCpuMs) || item.KernelCpuMs < 0);
        if (Invalid(before) || Invalid(after)) throw new ArgumentException("Thread snapshots must be bounded, unique, finite and nonnegative.");
        var initial = before.ToDictionary(item => (item.ThreadId, item.CreatedUtcTicks));
        var final = after.ToDictionary(item => (item.ThreadId, item.CreatedUtcTicks));
        var deltas = new List<ThreadCpuDelta>();
        foreach (var (key, end) in final)
        {
            if (!initial.TryGetValue(key, out var start)) continue;
            var duration = end.ElapsedMs - start.ElapsedMs; var user = end.UserCpuMs - start.UserCpuMs; var kernel = end.KernelCpuMs - start.KernelCpuMs;
            if (duration <= 0 || user < 0 || kernel < 0) throw new ArgumentException("Matched thread counters must be ordered and monotonic.");
            deltas.Add(new(end.ThreadId, end.CreatedUtcTicks, end.Description ?? start.Description, duration, user, kernel, (user + kernel) / duration * 100));
        }
        return new(before, after, deltas.OrderByDescending(item => item.UserCpuMs + item.KernelCpuMs).ToArray(),
            final.Keys.Except(initial.Keys).Select(item => item.ThreadId).Order().ToArray(), initial.Keys.Except(final.Keys).Select(item => item.ThreadId).Order().ToArray());
    }
}
