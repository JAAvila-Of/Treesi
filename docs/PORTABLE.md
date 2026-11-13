# Experimental portable builds

Treesi is licensed under **MIT OR Apache-2.0**, at your option. Third-party components retain their own licenses in `third-party/`.

Portable candidates include the .NET runtime and both Desktop and JSON CLI. A .NET SDK or separately installed runtime is unnecessary. Install Git separately and make it available on `PATH`. Treesi does not bundle Git.

Extract the entire archive into its own directory; keep all files together. On Windows, open `Treesi.Desktop.exe`. On Linux and macOS, launch `./Treesi.Desktop` from that directory. Invoke `Treesi.Cli.exe capabilities` on Windows or `./Treesi.Cli capabilities` on Unix for machine-readable capabilities. Linux requires the native desktop libraries used by Avalonia; this archive is not a standalone operating system image.

These are **unsigned experimental candidates**, not signed installers. macOS builds are not notarized and do not yet provide a Finder application bundle. Platform security policies may prevent opening them. Do not disable operating system protection to test a candidate. Signing, notarization, installer integration and native macOS/Linux acceptance remain release work.

## Verify a download

Compare the archive SHA-256 against its `.sha256` sidecar before extraction (`Get-FileHash -Algorithm SHA256` on PowerShell, `sha256sum` on Linux, or `shasum -a 256` on macOS). `package-manifest.json` records the exact source commit, version, runtime identifier, selected files, lengths and SHA-256 values; the manifest excludes itself. Checksums establish byte integrity, not publisher authenticity.

## Reproduce a candidate

Use the pinned SDK and PowerShell 7 on the matching native architecture:

```powershell
./tools/Build-Portable.ps1 -Runtime win-x64 -WorkDirectory /absolute/new/build-directory
```

Supported targets are `win-x64`, `linux-x64`, `osx-arm64` and `osx-x64`. Each build needs a clean committed source tree and a new work directory. The script verifies the reviewed solution lock files, creates separate runtime-specific locks, refuses dependency version/hash drift, repeats the runtime restore in locked mode, and publishes without trimming or single-file rewriting. Desktop and CLI share identical runtime files; conflicting duplicate files stop packaging. Only publish outputs, selected legal files and generated metadata enter the archive. Symbols, repository files and operational configuration are excluded.

Original NuGet license/notice bytes and nuspec metadata are retained for the conservative union of resolved product dependencies, including build-only and other-platform packages. Packages missing legal files use the reviewed, commit-pinned originals in `tools/Treesi.Packaging/licenses/`; source URLs and hashes are recorded there. The SDK-selected runtime pack has separate original notices and a recorded archive hash, outside the application NuGet lock graph. Review these sources whenever dependency or SDK versions change.

The manually triggered **Portable candidates** workflow builds on matching GitHub-hosted operating systems, extracts each archive, runs the self-contained CLI without a configured external runtime, and uploads archives, checksums and size reports. It does not run a native desktop on the developer machine or publish a GitHub Release.

The **Native Windows UI** workflow also builds and extracts the portable archive, then tests that extracted application in its guarded disposable interactive VM. macOS and Linux native desktop acceptance remains separate from the CLI packaging smoke tests.
