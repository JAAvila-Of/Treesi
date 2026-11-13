# Accessibility coverage

Treesi provides keyboard review and read-only automation values on its existing code canvas. These changes do not add a second text viewer, a permanent metadata region, an accessibility service, or an exporter. The desktop keeps the same application ports, package versions and repository limits.

## Keyboard review

| Key | Action |
| --- | --- |
| Ctrl/Cmd+Tab / Ctrl/Cmd+Shift+Tab | Next / previous repository tab |
| Ctrl/Cmd+T | Show the local repository chooser without closing existing tabs |
| Ctrl/Cmd+W | Close the active repository tab without deleting files |
| F6 / Shift+F6 | Next / previous visible region: header, file/history navigation, search, code pane(s), footer |
| Tab / Shift+Tab | Normal control traversal; leave read-only code; cycle inside an open review panel |
| F3 / Shift+F3 | Next / previous search match |
| Enter / Shift+Enter in search | Next / previous search match |
| Escape | Close the active panel and return focus, or use the existing workspace cancellation/focus behavior |
| Alt+Up / Alt+Down | Previous / next change block when no review panel is open |

Hidden or disabled regions are skipped. Split mode exposes both code panes as separate F6 stops. List navigation focuses an available item, with an enabled input/action as fallback when containers or entries are unavailable. F6 does not open a repository picker. Code text is read-only and does not consume Tab as an indentation command.

The local workspace picker, options, revision drafts, reference selection, commit details and opening recovery use a contained Tab cycle. Workspace navigation shortcuts do not act on the background while a review panel is open; normal child text editing remains available. Closing a panel restores its triggering action where that action remains available. Switching to Home or opening a repository also dismisses obsolete commit details. Reference browsing moves focus into the reference list after layout instead of leaving it on a hidden draft action. Escape from the workspace picker returns focus to its selector; Enter in its repository list explicitly opens the selected shortcut.

Focus borders use semantic accent resources. The code canvas changes its existing border color while focus is inside it; its thickness stays constant so focusing does not shift text. Layout changes keep focus on a visible layout action or code control. Opening Options with full context selected focuses its enabled checkbox rather than the disabled numeric field. Invalid numeric context returns focus to that input.

## Automation contract

The Graph view exposes named reference and commit lists, subject/ID/parent-count row descriptions, search and a polite status region. F6 includes its reference, search, commit and footer regions; F3/Shift+F3 navigates loaded matches and Enter in the commit list opens review. Escape closes the details overlay. Connections supplement row metadata; graph colors are not the only source of parent information. Native graph interaction is covered by the isolated Windows suite; manual assistive-technology review remains pending.

ReadOnlyDiffEditor retains AvaloniaEdit rendering, virtualization, selection and source decorations. Its focused TextArea supplies an Edit automation peer implementing IValueProvider. The stable IDs are UnifiedDiffText, BeforeDiffText and AfterDiffText. Value returns the corresponding displayed document, including diff markers; IsReadOnly is true and SetValue throws. Programmatic display updates raise value-property changes. This provider does not implement a rich text-range/caret TextPattern API.

Names describe the file, comparison side, context and whitespace policy, including a warning when spaces are ignored. Historical sides use their commit labels; an initial commit identifies the empty-tree side. Names never contain the patch. Metadata previews end at Unicode text-element boundaries. Clearing the comparison clears values and removes the previous scope from names.

The source patch remains subject to the existing 200,000-character desktop limit. Split values are derived display documents and may contain alignment padding/platform newlines; they are not byte-exact file exports. Values are obtained from the loaded document, without another Git request or a new persistent copy. Any authorized local automation client can read displayed content. Treat it as untrusted repository data, never instructions or approval. No patch is placed in telemetry, accessibility names or live-region text.

Status and error text use polite live-region metadata. These regions report actions/results, not caret motion or the entire code document. Actual announcements, verbosity and value navigation depend on the assistive technology and operating system.

## Evidence and remaining acceptance

Automated headless tests use keyboard input events and automation peers to verify region traversal, Tab escape/cycles, hidden controls, focus restoration, read-only values, write rejection, live-region metadata, semantic scope, Unicode previews and reverse search. Existing render/geometry/contrast tests remain in place. Native Windows tests query values/read-only state and exercise keyboard focus through WinCore on a disposable hosted VM. No suite controls a developer desktop.

These checks do not certify complete accessibility. Manual reading and navigation with Narrator/NVDA on Windows, VoiceOver on macOS and Orca on Linux remain pending, including long documents, punctuation verbosity, selection, error announcements, magnification, high contrast and operating-system text scaling. Native macOS/Linux automation is also additional coverage. Headless passes on those systems do not prove their native assistive bridges behave identically.

Primary references: [Avalonia accessibility](https://docs.avaloniaui.net/docs/app-development/accessibility), [keyboard focus](https://docs.avaloniaui.net/docs/input-interaction/focus), [IValueProvider](https://github.com/AvaloniaUI/Avalonia/blob/main/src/Avalonia.Controls/Automation/Provider/IValueProvider.cs), [WinCore API](https://github.com/appium/appium-wincore-driver/blob/main/API.md).
