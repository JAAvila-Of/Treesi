# Local commit graph

Graph opens a dedicated central canvas alongside Changes. Local branches, locally stored remote-tracking references, tags and HEAD identify the starting tips. This view never contacts a remote, checks out a revision or modifies the index. Remote-tracking labels describe local information and may be stale.

Rows show references, parent connections, subject, author and date in child-before-parent order. Merge commits have larger nodes and multiple parent connections. Colors help follow lanes; connections and node geometry also carry topology. Rows are virtualized. The default window contains 200 commits; Load more extends it by 200, up to 1,000. Dashed connections at the last row indicate parents outside the loaded window, including shallow boundaries. They do not imply an initial commit.

## Navigation

- Select a reference in the left sidebar, or enter a local reference or full commit ID and choose Go. Blank selects all supported local tips. The sidebar filter only filters reference labels.
- Find searches subjects, authors, IDs and reference labels in the loaded window. Enter selects the next match without removing intermediate commits or changing connections. Ctrl/Command+F focuses this field; F3/Shift+F3 moves between matches.
- Details opens an optional overlay with the selected commit's identity and parents. Escape closes it. The overlay leaves the graph's layout intact.
- Review or Enter in the commit list opens the full parent-to-commit diff. Merges initially use the first parent; the existing endpoint panel selects another parent. Initial commits compare against the empty tree.
- The footer's Graph button returns to the retained selection. Switching repository tabs restores each tab's graph, search, reference filter and scroll position without rereading Git.
- Refresh or Ctrl/Command+R resolves current tips again. Saved tab views remain observations of an earlier repository state until refreshed.

## Snapshot and limits

Each initial request captures full tip IDs and reference labels. Load more reuses these IDs and rereads an expanded topological prefix; it does not page over moving branch names. The desktop refuses an extension if its already loaded ID/parent prefix changes. This is a bounded observation, not an atomic repository snapshot. Concurrent object removal, shallow-boundary changes and missing objects can cause explicit errors. A failed refresh retains the previous graph.

The shared service accepts at most 1,000 commits, 200 tip/reference entries and 32 parents per commit. HEAD occupies one reference slot when available. References are read in name order with an explicit limited-list notice. Direct commit references and annotated tags pointing directly to commits are included; tags pointing to other tags or non-commit objects are omitted and mark the list as limited. Selecting a specific local revision can inspect history outside the displayed reference list.

The desktop supports at most 12 simultaneous lanes. More complex histories receive an explicit request to narrow the reference selection rather than incorrect connections. Git stdout remains bounded to 2 MiB per command; the whole graph request has a 15-second default deadline. Full commit messages contribute to the raw Git output bound even though the graph returns only subjects. No persistent graph cache, full-history streaming, arbitrary graph editing or remote operations are implemented.

Git's [raw pretty format](https://git-scm.com/docs/pretty-formats.html) preserves true parent IDs, including shallow boundaries. Commit messages are parsed as indented text, never as executable content or record delimiters. Subjects, identities and reference names are untrusted data. Desktop labels have bounded previews; the structured result retains metadata within the transport limits.

## CLI

```sh
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll graph /path/to/repository --limit 200
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll graph /path/to/repository --start refs/heads/main --limit 400
```

The JSON envelope contains `scope` (root, fixed tips, reference labels, selection and limited-list flag), `commits` (full IDs, ordered parents, subject, author and authored timestamp), `limit`, `hasMore`, `isShallow` and `contentTrust`. `capabilities.graph` advertises availability and shared bounds. Each CLI invocation creates a fresh scope; the CLI does not yet accept a previous multi-tip scope. For repeatable single-tip requests, pass a full commit ID to `--start`. Integrations consume the same `IGraphService` port used by the desktop.

## Verification

Synthetic Git tests cover SHA-1/SHA-256 merges, annotated tags, remote-tracking references, moving tips, shallow parents, unborn repositories, message-like delimiters, index/object nonmutation, malformed provider topology and CLI validation. Headless desktop tests cover both themes, compact content geometry, virtualization, merge lane layout, bounded complexity, retained selection, tab isolation, late results and changed-prefix refusal. A separate isolated Windows test exercises native graph search, initial-commit review and return navigation. Native tests never run in the regular test solution.
