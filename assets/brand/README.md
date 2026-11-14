# Application icon

`treesi.svg` is the original vector source, based on the branch symbol already used in the workspace picker. The graphite tile keeps the pale mint branch visible on both light and dark desktop surfaces. The open nodes represent revisions and the curved connection represents a branch. No third-party artwork, fonts or network assets are included. These original assets use the repository's MIT OR Apache-2.0 licensing.

`treesi.png` is the embedded 256px Avalonia window icon. `treesi.ico` contains 16–256px variants for the Windows executable, installer and uninstaller. `treesi.icns` contains PNG members through 1024px and is copied to the macOS bundle's `Contents/Resources`, referenced by `CFBundleIconFile`.

Regenerate committed files with `python tools/generate-icons.py`. The optional authoring script uses CairoSVG and Pillow (validated with CairoSVG 2.9.0 and Pillow 11.3.0); neither is a build or product runtime dependency. Normal builds consume the committed assets without Python, network access or image conversion. The script makes no network requests; keep the original SVG self-contained.

The portable workflow decodes the ICNS with Apple's native `iconutil` before accepting either macOS archive. Native lab launch tests cover window startup. These checks do not substitute for human visual acceptance of Explorer, Finder, Dock, scaling or accessibility. Linux desktop registration still uses its documented generic menu icon; this change assigns the native window icon but does not install icon-theme files or change descriptor ownership.
