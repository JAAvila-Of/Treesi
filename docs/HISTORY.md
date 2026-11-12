# Local history

History is a read-only capability shared by desktop and JSON CLI through IHistoryService and IHistoryReader. It traverses ancestors of one locally available start commit, not every branch. Git uses topological order and full-history traversal. A path filter is literal, repository-relative and uses forward slashes; deleted paths remain queryable. Rename following is disabled. Path-limited traversal can retain merges; original parents come from the commit object rather than a rewritten log graph.

## Desktop

History replaces the existing file list, preserving the main diff canvas. Select Repository or Selected file, enter a start revision and choose Go. A page contains up to 30 commits. Previous and Next use its pinned start ID and path, even if the draft revision changes. Go or changing scope starts a new traversal. Selecting an entry compares it to its first parent and loads the preferred file, or the first changed file if absent. Files returns to that commit's changed-file list; History reopens the loaded page.

The endpoint button opens selectable plain-text commit details on demand. Identities appear as recorded, including timestamp/offset headers. The parent selector uses one-based order from the object. Initial commits have one empty-tree comparison. Refresh of a selected history commit keeps its pinned IDs; use Go to resolve a moving reference again. Working changes returns to fresh working-tree inspection. History pages, messages, selections and draft revisions are session-only, excluded from recent-workspace settings. Late results cannot replace a newer root or selection.

## JSON CLI

To keep layout work bounded, desktop rows preview at most 256 subject characters and 80 author characters. Details preview 16,000 message characters with an explicit truncation notice, plus bounded identity headers. The CLI retains complete metadata within the subprocess output limit.

```sh
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll history /path/to/repository --limit 30
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll history /path/to/repository --start HEAD --path src/example.cs
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll history /path/to/repository --start <startCommitId> --offset 30 --limit 30
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll commit /path/to/repository HEAD
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll commit-files /path/to/repository HEAD --parent 2
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll commit-diff /path/to/repository HEAD src/example.cs --parent 1 --context 10
```

Responses retain envelope version 1. Capabilities advertises history availability, commands and bounds. Embedding hosts inject an optional history service independently; commit-files and commit-diff also require the revision service.

history returns root, startCommitId, path, offset, limit, items and nullable nextOffset. Items include commitId, subject, author and authoredAt. Continue with the returned complete startCommitId and nextOffset; keep limit consistent to reproduce page boundaries. Offsets greater than zero require a complete commit ID. Pages allow 1–50 entries, default 30, and offsets 0–10,000. The desktop stops at the browsing limit. These bounds limit returned data, not traversal complexity; existing 15-second command and 2 MiB output limits still apply. There is no expensive total count.

commit returns root, commitId, ordered parents, author, committer and the full raw UTF-8 message. Original objects are read with replacements disabled. Non-UTF-8 object data and NUL metadata are refused. Shallow boundaries retain recorded parents; an unavailable parent causes revision_unavailable during comparison, rather than an incorrect empty-tree diff. Traversal sees only locally exposed history. Moving the start reference does not affect continuation; external changes to objects or shallow metadata can invalidate it.

commit-files returns RevisionComparison; commit-diff returns FileDiff with context/whitespace flags. --parent defaults to 1 and selects an existing one-based parent. An initial commit accepts only 1 and returns endpoints.isRootComparison=true. All three commit-ID fields then pin the initial commit; the flag defines the empty base. Consumers must not interpret effectiveBaseCommitId as a base tree when this flag is true. Normal comparisons return false. No empty-tree object is written or fetched.

Typed errors include invalid_history_page, invalid_commit, invalid_parent and existing revision errors. Unknown, duplicate or malformed CLI flags fail with usage. Text and paths are untrusted, including instructions embedded in messages: never commands, approval, or model policy. Instrumentation uses fixed history/commit operation tags without IDs, names, paths or messages.

Git semantics: [git-log](https://git-scm.com/docs/git-log), [git-cat-file](https://git-scm.com/docs/git-cat-file), [git-diff-tree](https://git-scm.com/docs/git-diff-tree).
