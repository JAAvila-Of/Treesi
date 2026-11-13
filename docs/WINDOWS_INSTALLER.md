# Experimental Windows installer

The **Native Windows UI** workflow builds a per-user Windows x64 installer from the same validated self-contained portable candidate. The downloadable artifact is `installer-windows-x64`; it contains the `*-setup.exe`, checksum, size/source report, NSIS license, build-attestation bundle and verification result. Artifacts currently expire after one day. This is an experimental candidate without an OS publisher signature, not a stable release or an automatic updater.

## Install and remove

Verify the downloaded executable before running it. Compare its SHA-256 sidecar and verify its build provenance using the [GitHub CLI](https://cli.github.com/manual/gh_attestation_verify), with the full source commit you intended to test:

```sh
gh attestation verify Treesi-<version>-win-x64-<commit>-setup.exe --bundle Treesi-<version>-win-x64-<commit>-setup.exe.sigstore.json --repo JAAvila-Of/Treesi --signer-workflow JAAvila-Of/Treesi/.github/workflows/native-ui.yml --source-ref refs/heads/main --source-digest <expected-full-source-commit>
```

The expected signer is `native-ui.yml` for the installer and `portable.yml` for portable archives. Build provenance is separate from a Windows publisher signature. Do not bypass operating system protection to run a candidate.

Run the verified installer and follow its setup pages. It installs into `%LOCALAPPDATA%\Programs\Treesi Preview` for the current account, adds **Treesi Preview** to that account's Start menu and registers a current-user uninstall entry. It requires Windows x64. Git must already be available on `PATH`; .NET is bundled. Setup does not launch Treesi automatically, modify `PATH`, install a service, schedule a task, add file associations or request elevation. Do not run it with a different administrator account.

The destination is fixed; command-line `/D` does not retarget this preview installer. Existing installation folders, uninstall registrations or conflicting Start menu shortcuts are refused. To try a different installed candidate, close Treesi, uninstall the current one, then install the replacement. Portable builds can be extracted into separate directories instead.

Remove the candidate through Windows installed-app settings or its `Uninstall.exe`. The uninstaller checks product/build identity, refuses linked installation directories and checks that payload files are not in use before deleting its explicit owned file list. It does not recursively remove the installation tree. Unknown files and directories are retained, along with saved preferences and repositories. A retained file can leave the preview folder behind; move your retained files and remove the now-empty folder before installing another candidate. If installation is interrupted, its generated uninstaller may be used to remove the partial payload; no existing installation is overwritten.

These preview safeguards do not turn installation into a filesystem transaction or protect against another process changing files between checks. Recovery after arbitrary interruption, upgrades, repair, locked-file races, publisher signing and final installer visual polish remain release work.

## Build and acceptance

Build a portable candidate from a clean committed tree, extract it, then run:

```powershell
./tools/Build-WindowsInstaller.ps1 -PortableDirectory /absolute/extracted/candidate -WorkDirectory /absolute/new/installer-build
```

The compiler is the original NSIS 3.13 ZIP, size and SHA-256 pinned to its [official release](https://sourceforge.net/projects/nsis/files/NSIS%203/3.13/nsis-3.13.zip/download) in `eng/installer-tools/nsis.json`. It is extracted locally for headless compilation without installing NSIS globally. The generated payload refuses unlisted/modified files, unsafe Windows member names, links, unsupported identities and oversized inputs. NSIS uses zlib compression; its [core/UI and zlib licenses permit free commercial use](https://nsis.sourceforge.io/Docs/AppendixI.html), and original `COPYING` accompanies the installed payload and output artifact. Setup/uninstall acceptance runs only in the isolated Windows VM.

Native acceptance runs only in the guarded disposable hosted Windows VM. A FluentOperations lifecycle test checks the executable's `asInvoker` manifest, per-user files/registration, CLI inspection, rejection of reinstallation, locked files, mismatched identity and junctions, preservation of unknown files/preferences/repository bytes and unchanged user/machine `PATH`. The workflow then installs a fresh candidate and runs the existing native UI suite against that installed application. Only after these checks pass does it attest and publish the installer. The portable workflow remains available for all four runtime identifiers.
