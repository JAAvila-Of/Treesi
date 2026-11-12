# Appearance and UI verification

The workspace provides Light and Dark themes with a restrained green accent, clear type hierarchy, compact actions, and distinct repository, file-list, and diff surfaces. Theme changes apply immediately and preserve the current file, comparison, filter, and text selection.

The visual shell uses a rounded workspace frame, a compact repository card, a clear file heading, colored addition/removal counters, and a framed diff canvas. A segmented layout control exposes Unified and Side by side directly. Selected files and keyboard focus have explicit outlines; color is accompanied by status text and diff prefixes.

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

## Headless verification

UI tests use Avalonia Headless with Skia rendering. Controls, layout, theme resources, and editor content exist in memory. A fake inspection service supplies test data, and an in-memory preference store isolates appearance tests from application settings.

JAAvila.FluentOperations assertions cover theme switching and selection preservation, preference round trips and failure handling, compact layout bounds, empty states, filtering, search, and comparison changes. Split-view tests cover unequal replacements, one-sided changes, metadata ownership, source numbers, gaps, navigation wraparound, vertical scroll synchronization, and clearing stale data. Tests check 4.5:1 contrast for selected semantic text/background pairs in both themes and sample rendered pixels to confirm that diff backgrounds actually render. These checks are not a complete accessibility audit.

```sh
dotnet test tests/Treesi.Desktop.Tests --configuration Release
```

For local visual review, optionally set `TREESI_RENDER_OUTPUT` to an explicit directory before running the tests. The suite saves in-memory renders of Light, Dark, empty, and compact workspaces there. No native screen capture or input automation is involved. Image files are diagnostics, not accepted visual baselines; inspect them before adopting a baseline.

Primary references: [Avalonia headless platform](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform), [dynamic theme resources](https://docs.avaloniaui.net/docs/app-development/resources), [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit).
