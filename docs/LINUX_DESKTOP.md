# Optional Linux application menu entry

The Linux x64 portable candidate includes `Treesi.Integration`, a small self-contained console helper that shares the existing .NET runtime. It requires neither a .NET SDK nor PowerShell. Git and the native desktop libraries needed by Treesi remain separate requirements.

Verify the [archive provenance and checksums](PORTABLE.md) before extraction. Place the complete extracted folder at the location where you intend to keep it, then run as your normal desktop user:

```sh
./Treesi.Integration register
./Treesi.Integration status
./Treesi.Integration unregister
```

`register` adds **Treesi Preview** to the user application menu by creating `io.github.jaavila-of.treesi.preview.desktop` under `$XDG_DATA_HOME/applications`, or `$HOME/.local/share/applications` by default. Relative XDG data paths are ignored according to the [XDG base-directory specification](https://specifications.freedesktop.org/basedir/latest/). The desktop environment controls when its menu refreshes. No application is launched by these commands.

Keep the complete portable folder together. The menu entry points to its absolute Desktop executable. Unregister before relocating or replacing the folder, then register again at the new location. If you have already moved it, the helper at the new location can still unregister the old entry using its embedded identity. Only one preview entry is registered at a time; a different candidate must be unregistered first.

Registration is optional. The helper does not copy binaries, change `PATH`, install system packages, request elevation, add file/URL associations, change Git configuration or create a background process. Running it as root is refused. The main Git CLI remains a separate inspection interface.

## Ownership and failure behavior

Registration uses a new-file operation; existing foreign or modified entries are retained. Repeating registration for the exact same package location and commit is idempotent. `status` creates no directories or files. Linked data/application directories or desktop-entry files are refused. Installation paths containing control characters or `=` are rejected; the latter is prohibited in executable paths by the [desktop-entry Exec specification](https://specifications.freedesktop.org/desktop-entry/latest/exec-variables.html). Arguments use the specification's quoting and field-code escaping, without invoking a shell.

The entry keeps its source commit and UTF-8 installation path in versioned `X-Treesi-*` metadata. The path is Base64-encoded to preserve literal characters; this is not encryption and the executable path is already present in `Exec`. Unregister accepts only the exact bytes of a descriptor reconstructed from that metadata. Its only deletion target is the fixed user menu entry: package files, preferences, other menu entries and repositories are retained. A customized or oversized entry requires explicit manual review/removal. No saved receipt or recursive cleanup is used.

Checks are intended to prevent accidental overwrite/removal, not to sandbox a hostile process running as the same user or guarantee a transaction across concurrent filesystem changes. A manifest check is not a publisher signature: build provenance must be verified before running the helper. Windows/macOS menu or installer behavior is unchanged.

## Automation and acceptance

Every helper invocation returns one JSON envelope with `schemaVersion`, `ok`, `data` and `error`, and a nonzero exit code on failure. Status distinguishes `absent`, `registered` and `modified`; mutations fail closed on a conflict. Scripts and AI tools should check both the exit code and `ok`, and must not treat a conflict as permission to delete the entry.

The portable workflow exercises an extracted Linux package in a temporary XDG profile, including paths with spaces, idempotency, retained customizations, linked-entry refusal and unregistering after moving the application. `desktop-file-validate` checks the generated descriptor and GLib parses its name/executable without launching it. Those validation tools are CI-only dependencies. Actual GNOME/KDE menu discovery, launch, window grouping, icon polish, accessibility and native desktop interaction still require isolated platform acceptance.

The separate [native Linux launch workflow](LINUX_NATIVE_LAB.md) additionally opens the real product through its registered descriptor on an owned Xvfb/Openbox display. Its Home/window evidence is scoped separately from actual GNOME/KDE menus and Wayland.
