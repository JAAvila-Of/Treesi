# Isolated Linux native launch acceptance

The **Native Linux launch** workflow builds and extracts the actual Linux self-contained portable and registers its descriptor through `Treesi.Integration`. A separate `LinuxNative` NUnit/FluentOperations test then asks GIO to launch that registered descriptor. No product test flag or alternate application entry point is used.

## Admission and ownership

The workflow runs only for this public repository on `main`, in a disposable GitHub-hosted Ubuntu VM. Its `linux-native-ui` opt-in is distinct from Windows UI/performance and benchmarks. A developer desktop, self-hosted runner, wrong OS/mode or ordinary/local-forwarded display is refused. The test repeats admission before launching a process or running a display tool; the regular solution only tests the pure admission table and never starts native UI.

The session creates its own Xvfb display with TCP disabled and a private Xauthority cookie, plus an Openbox window manager and temporary XDG data/config/cache directories. An occupied display is refused. A private capability under `RUNNER_TEMP` binds display, authority and live Xvfb PID/arguments; the test validates it before using X11. Session cleanup owns only its two direct background children. The temporary D-Bus session is scoped to the same workflow lifetime. No persistent runner, display forwarding, personal WSL/Docker session or SSH desktop is used.

The GIO launch callback records exactly one child PID with `DO_NOT_REAP_CHILD`. The test verifies the process's parent PID and `/proc/.../exe` against the extracted product before observing its X11 window. The visible window's PID must match that child, its title must identify Treesi and its geometry must meet the product minimum. It captures only that window using the owned display; no mouse or keyboard input is generated. Cleanup terminates/reaps only the verified child and removes the registration through the product helper.

## Evidence and boundaries

`native-linux-results` retains TRX, Xvfb/Openbox and product stdout/stderr, a window PNG and a JSON report with source commit, ownership, title, geometry and scope. The PNG is a diagnostic, not a selected or approved design preview. The test checks its PNG header and minimum byte size; visual inspection adds evidence but does not replace full accessibility assertions.

This covers native Home startup through the registered desktop-entry protocol on X11/Xvfb/Openbox. It does not certify GNOME/KDE menu navigation, Wayland, drag providers, assistive technology, diff correctness, physical GPU behavior, frame pacing or launch budgets. The render-settle delay and capture/observation times are not performance metrics. These boundaries remain separate acceptance work.

The lab uses freely usable OS packages for Xvfb, Openbox, X11 queries, ImageMagick and D-Bus. They are CI-only and are not added to the product runtime package. Local development may compile/format these tests; run the documented workflow for native execution. Never set CI flags locally to bypass admission.
