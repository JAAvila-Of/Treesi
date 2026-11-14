# Application icon

`treesi.svg` is the approved full-detail vector source. It preserves nine open rings, the rounded canopy, reflection arc and dash, isolated inner strokes, gaps and central crossings. The same geometry appears in `treesi-app-icon.svg`, centered on a dark tile for desktop use. No lettering, external font, embedded bitmap, script or network resource is required.

`treesi.png` is the embedded 256px Avalonia window/workspace icon and the Linux menu PNG. `treesi.ico` contains 16–256px variants for the Windows executable, installer and uninstaller. `treesi.icns` contains PNG members through 1024px and is copied to the macOS bundle's `Contents/Resources`, referenced by `CFBundleIconFile`. These resources all retain the full approved mark; they do not select a reduced three-node design or a new wordmark composition.

Regenerate committed files with `python tools/generate-icons.py`. The optional authoring script uses CairoSVG and Pillow (validated with CairoSVG 2.9.0 and Pillow 11.3.0); neither is a build or product runtime dependency. Normal builds consume the committed assets without Python, network access or image conversion. The script makes no network requests; keep the original SVG self-contained.

The portable workflow decodes the ICNS with Apple's native `iconutil` before accepting either macOS archive. Native lab launch tests cover window startup. These checks do not substitute for human visual acceptance of Explorer, Finder, Dock, scaling or accessibility. Linux registration references the package's `treesi-icon.png` by absolute path and verifies its inventory hash before creating the descriptor. No icon-theme files are installed. See [Linux desktop integration](../../docs/LINUX_DESKTOP.md) for descriptor compatibility and removal.
