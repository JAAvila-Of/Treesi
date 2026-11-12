# Source presentation

Treesi decorates the loaded diff inside the desktop adapter. Domain records, patches, JSON CLI results and Git comparison options remain unchanged. No repository file, grammar, extension, editor configuration or language server is loaded for coloring. There are no new packages or runtime services.

## Fragment colors

Files with .cs/.csx extensions use the finite C# fragment lexer; .json uses JSON string, numeric and literal colors. Matching is case-insensitive. Other extensions keep ordinary diff colors. The lexer identifies keywords, strings, numbers and comments where supported; it is not a syntax validator or semantic parser. C# escaped identifiers are not keywords. Comments, verbatim strings and raw quoted strings retain state independently on the before and after sides of a change block. Context rows advance both states and display the after-side colors. Metadata is excluded.

Each hunk starts with unknown surrounding source and resets lexical state. A fragment that begins inside a multiline construct can therefore be colored imperfectly. Interpolations and newer language constructs are not semantically interpreted. Turn Code colors off when lexical hints are distracting. Colors never establish that a program or JSON document is valid.

Analysis retains styles for at most 200,000 source characters per diff. A line longer than 4,096 UTF-16 code units disables lexical coloring for the remainder of that side's hunk, avoiding a false continuation state after an unscanned line. Resource bounds fall back to ordinary diff rendering; text remains selectable and unmodified.

## Inline changes

Within each contiguous change run, removed and added source lines are paired by position. Metadata such as the missing-final-newline marker is excluded. Unpaired lines keep only their existing row background. This is a positional review aid, not moved-line detection, semantic matching or a new Git diff algorithm.

Comparison uses Unicode text elements, preserving surrogate pairs, combining sequences and joined emoji at range boundaries. A bounded longest-common-subsequence calculation allows several distinct changed ranges per pair. It accepts up to 256 text elements per line and spends at most 500,000 matrix cells across the diff. On budget exhaustion or larger element counts it falls back to common prefix/suffix emphasis. Lines longer than 4,096 UTF-16 code units and entirely unrelated pairs keep only their full-row background. Repeated text can have several equally valid alignments; emphasis does not establish edit intent.

## Rendering and controls

One cached model supplies unified and side-by-side editors. Token colors come from Code.Keyword/String/Number/Comment resources; Diff.AddInline/RemoveInline supply stronger edit backgrounds. Signs, original/new line numbers and full-row added/removed backgrounds continue to carry change direction. Selection and search retain their existing behavior. Theme switching resolves fresh brushes without reading Git or rebuilding source text.

Code colors and Emphasize changed text are session-only display preferences in the existing Options draft. Apply redraws the editors without reloading the diff when context/whitespace options are unchanged. Defaults resets display toggles as well as Git diff defaults; Cancel/Escape discard the draft. Turning both off restores ordinary diff coloring. No permanent toolbar or metadata area reduces the code canvas.

Tests cover lexical separation and resets, JSON literals, escapes, Unicode boundaries, multiple edits, budget fallbacks, metadata/unpaired rows, both themes/layouts, rendered background pixels and token colors, contrast of source colors against each diff background, unchanged search/selection and repository-call counts. This is correctness coverage, not proof of application startup, memory or large-repository performance budgets.
