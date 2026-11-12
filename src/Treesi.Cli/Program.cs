using Treesi.Cli;
using Treesi.Application;
using Treesi.Git;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) => { args.Cancel = true; cancellation.Cancel(); };
var git = new GitInspector();
var service = new InstrumentedInspectionService(new InspectionService(git));
var revisions = new InstrumentedRevisionService(new RevisionService(git));
var history = new InstrumentedHistoryService(new HistoryService(git));
return await CommandLine.RunAsync(args, Console.Out, service, cancellation.Token, revisions, history);
