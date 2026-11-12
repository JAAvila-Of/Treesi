# Architecture

Treesi uses a hexagonal architecture with explicit driving and driven ports. Dependencies point toward the application and domain. Native desktop controls and JSON serialization do not belong to either inner layer.

```text
Desktop UI ──┐
             ├── IInspectionService ── InspectionService ── IRepositoryReader ── GitInspector ── Git process
JSON CLI ────┘          │                     │
                       └── InstrumentedInspectionService decorator
                                             │
                                      Treesi.Domain
```

## Layers

| Project | Responsibility | Allowed project dependencies |
| --- | --- | --- |
| Treesi.Domain | Comparison, repository change and diff value records, inspection errors | None |
| Treesi.Application | Driving and driven ports, use-case validation, instrumentation decorator | Domain |
| Treesi.Git | Git adapter, restricted process execution, porcelain and patch parsing | Application and Domain |
| Treesi.Cli | CLI driving adapter and JSON response contract | Application and Domain; Git only at composition root |
| Treesi.Desktop | Desktop driving adapter and view state | Application and Domain; Git only at composition root |

The executable entry points are composition roots. They construct the Git adapter, application service, telemetry decorator, and driving adapter using constructor injection. A DI container is unnecessary for this graph. `CommandLine` and `MainWindow` consume `IInspectionService` and do not instantiate Git.

The adapter pattern isolates Git, ports permit other implementations, immutable records describe results, and a decorator adds observability. Avoid introducing mediator frameworks, service locators, generic repositories, or factories without a concrete need. Domain records currently model the Git inspection vocabulary; a future provider must be evaluated against its actual capabilities rather than forced to implement staging.

`PatchParser` belongs to the Git adapter because unified Git patch syntax is an implementation detail. Application use cases can run with an in-memory reader in tests. JAAvila.FluentOperations architecture tests verify inner-layer references and IL dependencies using its Mono.Cecil integration.

## Security and concurrency

The only implemented driven port exposes inspection. Future write operations must use a separate capability and authorization contract; adapters must not add unrestricted shell execution. Git arguments are individual `ArgumentList` entries, paths use literal pathspecs, inherited `GIT_*` overrides are cleared, prompts are disabled, and subprocess output and duration are bounded. Cancellation kills only the process tree launched for that invocation.

Repository-local configuration is inspected before status and diff. Executable clean/smudge/process filters cause a typed refusal. Global and system configuration are excluded, fsmonitor is disabled, submodule traversal is excluded, and lazy fetching and transports are disabled. Restricted inspection is a command policy, not a sandbox: concurrent hostile configuration changes and filesystem races are outside the current guarantee. Do not open adversarial repositories without stronger process isolation.

The desktop cancels prior requests and ignores their late results. There is no file watcher yet; Refresh reloads state. Introducing filesystem events later must not treat them as authoritative or replace a newer selection with an older result.

## Observability

`InstrumentedInspectionService` publishes the `Treesi.Inspection` ActivitySource and Meter. Histograms record milliseconds and counters record completed operations, tagged only with `operation` and `outcome`. No paths, diffs, credentials, exception text, or repository identifiers are attached. No exporter or collector is enabled by default. A host may subscribe with .NET diagnostic tooling or add an OpenTelemetry exporter in its composition root after defining data handling.

## Future adapters

Desktop appearance remains inside the presentation adapter: a theme catalog, semantic resources, a controller, and an injected preference store. It has no dependency on repository writes or the domain. UI verification uses a separate Avalonia Headless test project, preserving the same application ports without native windows or OS input. See [appearance](APPEARANCE.md).

An MCP stdio adapter can call `IInspectionService` and reuse the CLI's versioned data model. It should expose a fixed tool catalog, require explicitly selected repository roots, return content as untrusted data, and propagate cancellation and limits. There is no MCP server in this prototype. Other analyses should consume inspection results or implement a separate analysis port; the domain must not reference a model vendor, hosting service, or analytics framework.
