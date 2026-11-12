# Appearance and UI verification

The workspace provides Light and Dark themes with a restrained green accent, clear type hierarchy, compact actions, and distinct repository, file-list, and diff surfaces. Theme changes apply immediately and preserve the current file, comparison, filter, and text selection.

The visual shell uses a rounded workspace frame, a compact repository card, a small file heading, colored addition/removal counters, and a framed diff canvas. A single-row toolbar exposes endpoints, Unified and Side by side, file-panel visibility and change-block navigation. Search stays beside the file heading. Selected files and keyboard focus have explicit outlines; color is accompanied by status text and diff prefixes.

The diff is the primary surface. Geometry checks require it to occupy at least 80% of the workspace body's height and 60% of its area in both layouts and themes, at 900 × 560 and 1180 × 740. These are layout checks, not accessibility or performance acceptance. Long headings are trimmed rather than allowed to shrink the code area; editor text remains 13 px.

Ctrl+B folds the files; Ctrl+Shift+F or the footer's Focus button folds the application header and files together. Search and comparison tools remain available. Escape restores the workspace before cancelling an inspection. Exiting focus restores the prior file-panel state; showing files while focused exits focus and opens the panel. Hiding a focused navigation panel moves keyboard focus to the active editor's text area. Selection, filter, search, mode and loaded content survive without another Git query. File-panel visibility is remembered per recent workspace; focus mode is session-only.

## Opening and resuming

The home surface lists up to eight local recent repositories in most-recently-opened order. Select an entry and resume with its button or Enter; open-file and open-repository actions remain available. A command-line path takes precedence over the list. No recent location is inspected until explicitly opened. Returning to recents keeps the loaded comparison available through Back to comparison.

`IWorkspaceHistoryStore` isolates presentation state from its JSON adapter; `WorkspaceHistory` owns ordering, deduplication, validation and removal. The version-1 document in local application data under `Treesi/workspaces.json` contains only roots, repository-relative selection, comparison, split/unified layout and file-panel visibility. Reads and writes are bounded to 64 KiB. Unknown versions, malformed or oversized documents and invalid entries are ignored. Writes replace the document atomically in its own directory. Multiple app instances currently use last-writer-wins settings; there is no cross-process merge.

View state is updated when visiting recents, before switching locations, after a successful repository opening and on close, rather than writing on every selection or keystroke. Focus, filter, search text, patches and credentials are excluded. Removing or clearing entries only updates local shortcuts; closing the current workspace does not reinsert a forgotten entry. Save failures retain the in-memory session and report failures while the window is available.

A new location's repository is inspected before replacing the current workspace. Failure or cancellation of that opening inspection keeps an existing comparison visible, with retry, another-location and dismissal actions. Once the repository is open, its selected diff is loaded separately and can be cancelled with an explicit incomplete-result state. Superseded responses cannot replace a newer workspace. A missing saved file produces an explicit inspection error; the file panel remains available to select another change. Opening failures with no previous workspace show recovery on Home, including missing Git. File/folder picker cancellation leaves the current view unchanged.

Headless checks cover metadata round trips and limits, stale requests, cancellation, startup precedence, reopening a saved view, forgetting entries, save failures and keyboard recovery. The hosted native suite also resumes and removes a recent shortcut through accessibility controls, verifying that repository files remain present.

## Motion

Buttons respond to hover and press with 120 ms transitions. File selection and layout segments blend their backgrounds over 140 ms. Loaded diffs and layout changes use a single 180 ms ease-out reveal with a four-pixel translation and opacity from 0.88 to 1. Theme changes briefly reveal the file heading. Text is never scaled or animated during scrolling, and there are no decorative idle loops. The activity line animates only while inspection is busy.

`WorkspaceMotion` owns cancellation: replacing an animation, showing a non-diff state, reducing motion, or closing the window stops the previous reveal. Content, focus, and text selection remain immediately available; actions never wait for a decorative transition. No animation library or additional runtime dependency is required.

The footer's Reduce motion setting disables these custom reveals and interaction transitions and keeps the activity line static. `IMotionPreferenceStore` separates the setting from its JSON adapter, which writes atomically to local application data under Treesi/motion.json. Save failures preserve the current session and report a message. This explicit app setting does not currently follow the operating system's motion preference or disable every built-in Fluent control effect.

Headless tests verify completion, replacement/cancellation, stable final appearance, persisted preferences and save failures, interaction without Git reloads, and compact layout. Optional `TREESI_MOTION_OUTPUT` exports a synthetic headless walkthrough as PNG frames; it is a visual diagnostic, not a native frame-rate or GPU benchmark. See the official [Avalonia keyframe animation reference](https://docs.avaloniaui.net/docs/graphics-animation/keyframe-animations).

## Theme structure

`ThemeCatalog` registers theme IDs, display names, and Avalonia variants. `WorkspaceStyles.axaml` defines semantic brushes for surfaces, text, borders, selection, additions, removals, and hunk headers. Layout and controls reference those resources dynamically. The diff renderer resolves brushes against the editor's active variant rather than hardcoding light or dark colors.

To introduce another built-in theme, register its definition and supply its resource dictionary, using a variant derived from Light or Dark for Fluent controls. Do not add theme-specific branches throughout the view. Arbitrary downloaded theme packages are not supported.

`ThemeController` coordinates application appearance. `IThemePreferenceStore` separates presentation from preference persistence; `JsonThemePreferenceStore` writes a small JSON file atomically under the platform's local application data directory. Dark is the fallback for a missing, invalid, or unknown preference. Save failures leave the new theme active for the session and display a message. Theme preferences never write to a repository.

## Diff presentation

The unified viewer uses the MIT-licensed AvaloniaEdit control for text virtualization and selection. Changes have both `+`/`−` prefixes and themed backgrounds. The gutter displays the original and new file line numbers from the structured diff, not patch-row numbers, and expands for large line numbers. Hunk headers and file metadata are visually secondary. Hyperlink and email activation are disabled; repository content remains inert text.

The layout selector switches between Unified and Side by side without querying Git again. Split mode projects each loaded hunk into two aligned, virtualized editors; adjacent removal/addition runs are paired by row position, with empty cells for unmatched rows. This is positional alignment, not semantic matching. Original source numbers remain visible in each gutter, and both panes scroll vertically together. Horizontal scrolling remains independent. Only changed hunks and their context are shown; gaps between hunks remain explicit headers rather than invented source lines.

Before/after labels reflect the selected comparison (HEAD, Index, or Working tree). Search starts in the last focused split pane and continues into the other pane before wrapping. Previous/next block buttons, or Alt+Up/Alt+Down, cycle through hunks and keep the active block when changing layouts. Switching themes preserves the loaded diff and selection. Binary, empty, failed, or oversized previews clear both panes and disable navigation.

Source-language highlighting, word-level comparisons, full-file context, and conflict editing remain separate product work. The existing 200,000-character preview limit remains in force. All editors remain read-only and disable hyperlink activation.

## Local revision workspace

Compare opens an in-window draft with base, target, mode, Swap and an explicit reference browser. The smaller draft expands for a reference list or error, while keeping actions reachable at compact sizes. Browsing is optional and bounded; choosing a ref fills one endpoint without applying the comparison. Cancel/Escape preserves the loaded view. Resolution/list failures retain the previous comparison and show an actionable error.

Successful comparison reuses the primary code canvas and file navigation. The sidebar identifies changes between commits; rows do not call them staged or working. The endpoint button shows Direct or Merge base and abbreviated effective-base/target IDs, with full requested names and IDs in its accessible description and tooltip. Split panes label the effective base and target commits. Unified view retains mode and direction in the endpoint button.

Files and options reuse pinned endpoints. Refresh explicitly resolves requested names again. Working changes returns to a fresh inspection and restores the working selection. Historical selections do not overwrite recent-workspace metadata, and the comparison remains session-only. Empty lists refer to the compared commits, while filters and whitespace-empty results retain their separate messages. Layout remains subject to the same 80% height and 60% area checks.

## Comparison option controls

The file heading exposes Options without adding another toolbar row. Its in-window panel edits a draft: choose 0–50 context lines and Exact, Ignore trailing space, Ignore space changes, or Ignore all space. Apply reloads only a changed request; Cancel, Escape and navigation discard the draft. Defaults changes the draft and still requires Apply. Fractional or missing context values keep the panel open with an error. Context appears beside the selected path; the button tooltip describes both active options.

A WS ignored badge remains visible for every non-Exact mode, even with an empty result. Empty filtered results say No differences for these options and explain how to return to Exact. The file list continues to reflect unfiltered Git status, and counters reflect the loaded filtered hunks. Whitespace filtering can hide significant string contents, so it is never represented as an exact or clean result. Filtered context follows Git's patch rather than reconstructing both whitespace variants.

Options are session-only, excluded from recent-workspace metadata and restored to 3/Exact when the repository root changes. Applying new options cancels superseded loads; late results cannot overwrite a newer selection. Switching layout or theme retains the current options without reading Git again. Existing timeout, output and UTF-8 boundaries remain unchanged; context is not a request for unlimited content.

Headless tests cover draft cancellation, unchanged Apply, defaults, invalid input, filtered-empty recovery, stale requests, root changes and compact bounds in both themes and layouts. The code canvas retains its height and area requirements with the warning visible. Contrast checks include warning text against the panel. Native isolated verification types context, selects whitespace, applies changes and verifies the persistent warning and return to defaults.

## Headless verification

UI tests use Avalonia Headless with Skia rendering. Controls, layout, theme resources, and editor content exist in memory. A fake inspection service supplies test data, and an in-memory preference store isolates appearance tests from application settings.

JAAvila.FluentOperations assertions cover theme switching and selection preservation, preference round trips and failure handling, compact layout bounds, empty states, filtering, search, and comparison changes. Split-view tests cover unequal replacements, one-sided changes, metadata ownership, source numbers, gaps, navigation wraparound, vertical scroll synchronization, and clearing stale data. Tests check 4.5:1 contrast for selected semantic text/background pairs in both themes and sample rendered pixels to confirm that diff backgrounds actually render. These checks are not a complete accessibility audit.

```sh
dotnet test tests/Treesi.Desktop.Tests --configuration Release
```

For local visual review, optionally set `TREESI_RENDER_OUTPUT` to an explicit directory before running the tests. The suite saves in-memory renders of Light, Dark, empty, and compact workspaces there. No native screen capture or input automation is involved. Image files are diagnostics, not accepted visual baselines; inspect them before adopting a baseline.

## Source presentation

Full file context is available in the same Options draft. Reloads retain the previous code on errors, cancellation or preview-limit refusal, with a source-line anchor on successful expansion/contraction. See [full-context review](FULL_CONTEXT.md). It adds no permanent toolbar or extra canvas row.

Code token colors and inline edit backgrounds use semantic theme resources and one cached presentation model across unified/before/after editors. Options drafts include two display toggles; Apply changes presentation without another repository request, Cancel discards drafts, and Defaults restores both to enabled. Display choices last for the process session across repositories and are not persisted in recents. Search, selection and loaded text remain unchanged. The document model and its limits are described in [source presentation](SOURCE_PRESENTATION.md).

Primary references: [Avalonia headless platform](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform), [dynamic theme resources](https://docs.avaloniaui.net/docs/app-development/resources), [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit).
