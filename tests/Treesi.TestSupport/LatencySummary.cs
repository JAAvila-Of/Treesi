namespace Treesi.TestSupport;

public sealed record LatencySummary(int Samples, double MinimumMs, double MeanMs, double MedianMs, double P95Ms, double MaximumMs)
{
    public static LatencySummary From(IEnumerable<double> samples)
    {
        var sorted = samples.ToArray();
        if (sorted.Length is < 1 or > 10000 || sorted.Any(value => !double.IsFinite(value) || value < 0)) throw new ArgumentException("Latency samples must contain 1 to 10,000 finite nonnegative values.", nameof(samples));
        Array.Sort(sorted);
        var middle = sorted.Length / 2;
        return new(sorted.Length, sorted[0], sorted.Average(), sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle], sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], sorted[^1]);
    }
}
