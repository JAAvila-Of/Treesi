# Full-context review

Full file context expands the surrounding text of a changed file in the existing unified or side-by-side canvas. It uses the same read-only Git diff pipeline as bounded context. It does not open an editor, check out revisions, read historical files from the working tree, fetch objects, or write the index. No packages were added.

## Desktop

Open Options, enable Full file context and Apply. The numeric context field is disabled while full context is selected; its previous valid value is retained for contraction. Turn the checkbox off and Apply to return to 0–50 surrounding lines. Defaults restores three lines and bounded context. Cancel/Escape discard the draft. When the numeric field is disabled, opening Options focuses the full-context checkbox instead.

The file heading identifies Full file context. Existing code colors, inline ranges, source line numbers, search and layouts remain available. Expanded context can merge Git hunks; full-context navigation therefore targets individual contiguous change runs, so separated edits remain reachable. Bounded context retains the existing hunk navigation.

Option reloads keep the previous code visible until a replacement result is accepted. Failures, cancellation, binary replacements or the desktop's 200,000-character limit retain that comparison and restore its previous Git diff options. The status explains that the retained result is older. Local display choices remain applied. A new file/root/selection supersedes the old request, and late results cannot replace it.

Expansion/contraction preserves a source-line anchor, using the visible source when the caret is off screen. A single-line selection is restored when its text still matches at the mapped location; otherwise the caret is positioned on that source line. Multiline selections and anchors removed by filtering are not guaranteed to survive. Search text remains available. This is best-effort positioning, not a promise that live repository contents stayed unchanged during reload.

## CLI and data contract

```sh
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll diff /path/to/repository src/example.cs --context full
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll revision-diff /path/to/repository HEAD~1 HEAD src/example.cs --context full
dotnet src/Treesi.Cli/bin/Release/net11.0/Treesi.Cli.dll commit-diff /path/to/repository HEAD src/example.cs --context full --parent 1
```

DiffOptions adds the boolean FullContext, default false. In JSON, `options.fullContext=true` marks full context and `contextLines` remains the retained bounded value. Numeric context must still be 0–50 in the typed contract. CLI `--context full` selects full mode; `--context <number>` selects bounded mode. Duplicate, unknown or malformed flags remain errors. Envelope version 1 is unchanged; new fields are additive. Capabilities advertises `diffOptions.context.full=true`.

The Git adapter uses a fixed context count greater than the number of lines any patch can return under its byte limit. This avoids extreme integer values in Git's hunk calculations. The same helper supplies live working/staged/HEAD comparisons, pinned local revisions and initial-commit diffs. Historical expansion reuses complete commit IDs without resolving moving names again.

## Limits

Existing limits still apply: 15 seconds per Git command, 2 MiB stdout, 1 MiB for a new untracked file, and 200,000 characters for a desktop patch. Full mode is explicit and never enabled automatically. Large files can be refused even if their narrow diff fits. The CLI has the same Git output/time bounds and is not an unlimited export.

Full context means surrounding text in a patch of a changed file. An unchanged file still produces no diff. Binary content, submodule contents and unsupported transitions retain their existing handling. Whitespace filters can still hide changes and can affect the text represented by context rows; full context is not an independent byte-exact file viewer. Untracked additions and deleted files already contain their complete added/removed text when within limits.

Primary semantics: [Git context options](https://git-scm.com/docs/diff-context-options), [git-diff-tree](https://git-scm.com/docs/git-diff-tree).
