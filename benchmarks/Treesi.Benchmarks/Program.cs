using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Treesi.TestSupport;

IsolatedExecution.Require("benchmarks");
var profile = Environment.GetEnvironmentVariable("TREESI_BENCHMARK_PROFILE") ?? "short";
var job = profile switch
{
    "short" => Job.ShortRun,
    "dry" => Job.Dry,
    _ => throw new ArgumentException("Benchmark profile must be short or dry.")
};
var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(job.WithToolchain(InProcessNoEmitToolchain.Instance));
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
