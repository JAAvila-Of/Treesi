# Appearance and UI verification

The workspace provides Light and Dark themes with a restrained green accent, clear type hierarchy, compact actions, and distinct repository, file-list, and diff surfaces. Theme changes apply immediately and preserve the current file, comparison, filter, and text selection.

## Theme structure

`ThemeCatalog` registers theme IDs, display names, and Avalonia variants. `WorkspaceStyles.axaml` defines semantic brushes for surfaces, text, borders, selection, additions, removals, and hunk headers. Layout and controls reference those resources dynamically. The diff renderer resolves brushes against the editor's active variant rather than hardcoding light or dark colors.

To introduce another built-in theme, register its definition and supply its resource dictionary, using a variant derived from Light or Dark for Fluent controls. Do not add theme-specific branches throughout the view. Arbitrary downloaded theme packages are not supported.

`ThemeController` coordinates application appearance. `IThemePreferenceStore` separates presentation from preference persistence; `JsonThemePreferenceStore` writes a small JSON file atomically under the platform's local application data directory. Dark is the fallback for a missing, invalid, or unknown preference. Save failures leave the new theme active for the session and display a message. Theme preferences never write to a repository.

## Diff presentation

The unified viewer uses the MIT-licensed AvaloniaEdit control for text virtualization and selection. Changes have both `+`/`−` prefixes and themed backgrounds. The gutter displays the original and new file line numbers from the structured diff, not patch-row numbers, and expands for large line numbers. Hunk headers and file metadata are visually secondary. Hyperlink and email activation are disabled; repository content remains inert text.

This is a unified diff presentation, not syntax-aware source editing. Side-by-side layout, source-language highlighting, conflict editing, and additional navigation remain separate product work. The existing preview size limit remains in force.

## Headless verification

UI tests use Avalonia Headless with Skia rendering. Controls, layout, theme resources, and editor content exist in memory. A fake inspection service supplies test data, and an in-memory preference store isolates appearance tests from application settings.

JAAvila.FluentOperations assertions cover theme switching and selection preservation, preference round trips and failure handling, compact layout bounds, empty states, filtering, search, and comparison changes. Tests check 4.5:1 contrast for selected semantic text/background pairs in both themes and sample rendered pixels to confirm that diff backgrounds actually render. These checks are not a complete accessibility audit.

```sh
dotnet test tests/Treesi.Desktop.Tests --configuration Release
```

For local visual review, optionally set `TREESI_RENDER_OUTPUT` to an explicit directory before running the tests. The suite saves in-memory renders of Light, Dark, empty, and compact workspaces there. No native screen capture or input automation is involved. Image files are diagnostics, not accepted visual baselines; inspect them before adopting a baseline.

Primary references: [Avalonia headless platform](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform), [dynamic theme resources](https://docs.avaloniaui.net/docs/app-development/resources), [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit).
