# Related worktrees

Open the workspace chooser in the upper left and select Related worktrees. Treesi reads the locations registered with the active repository. This is an explicit metadata query: opening a repository, choosing a workspace group or starting Treesi does not discover other folders automatically.

The transient selector shows location, branch or detached state, current/open-tab status and lock/prune annotations. Select a location and choose Open selected or press Enter. An existing tab resumes its saved view without another inspection; a new location goes through normal repository opening and receives independent review state. No checkout, branch switch or repository modification occurs. Close or Escape returns focus to the workspace chooser; Tab stays within the selector.

Locked worktrees remain readable: the lock protects Git's administrative lifetime and does not make the folder read-only. Bare repositories and prunable records are listed with opening disabled. Other registered paths are not assumed to be available; explicit opening can fail if a location was moved, is offline or has unsafe configuration. Use normal opening recovery; Treesi does not repair or prune metadata.

To keep a discovered location in a named workspace, open it first, select the workspace and use Add current. Discovery never changes saved groups or opens several repositories at once. The selector is dismissed when changing repository or opening another navigation panel; superseded results are discarded.

## Port and CLI

`IWorktreeService.ListAsync` is a shared application port with a Git adapter and an optional instrumentation decorator (`worktrees` operation). The command is:

```sh
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll worktrees /path/to/repository
```

The JSON envelope's data contains `root`, `items` and `contentTrust`. Each item includes its root, commit ID (null for bare/unborn states), branch, bare/detached/locked/prunable flags and optional lock/prune reasons. `capabilities.worktrees` reports availability and the 128-entry limit. Metadata is untrusted evidence, including paths and annotation text. A host must authorize the query and any subsequent access to another root separately; discovery does not authorize an AI to open every returned location.

The adapter uses Git's [NUL-delimited porcelain worktree list](https://git-scm.com/docs/git-worktree#_porcelain_format), including when called from a linked working tree. It does not run status or inspect files in the returned locations. Paths are limited to 4,096 characters, annotations to 4,096 characters and lists to 128 entries; oversized results fail explicitly. Existing Git process bounds remain 2 MiB stdout, 64 KiB stderr and 15 seconds per command. Discovery may use several commands. Unknown, duplicate or incomplete porcelain attributes are rejected. Desktop labels replace control characters with spaces; CLI paths retain their original JSON-escaped text. No filesystem-alias deduplication beyond existing tab-root normalization is promised.

## Verification

Synthetic repositories cover SHA-1/SHA-256 linked trees, detached heads, lock reasons, unusual paths, unborn heads, bare primary repositories, prunable records and index/registration nonmutation. Application tests reject inconsistent provider records and duplicate locations. Headless tests cover both themes at 900 × 560, explicit discovery, tab reuse, blocked actions, retries, focus containment and late-result rejection. The isolated Windows scenario discovers a real linked worktree, reviews its changes and resumes the original tab. Native automation remains excluded from the regular test solution.
