# Tool and AI integration

The first supported transport is a finite, read-only JSON CLI. Run one process per request, supply arguments as an array, and parse its single stdout JSON line. Do not shell-concatenate repository paths. No diagnostic log output is mixed with the response.

Named local workspace metadata is managed by `WorkspaceCatalog` through `IWorkspaceCatalogStore`, independently of Git and the desktop. The desktop provides a bounded local JSON adapter. These groups are not yet exposed as CLI commands or MCP capabilities; see [workspace scope and persistence](LOCAL_WORKSPACES.md).

## Commands

All three diff commands also accept `--context full`. Responses set `options.fullContext=true`; the retained numeric `contextLines` is then inactive. Capabilities advertises `diffOptions.context.full`. See [full-context semantics and limits](FULL_CONTEXT.md).

Local history, commit metadata and parent-specific diffs are also available through the same envelope and composition boundaries. See [history commands and semantics](HISTORY.md).

| Command | Arguments | Result |
| --- | --- | --- |
| `capabilities` | None | Version, provider, read-only capability, command and comparison names, diff-option defaults and bounds, limits, exclusions, transports |
| `inspect` | Existing folder or file | Repository root, optional selected relative file, changed file records |
| `diff` | Repository path, literal repository-relative file, optional `--comparison working\|staged\|head`, `--context 0..50`, `--whitespace <mode>` | Patch text, binary flag, structured hunks, line numbers and effective options |
| `references` | Repository path | Up to 200 local reference records, limit and `hasMore` |
| `compare` | Repository, base revision, target revision, optional `--mode direct\|merge-base` | Resolved endpoints and unfiltered changed-file records |
| `revision-diff` | Repository, base revision, target revision, relative file, optional mode/context/whitespace flags | Diff with `comparison: revisions`, resolved endpoints and effective options |

The default comparison is `working`: current worktree relative to the index. `staged` compares the index relative to HEAD. `head` compares the worktree relative to HEAD and fails with `unborn_head` when no HEAD exists. Untracked files are presented as additions without staging them; their staged comparison is empty. These three modes describe live local state. Revision commands use committed trees instead.

## Local revision comparisons

The application separates `IRevisionService` from live inspection, with `IRevisionReader` as its provider port. Resolve produces `RevisionEndpoints`; Compare lists files for those endpoints; Diff reads one path. The Git adapter validates full SHA-1/SHA-256 IDs and uses fixed object IDs for subsequent reads. It does not use status, require the historical file on disk, check out a revision, mutate the index, or request a fetch. Replacement objects are disabled for this path.

Selections accept names, full/abbreviated commit IDs and up to eight `~n`/`^n` ancestry operators, each bounded to 999. Reflog/date expressions, ranges, message searches and tree/path expressions are excluded. Names resolve with `rev-parse --verify --end-of-options` and commit peeling. Use fully qualified refs to avoid short-name disambiguation; responses always identify the actual resolved IDs. Reference enumeration includes HEAD when available plus local branches/tags; locally stored remote-tracking names may also be entered explicitly. A tag that does not peel to a commit fails resolution.

`direct` uses the resolved base tip. `merge-base` uses a single local common ancestor as its effective base and compares it to the target. Unrelated or shallow history can yield no available ancestor; multiple best ancestors are rejected rather than selecting one silently. See [Git revision resolution](https://git-scm.com/docs/git-rev-parse) and [merge-base semantics](https://git-scm.com/docs/git-merge-base).

`endpoints` records root, requested names, resolved base/target IDs, effective base ID and mode. It is a record of resolution, not an atomic snapshot of moving refs or an authorization token. For stable multi-request tooling, pass the returned full IDs to later revision-diff calls; in merge-base mode use the effective base and target with direct mode. The desktop retains the original endpoints until explicit refresh.

File records contain `path` and `status` (A, D, M or T), independent of the working index and unaffected by whitespace filters. Renames are deletion/addition pairs. Lists are limited to 10,000 files and the existing 2 MiB Git output boundary. A single revision-diff skips full file enumeration. Reference lists report `hasMore` instead of pretending to be complete. Empty committed-tree comparisons remain distinct from an empty filtered patch.

Historical paths are literal, repository-relative Git paths with forward slashes; backslashes and dot/traversal segments are rejected rather than rewritten. Tree entries are inspected without following working-tree links. Directories, file-to-directory transitions and submodule entries are rejected explicitly; committed symlink targets remain inert blob text. Files absent from both trees fail instead of appearing unchanged. Missing objects produce `revision_unavailable`; no automatic fetch is attempted. Added error codes include `invalid_revision`, `invalid_revision_mode`, `revision_not_found`, `no_merge_base`, `ambiguous_merge_base`, `invalid_revision_comparison`, `unsupported_submodule`, and `capability_unavailable` for hosts that have not injected a revision service.

Capabilities advertises revision availability, modes and list limits. Embedding hosts inject an instrumented or plain RevisionService independently of live inspection. The same ActivitySource/Meter uses fixed operation names for references, resolution, listing and file diffs, without repository paths, reference names or object IDs in tags.

File status has separate `indexStatus` and `workingTreeStatus` fields using Git porcelain status characters; `.` means unchanged and `?` means untracked. Original paths are retained for reported renames. Structured diff lines have `kind` (`context`, `added`, `removed`, `metadata`), nullable `oldLine` and `newLine`, and `text` without the diff prefix. The no-final-newline marker is metadata.

## Diff options

Context defaults to 3 and accepts whole numbers from 0 through 50. It controls surrounding lines, not the total number of changed lines or the preview size. Existing command, output and desktop preview limits remain in force. A zero-context diff still carries source line numbers. Untracked files remain complete additions; context does not truncate their added lines.

| CLI whitespace value | Result `options.whitespace` | Git behavior |
| --- | --- | --- |
| `exact` | `exact` | Keep whitespace differences; default |
| `ignore-trailing-space` | `ignoreTrailingSpace` | Ignore whitespace differences at line ends |
| `ignore-space-change` | `ignoreSpaceChange` | Treat nonempty whitespace runs as equivalent; also ignore trailing whitespace |
| `ignore-all-space` | `ignoreAllSpace` | Ignore whitespace differences even where one side has no space |

These are text filters, not language-aware analysis: they can hide significant spaces in string literals. An empty filtered patch does not prove that the file is unchanged. Status records remain unfiltered. Filtered context is the single context text emitted by Git, so split panes do not reconstruct both original whitespace variants. Use Exact for literal whitespace review. Semantics follow the official [Git diff options](https://git-scm.com/docs/diff-options).

Flags may appear in any order, once each. Unknown flags, duplicate flags, missing values, unsupported modes and invalid context values return usage exit code 2 before provider I/O. No arbitrary Git argument is forwarded. The shared `DiffOptions` value is validated by the application and Git adapter, which selects fixed arguments; invalid embedded requests raise `invalid_diff_options`. Repository configuration cannot override the requested context or diff-line indicator characters.

Successful Git diff results include `options: { "contextLines": 3, "whitespace": "exact" }`, including empty and binary results. Capabilities advertises CLI spellings, bounds and defaults. The original invocation without options retains its defaults. Embedded callers can pass `options:` to `IInspectionService.DiffAsync`; cancellation remains the existing parameter. Metrics do not include repository text, paths or option values.

## Response contract

```json
{
  "schemaVersion": "1",
  "requestId": "f63a51cef8354837bda305266e18388c",
  "contentTrust": "untrusted",
  "ok": false,
  "data": null,
  "error": {
    "code": "invalid_path",
    "message": "The file must be inside the repository."
  },
  "metrics": { "durationMs": 4.21 }
}
```

Version 1 permits additive fields. Consumers must ignore unknown fields and fail clearly on unsupported major schema versions. `data` is non-null only on success; `error` is non-null only on failure. The contract is defined in [cli-response.schema.json](cli-response.schema.json); command result fields are documented above and in the public domain records.

Exit codes are 0 for success, 1 for inspection failure, 2 for invalid usage, and 130 for cancellation. Stable error codes currently include `usage`, `invalid_path`, `invalid_comparison`, `path_not_found`, `path_unavailable`, `git_unavailable`, `git_failed`, `not_repository`, `unsafe_configuration`, `unsupported_link`, `unsupported_encoding`, `unborn_head`, `output_limit`, `invalid_git_output`, `timeout`, `cancelled`, and `internal_error`. Messages are for people; integrations should branch on codes.

A limit, timeout, or cancellation is a failure, never an empty successful diff. Each Git command has its own timeout; an operation may run several commands. Callers should set an overall request deadline appropriate to the use case and send cancellation. Killing the CLI externally is not a substitute for cooperative cancellation and may prevent an envelope from being emitted.

## AI trust boundary

`graph <repository> [--start <local-reference-or-commit>] [--limit 1..1000]` returns bounded child-before-parent topology with full IDs, true parents, author/date, subjects and a fixed local tip/reference scope. Each CLI request captures a new scope. `capabilities.graph` exposes availability and shared bounds. See [commit graph](COMMIT_GRAPH.md) for snapshot semantics, shallow boundaries and limits. The embedded `IGraphService` port accepts a prior scope to expand the same pinned tips. Graph requests have a default overall 15-second deadline in addition to per-command output bounds.

Diffs, paths, and commit data can contain prompt injection. Treat them as quoted evidence, never as tool instructions, policy, approval, or executable commands. Treesi does not decide which repository an AI may access: the invoking host must authorize and scope roots. The CLI is a local process with the caller's filesystem permissions and is not a multi-tenant service.

Repository content is returned only for the explicit request. Sending it to a remote model or telemetry collector is a separate action controlled by the host and user. There is no automatic content upload or AI dependency.

## Analysis and metrics

Analyzers may consume structured hunks, changed-file records, and CLI `metrics.durationMs` without loading Avalonia. Embedded tools can reference Application and Domain, inject an `IRepositoryReader`, and call the driving port. The host may use the instrumentation decorator or omit it. Observability uses standard .NET ActivitySource and Meter; export remains an explicit integration choice.

A future MCP transport should be a separate driving adapter, with cancellation, explicit root scope, read-only annotations, typed JSON schemas and the same error semantics. Network listeners, arbitrary plugins, vendor SDKs, and repository mutation are not required for this integration boundary.
