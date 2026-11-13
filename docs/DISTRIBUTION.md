# Distribution and update policy

## Current behavior

Treesi currently distributes experimental self-contained portable candidates through the manually triggered [portable workflow](../.github/workflows/portable.yml). The application does not check for updates, download executables, replace its own files, install services, request elevation or register a startup task. There is no hidden network operation or update timer. Git remains a separate installation requirement.

To try another candidate, download it separately, verify its build attestation and checksums as described in [portable builds](PORTABLE.md), close the running application, and extract it into a new directory. On macOS, keep the complete `.app` bundle together. Retain the previous directory until the replacement is accepted. Do not mix files from different packages or overwrite an application while it is running. Workspace preferences are user-local data and are not carried in archives; manual rollback does not guarantee compatibility with a future preferences schema.

An Actions artifact is a candidate, not a stable release. The workflow does not publish a release, tag a version, upload to an application store, or imply human acceptance. Its attestation proves the expected builder and source produced those archive bytes; it does not establish product quality or protect a compromised source repository.

## Requirements before implementing an updater

The initial update experience should be explicit and user-controlled: show the version, channel, supported platform, size, change notes and verification result; let the person choose when to install. A check must be cancellable, bounded in time and response size, and leave the review workspace usable. Checking must not send repository paths, code, credentials or metrics. Preview and stable channels must remain distinct, with no automatic channel switch.

Keep network, trust verification and installation behind separate application ports; CLI/AI integrations must receive the same structured results and error codes as the desktop. Treat downloaded metadata, notes and filenames as untrusted data. Never execute instructions from release notes or package contents.

Before any replacement is enabled, define an authenticated release authorization that binds version, channel, runtime identifier, size, archive digest and expected provenance identity/source commit. A checksum fetched alongside an archive is insufficient. Verification must reject an incorrect repository, workflow, ref, digest, platform or release authorization; fail closed on missing or expired metadata. Define replay/downgrade protection and key/identity rotation before relying on a remote feed. A user-requested rollback must be a separate verified operation, not a silent downgrade.

Stage a verified download outside the live installation, enforce archive path/link/size boundaries, wait for owned processes to close, and replace as one transaction with recovery after interruption. Plan preferences compatibility and rollback before migrating persistent data. Do not extract arbitrary remote archives directly into an executable directory or invoke a shell command received from a feed.

No updater or release feed is implemented by this policy. Its failure cases need automated tests before activation: tampered bytes/metadata, incorrect identity/platform/channel, expired authorization, replay/downgrade, cancellation, interrupted transfer, insufficient disk space, path traversal/link entries, an application still running, interrupted replacement and recovery.

## Platform trust and remaining acceptance

Build tooling and runtime dependencies remain freely usable. `actions/attest` is MIT-licensed and [GitHub supports artifact attestations for public repositories on current plans](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations); it is a CI tool, not a product dependency. A local script build does not receive the official workflow identity or an official attestation.

OS publisher trust is separate. macOS Developer ID signing/notarization requires an [Apple Developer Program membership](https://developer.apple.com/support/compare-memberships/), so the current candidate path does not require it. Do not claim notarization or bypass Gatekeeper. Windows publisher signing, installer design, Linux desktop integration, macOS app icon/Finder acceptance, accessibility, updates and preference migration remain release work. No certificate, paid account or store enrollment is created by these workflows.
