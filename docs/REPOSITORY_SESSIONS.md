# Repository tabs

The desktop can keep up to eight validated local repository roots open in one window. Open another repository through **Open repository**, **Open file**, a recent shortcut, or the **+** action. The + action and Ctrl/Cmd+T show the existing local-location chooser; they do not create a repository or start a disk scan. Opening a location validates it through the existing inspection port before adding a tab. A failed or cancelled opening retains the current view. A ninth distinct root is refused with recovery guidance; no tab is silently evicted.

## Switching and closing

Click a tab or use Ctrl/Cmd+Tab and Ctrl/Cmd+Shift+Tab to switch. Returning to a tab restores its loaded snapshot without requesting Git again. File selection, path filter, comparison, diff options, display toggles, layout, search, text selection, horizontal/vertical reading positions, file-panel/focus state, loaded history page and pinned revision/commit context belong to that tab. Theme and reduced-motion preferences belong to the application.

Restored views are explicitly labeled **Saved tab view**. They may be stale after external repository changes; Refresh performs the existing explicit inspection or historical refresh. Incomplete comparisons are shown as paused and require Refresh. Switching away cancels and invalidates pending opening, diff, reference, history and revision requests. Deferred layout restoration is scoped to the active session generation. A late response cannot replace another tab or revive a cancelled request after a round trip.

The close button or Ctrl/Cmd+W closes the active tab and releases its retained state. Other tabs keep their views. Closing the final tab returns to Home, with the local repository chooser and theme control still available. Closing a tab never deletes a repository, worktree, branch or recent shortcut. Ctrl/Cmd+T, tab cycling and closing do not operate behind an active options, revision, commit-details or recovery panel. Mouse navigation to another tab dismisses transient drafts rather than applying them.

Opening an already-known normalized root updates its existing tab instead of adding a duplicate. This explicit open performs a fresh inspection; selecting a tab only restores memory. Roots are compared case-insensitively on Windows and ordinally elsewhere, using the validated provider root after full-path and trailing-separator normalization. The desktop does not claim to resolve every filesystem alias or symlink to a unique inode. Linked Git worktrees have distinct working-tree roots and therefore distinct tabs; discovering and grouping related worktrees is still planned.

## State and architecture

One code-view control tree renders the active tab. Typed presentation snapshots retain domain inspection results and view state, rather than keeping eight full editor trees. There is no background polling or hidden reinspection on tab switching. The existing 200,000-character displayed-diff limit applies to each retained text comparison; the Git adapter's timeout/output limits remain unchanged. The tab limit bounds retained sessions but is not a measured process-memory budget.

Application and domain ports remain independent of Avalonia and tabs. No package, network service, account, repository mutation or automatic AI/analyzer execution is introduced. The session state exists only in memory. Existing recent shortcuts retain their small, opt-in-resume behavior; the set of open tabs and historical patches are not persisted or restored at startup.

Named local workspace groups, workspace editing, related-worktree discovery, drag-to-reorder tabs, a commit graph and analyzer panels are not implemented by this change. The tab bar is horizontally scrollable without a permanent scrollbar; keyboard tab cycling brings the selected item into view. A future overflow list must remain subject to compact-layout and keyboard checks.

## Verification

Headless tests cover state restoration without Git reads, cancelled and uncooperative late results, opening invalidation, closing active/inactive/final tabs, duplicate roots, the eight-tab bound, pinned history context, keyboard navigation and returning from Home through the selected tab. Existing theme/layout tests keep the 80% body-height, 60% body-area and compact code-height requirements. A native Windows test exercises the real accessible tab, Home return and final close on the isolated hosted VM. Native evidence and remaining assistive-technology acceptance are separate from headless results.
