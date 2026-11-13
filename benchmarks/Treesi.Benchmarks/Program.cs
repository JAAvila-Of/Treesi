using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Treesi.TestSupport;

IsolatedExecution.Require("benchmarks");
if (args is ["--latency-report", var destination])
{
    await Treesi.Benchmarks.EngineLatencyReport.WriteAsync(destination);
    return;
}
var profile = Environment.GetEnvironmentVariable("TREESI_BENCHMARK_PROFILE") ?? "short";
var job = profile switch
{
    "short" => Job.ShortRun,
    "dry" => Job.Dry,
    _ => throw new ArgumentException("Benchmark profile must be short or dry.")
};
var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(job.WithToolchain(InProcessNoEmitToolchain.Instance));
var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config).ToArray();
if (summaries.Any(summary => summary.HasCriticalValidationErrors || summary.Reports.Any(report => !report.Success || report.ResultStatistics is null)))
    throw new InvalidOperationException("A benchmark failed or produced no measurements. Inspect the retained reports.");
if (summaries.Length == 0 && !args.Contains("--list", StringComparer.Ordinal))
    throw new InvalidOperationException("No benchmark measurements were produced.");
