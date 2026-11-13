# Treesi

Treesi is a desktop change inspector focused on UX, security, and performance. Open a Git working tree or a file to review changes without loading an IDE.

This is an early, read-only prototype. The desktop and JSON CLI share hexagonal application ports. There is no account requirement, network service, embedded AI model, or repository mutation command.

The desktop supports up to eight [repository tabs](docs/REPOSITORY_SESSIONS.md) with independent in-memory review state and persistent [local workspace groups](docs/LOCAL_WORKSPACES.md). Returning to a tab uses its saved view; Refresh explicitly reads current repository state. Groups organize shortcuts without automatically opening repositories. The [local commit graph](docs/COMMIT_GRAPH.md) provides virtualized topology, reference scopes and direct commit review in the full diff canvas.

Use Related worktrees in the workspace chooser to discover the active repository's registered working folders. Open one explicitly in an independent tab, or resume its existing tab. Discovery reads metadata without opening the other locations; see [related worktrees and CLI](docs/RELATED_WORKTREES.md).

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

You can also drop a single local file or folder onto the workspace to open its review. Hover does not inspect repositories, and the original item stays in place. Multiple items and non-file links are refused; close an open draft panel first. Failed openings preserve the current comparison. See [file-drop behavior and verification](docs/FILE_DROP.md).

Compare opens a draft for two local revisions. Enter commit IDs, reference names or bounded `~n`/`^n` ancestry; Browse refs reads up to 200 local entries only when requested. Direct compares the two tips; Merge base compares their single available common ancestor to the target. Compare pins complete commit IDs, lists changed files and reuses the same read-only diff viewer. The endpoint button shows direction and mode, with full IDs in its tooltip. Refresh resolves the names again; changing files, options, themes or layouts does not. Working changes returns to a fresh working-tree inspection. Historical comparisons are session-only; recents retain the working view.

Options beside the diff search opens a draft panel for 0–50 context lines and whitespace handling. Defaults are 3 lines and Exact. Apply reloads the selected diff only when the options change; Cancel and Escape discard the draft. Ignoring trailing space, space changes or all space leaves a visible WS ignored indicator, including when no differences remain. These filters can hide meaningful changes inside strings; return to Exact to verify all changes. Options are session-only and reset when switching repository roots.

Without a path argument, the app starts at local recent repositories. Select a shortcut and choose Resume selected, or press Enter in the list. Startup reads only the small settings file; it does not inspect recent repositories automatically. A path argument opens that location instead. Failed or cancelled openings preserve an existing comparison and offer recovery.

Ctrl+O opens a folder, Ctrl+R refreshes, and Ctrl+F focuses diff search. Ctrl+B folds the file panel; Ctrl+Shift+F toggles focus mode. Escape exits focus mode first, or cancels inspection otherwise. On macOS the primary modifier can also be Command. Unified and side-by-side viewers use AvaloniaEdit virtualization, selectable text, change backgrounds, and original/new line numbers. Alt+Up/Alt+Down navigate change blocks. Layout changes preserve the loaded comparison without querying Git again.

Compact headers and a single comparison toolbar give the diff most of the working area, including at 900 × 560. Focus mode folds the file panel and top application header while keeping comparison controls and search available. Leaving focus restores the previous panel visibility. Each recent shortcut remembers the selected file, comparison, layout and file-panel visibility. Focus mode, filters and search text are not persisted.

Up to eight recent roots are stored locally in `Treesi/workspaces.json` under local application data. View state is saved after a successful repository opening, before switching locations or visiting recents, and when closing the window. Removing a shortcut or clearing the list never deletes repository files. Only paths and view metadata are stored, with no patches or credentials. Save failures keep the current session usable.

Choose Light or Dark from the appearance selector in the upper right. Changes apply immediately without reloading the diff. The choice is stored in `Treesi/appearance.json` under the platform's local application data directory; an unwritable preference does not prevent switching for the current session. The first-run fallback is Dark. See [appearance and UI verification](docs/APPEARANCE.md).

On Windows, **Settings** offers **Automatic (recommended)** and **Software compatibility** graphics modes. Software can help virtual or problematic graphics devices with high CPU use or display issues. Save the preference and reopen Treesi when convenient to apply it; saving never restarts the app. Switch back to Automatic through the same dialog. See [Windows graphics compatibility](docs/WINDOWS_RENDERING.md).

History at the bottom of the sidebar browses local repository or selected-file history. Go starts from a local revision (HEAD by default). Previous and Next reuse the first page's pinned commit; selecting an entry loads its changes directly in the main diff. Files restores file navigation for that commit. Click the endpoint button for the message, identities and parent selector. Merges default to their first parent; initial commits show an empty-tree base. Working changes returns to the current working view. History does not follow renames or fetch missing objects. See [history and CLI commands](docs/HISTORY.md).

Code colors and Emphasize changed text in Options control local presentation. Both are enabled initially and apply without reloading Git. C#/C# script and JSON fragments receive lexical colors; other files retain plain diff colors. Paired removed/added lines receive bounded, Unicode-aware emphasis within each line in unified and split views. Signs, line numbers and added/removed row backgrounds remain visible. These are review hints rather than semantic parsing or a full-file reconstruction. See [source presentation and limits](docs/SOURCE_PRESENTATION.md).

Full file context in Options expands surrounding lines of changed files while preserving the review position. Turn it off to return to the retained 0–50 context-line setting. Historical comparisons reuse their pinned commits. Expansion is bounded by the existing limits; failed, cancelled or oversized option reloads retain the previous comparison and restore its diff options. This does not load unchanged files or bypass whitespace filtering. See [full-context review](docs/FULL_CONTEXT.md).

F6 and Shift+F6 move between visible header, navigation, search, code panes and footer regions. Tab leaves the read-only code; in Options, revision and commit panels it cycles inside the active panel. Escape closes the panel and returns focus. F3/Shift+F3 and Enter/Shift+Enter in search navigate matches in both directions. Comparison text has a read-only accessibility value with names describing its file, side, context and whitespace policy. This is an accessibility foundation, with manual screen-reader validation still pending; see [accessibility coverage](docs/ACCESSIBILITY.md).

## Use with tools and AI agents

After building, run the CLI assembly directly to keep build output out of stdout:

```sh
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll capabilities
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll inspect /path/to/repository
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll diff /path/to/repository src/example.cs --comparison staged
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll diff /path/to/repository src/example.cs --context 10 --whitespace ignore-trailing-space
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll references /path/to/repository
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll compare /path/to/repository main feature --mode merge-base
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll revision-diff /path/to/repository HEAD~1 HEAD src/example.cs --context 10
```

Every invocation emits one JSON envelope with a schema version, request ID, success flag, data or a stable error code, and elapsed time. Diffs include structured hunks and line numbers. Repository text and paths are untrusted data, including when they look like instructions to an AI agent.

See [tool integration](docs/INTEGRATION.md), [architecture](docs/ARCHITECTURE.md), and [dependency licensing](docs/DEPENDENCIES.md).

See [test execution and isolation](docs/TESTING.md) for hosted CI, native Windows verification, and the separate benchmark harness. The regular test solution never executes native UI automation or benchmarks.

## Prototype boundaries

- Inspection only: no staging, commit creation, remote access, or conflict resolution yet. Local history and revision comparison are available without fetching objects.
- Revision comparison reads committed trees without checking out a revision or reading the current file. File lists are bounded to 10,000 entries; the CLI can inspect a specific historical file without enumerating the full list. Missing objects or absent/ambiguous merge bases are explicit errors. Rename detection remains disabled, so renames appear as deletion/addition. Historical submodule traversal and file-to-directory transitions are unsupported.
- Global and system Git configuration are intentionally excluded. Repository-local executable content filters are refused; external diff drivers, textconv, and fsmonitor are disabled. This may produce different results from a user's custom Git setup and is not an OS sandbox.
- Submodule contents are excluded. Rename status is retained, but individual diff previews disable rename detection.
- Git stdout is capped at 2 MiB per command, stderr at 64 KiB, and each Git command at 15 seconds. New-file previews are capped at 1 MiB. The desktop displays at most 200,000 patch characters and reports when a selection exceeds that limit.
- UTF-8 text is supported. Binary files are identified; alternate encodings and link traversal are rejected. LFS pointers are shown as ordinary text; LFS content is not fetched.
- The repository can change during inspection. Results are live observations, not an atomic snapshot. Refresh before relying on results for another operation. The UI discards superseded requests.
- Light and Dark are available. Additional themes can be registered through the theme catalog and semantic resource dictionaries; loading arbitrary external themes is not implemented.
- Native startup, idle CPU, total desktop memory, frame responsiveness, packaging and full accessibility still require validation. The [engine performance protocol](docs/PERFORMANCE.md) defines synthetic workloads and provisional warm-operation targets separately from desktop acceptance.
