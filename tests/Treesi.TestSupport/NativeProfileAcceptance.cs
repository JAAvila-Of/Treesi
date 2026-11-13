namespace Treesi.TestSupport;

public static class NativeProfileAcceptance
{
    public static string Status(double durationMs, double targetCpuMs, double controlCpuMs, int targetSamples, int foreignSamples, int userStacks, long eventsLost)
    {
        if (!double.IsFinite(durationMs) || !double.IsFinite(targetCpuMs) || !double.IsFinite(controlCpuMs)
            || durationMs < 4500 || targetCpuMs < 500 || controlCpuMs < 500 || targetSamples < 0 || foreignSamples < 0 || userStacks < 0 || eventsLost < 0 || userStacks > targetSamples)
            return "invalid-control";
        if (foreignSamples > 0) return "pid-filter-not-verified";
        if (targetSamples == 0) return "no-target-samples";
        if (userStacks == 0) return "no-native-stacks";
        return eventsLost > 0 ? "events-lost" : "supported";
    }
}
