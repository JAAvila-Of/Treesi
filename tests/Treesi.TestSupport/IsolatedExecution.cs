namespace Treesi.TestSupport;

public sealed record LabEnvironment(bool GitHubActions, string? RunnerEnvironment, string? RunnerOs,
    bool Interactive, int SessionId, bool MacOs, string? Mode);

public static class IsolatedExecution
{
    public static string? Refusal(string suite, LabEnvironment environment) => suite switch
    {
        "native-ui" when environment.Mode != "native-ui" => "Native UI execution was not explicitly enabled.",
        "native-ui" when !environment.GitHubActions || environment.RunnerEnvironment != "github-hosted" =>
            "Native UI tests require an isolated GitHub-hosted runner, never a developer desktop or self-hosted runner.",
        "native-ui" when environment.RunnerOs != "Windows" || !environment.Interactive || environment.SessionId == 0 =>
            "Native Windows tests require an interactive desktop in the isolated runner VM.",
        "native-ui" => null,
        "benchmarks" when environment.Mode != "benchmarks" => "Benchmark execution was not explicitly enabled.",
        "benchmarks" when environment.GitHubActions && environment.RunnerEnvironment == "github-hosted" => null,
        "benchmarks" when environment.MacOs && !environment.GitHubActions => null,
        "benchmarks" => "Benchmarks require a hosted runner or a configured macOS performance host.",
        _ => "Unknown isolated test suite."
    };

    public static void Require(string suite)
    {
        var environment = new LabEnvironment(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true",
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT"),
            Environment.GetEnvironmentVariable("RUNNER_OS"),
            Environment.UserInteractive,
            System.Diagnostics.Process.GetCurrentProcess().SessionId,
            OperatingSystem.IsMacOS(),
            Environment.GetEnvironmentVariable("TREESI_ISOLATED_SUITE"));
        if (Refusal(suite, environment) is { } reason) throw new InvalidOperationException(reason);
    }
}
