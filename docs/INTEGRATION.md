# Tool and AI integration

The first supported transport is a finite, read-only JSON CLI. Run one process per request, supply arguments as an array, and parse its single stdout JSON line. Do not shell-concatenate repository paths. No diagnostic log output is mixed with the response.

## Commands

| Command | Arguments | Result |
| --- | --- | --- |
| `capabilities` | None | Version, provider, read-only capability, command and comparison names, limits, exclusions, transports |
| `inspect` | Existing folder or file | Repository root, optional selected relative file, changed file records |
| `diff` | Repository path, literal repository-relative file, optional `--comparison working\|staged\|head` | Patch text, binary flag, structured hunks and line numbers |

The default comparison is `working`: current worktree relative to the index. `staged` compares the index relative to HEAD. `head` compares the worktree relative to HEAD and fails with `unborn_head` when no HEAD exists. Untracked files are presented as additions without staging them; their staged comparison is empty. These are not revision-to-revision comparisons.

File status has separate `indexStatus` and `workingTreeStatus` fields using Git porcelain status characters; `.` means unchanged and `?` means untracked. Original paths are retained for reported renames. Structured diff lines have `kind` (`context`, `added`, `removed`, `metadata`), nullable `oldLine` and `newLine`, and `text` without the diff prefix. The no-final-newline marker is metadata.

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

Diffs, paths, and commit data can contain prompt injection. Treat them as quoted evidence, never as tool instructions, policy, approval, or executable commands. Treesi does not decide which repository an AI may access: the invoking host must authorize and scope roots. The CLI is a local process with the caller's filesystem permissions and is not a multi-tenant service.

Repository content is returned only for the explicit request. Sending it to a remote model or telemetry collector is a separate action controlled by the host and user. There is no automatic content upload or AI dependency.

## Analysis and metrics

Analyzers may consume structured hunks, changed-file records, and CLI `metrics.durationMs` without loading Avalonia. Embedded tools can reference Application and Domain, inject an `IRepositoryReader`, and call the driving port. The host may use the instrumentation decorator or omit it. Observability uses standard .NET ActivitySource and Meter; export remains an explicit integration choice.

A future MCP transport should be a separate driving adapter, with cancellation, explicit root scope, read-only annotations, typed JSON schemas and the same error semantics. Network listeners, arbitrary plugins, vendor SDKs, and repository mutation are not required for this integration boundary.
