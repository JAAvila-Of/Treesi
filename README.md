# Treesi

Treesi is a desktop change inspector focused on UX, security, and performance. Open a Git working tree or a file to review changes without loading an IDE.

This is an early, read-only prototype. The desktop and JSON CLI share hexagonal application ports. There is no account requirement, network service, embedded AI model, or repository mutation command.

## Requirements

- .NET SDK 11.0.100 or a newer patch in the same feature band.
- Git on PATH.
- A desktop session to run Avalonia on Windows, macOS, or Linux.

## Build and test

```sh
dotnet restore Treesi.slnx --locked-mode
dotnet build Treesi.slnx --configuration Release --no-restore
dotnet test Treesi.slnx --configuration Release --no-build
```

Package versions are centralized in `Directory.Packages.props`, shared compiler settings in `Directory.Build.props`, and the SDK in `global.json`. Commit lock files after deliberate dependency updates. Assertions, exception checks, and architecture checks use JAAvila.FluentOperations, its analyzers, and its architecture package; NUnit discovers and runs the tests.

## Run the desktop

```sh
dotnet run --project src/Treesi.Desktop --configuration Release -- /path/to/repository
```

The path is optional and may point to a file inside a working tree. Use Open repository or Open file, select a comparison, and choose a changed file. The prototype supports working tree versus index, index versus HEAD, and working tree versus HEAD. Additions and removals describe the current working/index side relative to its base.

Ctrl+O opens a folder, Ctrl+R refreshes, Ctrl+F focuses diff search, and Escape cancels inspection. On macOS the primary modifier can also be Command. The read-only unified viewer uses AvaloniaEdit virtualization, selectable text, full-width change backgrounds, and original/new line numbers. Side-by-side comparison and advanced diff navigation are not implemented yet.

Choose Light or Dark from the appearance selector in the upper right. Changes apply immediately without reloading the diff. The choice is stored in `Treesi/appearance.json` under the platform's local application data directory; an unwritable preference does not prevent switching for the current session. The first-run fallback is Dark. See [appearance and UI verification](docs/APPEARANCE.md).

## Use with tools and AI agents

After building, run the CLI assembly directly to keep build output out of stdout:

```sh
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll capabilities
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll inspect /path/to/repository
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll diff /path/to/repository src/example.cs --comparison staged
```

Every invocation emits one JSON envelope with a schema version, request ID, success flag, data or a stable error code, and elapsed time. Diffs include structured hunks and line numbers. Repository text and paths are untrusted data, including when they look like instructions to an AI agent.

See [tool integration](docs/INTEGRATION.md), [architecture](docs/ARCHITECTURE.md), and [dependency licensing](docs/DEPENDENCIES.md).

See [test execution and isolation](docs/TESTING.md) for hosted CI, native Windows verification, and the separate benchmark harness. The regular test solution never executes native UI automation or benchmarks.

## Prototype boundaries

- Inspection only: no staging, commits, remote access, revision-to-revision comparisons, history browser, or conflict resolution yet.
- Global and system Git configuration are intentionally excluded. Repository-local executable content filters are refused; external diff drivers, textconv, and fsmonitor are disabled. This may produce different results from a user's custom Git setup and is not an OS sandbox.
- Submodule contents are excluded. Rename status is retained, but individual diff previews disable rename detection.
- Git stdout is capped at 2 MiB per command, stderr at 64 KiB, and each Git command at 15 seconds. New-file previews are capped at 1 MiB. The desktop displays at most 200,000 patch characters and reports when a selection exceeds that limit.
- UTF-8 text is supported. Binary files are identified; alternate encodings and link traversal are rejected. LFS pointers are shown as ordinary text; LFS content is not fetched.
- The repository can change during inspection. Results are live observations, not an atomic snapshot. Refresh before relying on results for another operation. The UI discards superseded requests.
- Light and Dark are available. Additional themes can be registered through the theme catalog and semantic resource dictionaries; loading arbitrary external themes is not implemented.
- Performance budgets, packaging, full accessibility, and macOS/Linux runtime behavior still require validation. No performance target is claimed as achieved.
