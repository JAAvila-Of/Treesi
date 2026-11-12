using Treesi.Cli;
using Treesi.Application;
using Treesi.Git;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) => { args.Cancel = true; cancellation.Cancel(); };
var service = new InstrumentedInspectionService(new InspectionService(new GitInspector()));
return await CommandLine.RunAsync(args, Console.Out, service, cancellation.Token);
