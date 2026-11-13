# Local workspaces

A workspace is a named collection of local repository shortcuts. It is separate from a Git branch, a linked worktree, a recent-location list and an open repository tab. Workspaces do not require an account or hosting-provider integration.

## Use the selector

Click the top-left selector to open **Workspaces**. Choose **New workspace**, enter a name and select **Create**. To rename a group, edit its name and select **Save name**. Names are trimmed, contain 1–64 UTF-16 characters without control characters, and are unique ignoring case. The built-in **Recent repositories** entry is not an editable group.

For a selected workspace:

- **Add current** adds the already-validated active repository root without another Git request.
- **Add folder…** lets you choose a folder, validates it through the read-only inspection port, and stores the discovered working-tree root. It does not open a tab.
- **Replace location…** validates a replacement for the selected shortcut. This repairs a moved/unavailable location; it does not move or rename files.
- **Remove shortcut** changes group membership only.
- **Open selected** explicitly opens that repository, or restores its existing tab without reading Git again. Existing tabs keep their state.
- **Remove group** removes the metadata group; repository files, tabs and recent shortcuts remain intact.

Changing groups changes the picker list and its caption, not the active repository. All open tabs remain available across groups. A repository may belong to several groups. The repository name/path in the context row continues to identify the actual active tab, even when it is outside the selected group. No bulk opening, checkout, fetch or background polling occurs. [Related worktrees](RELATED_WORKTREES.md) discovers registered locations on request. Open one explicitly and use Add current to keep its shortcut in a named group; discovery does not modify groups automatically.

The **Recents** action and + / Ctrl/Cmd+T retain the existing Home chooser. Clearing recent shortcuts does not clear workspace membership, and removing a workspace does not clear recents.

## Persistence and limits

`LocalWorkspace` is a domain value. `WorkspaceCatalog` manages metadata through the `IWorkspaceCatalogStore` application port, independently of Git and Avalonia. The desktop JSON adapter stores a version-1 catalog in `Treesi/workspace-groups.json` under the platform's local application-data directory. It is separate from the existing `workspaces.json` recent-history file.

Only group IDs, names, normalized absolute roots and the selected group ID are stored. Patches, editor selections, open-tab sets, credentials and agent instructions are excluded. Startup reads the bounded settings file once and restores the selected group caption; it neither opens tabs nor checks the listed repositories. Missing roots remain visible as uninspected shortcuts until explicitly opened/validated. An opening error preserves the active comparison and offers existing recovery actions; the group shortcut can then be replaced.

Limits are 16 groups, 32 roots per group, 4,096 characters per root and a 256 KiB serialized file. Names, identities, selection and roots are validated as a whole. Roots are normalized with full-path/trailing-separator rules and platform-appropriate case comparison; the catalog does not claim to deduplicate every filesystem alias. A limit refusal never truncates a group or silently evicts a shortcut.

Writes replace the settings file atomically and clean up owned temporary files. A save failure leaves the edited catalog available in memory and displays **Session only**. Those changes will not survive a restart. A malformed, oversized or unknown-version file is preserved: the running catalog starts empty, reports the issue, and never saves over that file for the rest of the process. Correcting/recovering that settings file requires a restart before normal saving resumes. Persistence is a single-process preferences adapter; it does not merge concurrent edits from separate Treesi processes.

## Cancellation and accessibility

Opening the picker cancels unfinished inspection work and dismisses other drafts; a completed comparison remains available. New folder validation has its own cancellation source. Closing the picker, cancelling validation, changing groups, switching tabs or closing the window invalidates that request. Late results cannot update a different group. The existing Git timeout/output and executable-filter restrictions apply to explicit folder validation.

Tab/Shift+Tab stay inside the picker; Escape closes it and returns focus to its selector. Repository-list Enter opens the selected shortcut. Background review/session shortcuts are contained while the picker is open. Status text identifies successful metadata edits, uninspected roots, failed validation and persistence failures. Names and roots are displayed as text, never interpreted as commands or instructions. Manual assistive-technology/scaling acceptance remains separate from headless/native automation coverage.

The application port is available to future tool adapters. This change does not add workspace CLI commands, MCP transport, synchronization or analyzer execution.
