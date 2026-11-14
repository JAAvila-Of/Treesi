# Experimental portable builds

Treesi is licensed under **MIT OR Apache-2.0**, at your option. Third-party components retain their own licenses in `third-party/` on Windows/Linux, or `Treesi.app/Contents/Resources/third-party/` on macOS.

Portable candidates include the .NET runtime and both Desktop and JSON CLI. A .NET SDK or separately installed runtime is unnecessary. Install Git separately and make it available on `PATH`. Treesi does not bundle Git.

Extract the entire archive into its own directory; keep all files together. On Windows, open `Treesi.Desktop.exe`. On Linux, launch `./Treesi.Desktop` from that directory. macOS provides a `Treesi.app` bundle that can be moved as one unit; Desktop, CLI, shared runtime, legal notices and build identity travel together. Its CLI is `./Treesi.app/Contents/MacOS/Treesi.Cli capabilities`; Windows uses `Treesi.Cli.exe capabilities`, Linux uses `./Treesi.Cli capabilities`. The package manifest also records the executable and legal paths in `entryPoints`. Linux requires the native desktop libraries used by Avalonia; this archive is not a standalone operating system image.

These are **experimental candidates without an OS publisher signature**, not signed installers. macOS builds are not notarized; SDK-generated apphosts can carry ad-hoc signatures, which do not authenticate Treesi as a publisher. Platform security policies may prevent opening them. Do not disable operating system protection to test a candidate. The `.app` layout and relocated CLI are verified headlessly; Finder launch, app icon polish, signing, notarization, installer integration and native macOS/Linux desktop acceptance remain release work.

## Verify a download

Compare the archive SHA-256 against its `.sha256` sidecar before extraction (`Get-FileHash -Algorithm SHA256` on PowerShell, `sha256sum` on Linux, or `shasum -a 256` on macOS). `package-manifest.json` records the exact source commit, version, runtime identifier, selected files, lengths and SHA-256 values; the manifest excludes itself. Checksums establish byte integrity, not publisher authenticity.

Official candidates additionally include a `.sigstore.json` build-attestation bundle and the CI verification result. Use the [GitHub CLI](https://cli.github.com/manual/gh_attestation_verify) to verify the archive before extracting or executing it:

```sh
gh attestation verify Treesi-<version>-<rid>-<commit>.zip --bundle Treesi-<version>-<rid>-<commit>.zip.sigstore.json --repo JAAvila-Of/Treesi --signer-workflow JAAvila-Of/Treesi/.github/workflows/portable.yml --source-ref refs/heads/main --source-digest <expected-full-source-commit>
```

Use `.tar.gz` instead of `.zip` for Linux. Obtain the expected full commit from a source revision you intend to test; reading it only from the downloaded manifest does not independently establish which version you wanted. CI enforces the same repository, workflow, ref and source-commit constraints against the attestation before uploading the tested archive. Build provenance binds bytes to the expected GitHub builder/source; it is not an OS signature, notarization, release approval, or proof that the software has no defects. See [GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations).

## Reproduce a candidate

Use the pinned SDK and PowerShell 7 on the matching native architecture:

```powershell
./tools/Build-Portable.ps1 -Runtime win-x64 -WorkDirectory /absolute/new/build-directory
```

Supported targets are `win-x64`, `linux-x64`, `osx-arm64` and `osx-x64`. Each build needs a clean committed source tree and a new work directory. The script verifies the reviewed solution lock files, creates separate runtime-specific locks, refuses dependency version/hash drift, repeats the runtime restore in locked mode, and publishes without trimming or single-file rewriting. Desktop and CLI share identical runtime files; conflicting duplicate files stop packaging. Only publish outputs, selected legal files and generated metadata enter the archive. Symbols, repository files and operational configuration are excluded.

macOS uses the [standard application bundle layout](https://developer.apple.com/library/archive/documentation/CoreFoundation/Conceptual/CFBundles/BundleTypes/BundleTypes.html) with Retina support and an explicit Desktop apphost in `Info.plist`. Numeric product display version comes from `VersionPrefix`; the full prerelease version and source commit remain in the bundle metadata and `Resources/build-info.json`. `TreesiBundleBuildVersion` in `Directory.Build.props` is a separate Apple-compatible build sequence (initially `1.0.0`); increment it for each future released bundle, without relabeling the product as stable. No URL schemes, file associations, privileged permissions or background service are registered.

Original NuGet license/notice bytes and nuspec metadata are retained for the conservative union of resolved product dependencies, including build-only and other-platform packages. Packages missing legal files use the reviewed, commit-pinned originals in `tools/Treesi.Packaging/licenses/`; source URLs and hashes are recorded there. The SDK-selected runtime pack has separate original notices and a recorded archive hash, outside the application NuGet lock graph. Review these sources whenever dependency or SDK versions change.

The manually triggered **Portable candidates** workflow builds on matching GitHub-hosted operating systems from this repository on `main`, extracts each archive, runs the self-contained CLI without a configured external runtime, and uploads archives, checksums, attestations and size reports. macOS also validates the plist, Desktop architecture and executable permission, then relocates the bundle to a path containing spaces before inspecting a Git fixture with its CLI. It does not run a native desktop on the developer machine or publish a GitHub Release.

The **Native Windows UI** workflow also builds and extracts the portable archive, then tests that extracted application in its guarded disposable interactive VM. macOS and Linux native desktop acceptance remains separate from the CLI packaging smoke tests.

See [distribution and update policy](DISTRIBUTION.md) for the current manual replacement model and requirements before introducing automatic updates.

Linux candidates also provide [optional application menu registration](LINUX_DESKTOP.md) through the self-contained `Treesi.Integration` helper. It registers only the current user entry, without launching the desktop or installing a service.
