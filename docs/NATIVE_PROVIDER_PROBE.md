# Native CPU provider capability probe

The manual `native-provider-probe.yml` workflow tests a native CPU sampling capability before it is used on Treesi. It runs only in an explicitly admitted interactive GitHub-hosted Windows VM. It opens no UI and is not part of the product. Never run it on a developer desktop or configured benchmark host.

Two owned console children run identical bounded CPU loads. A unique ETW system-logger session starts with **no legacy global keywords**, then enables the [System Profile Provider](https://learn.microsoft.com/en-us/windows/win32/etw/system-providers) with a source PID filter for only one child and stack capture requested. It never attaches to or restarts another session, changes the system-wide sampling interval, captures heaps or suspends/injects into a process. It temporarily enables an already-assigned profiling privilege in its own token and restores the previous token state; there is no elevation request, account-policy or privilege-assignment change.

The [TraceEvent](https://github.com/microsoft/perfview) dependency is centrally pinned to stable 4.0.0 with a lock file, is MIT-licensed, and belongs only to this isolated diagnostic executable. It adds no product dependency or installed service.

Acceptance requires all of the following:

- At least 4.5 seconds of recording and 500 ms of measured CPU from **each** synthetic child, including the excluded negative control.
- Target CPU samples, zero samples attributed to another/unknown PID, and zero lost events.
- At least one target stack containing two or more frames and a frame inside an owned target module. An instruction pointer alone is insufficient.

Unsupported providers, rejected/ineffective filters, missing stacks, dropped data and inactive controls produce explicit statuses and a nonzero exit code. A failed probe does not prove that native sampling is generally unavailable. Raw ETL/ETLX files are retained only after target-only stack evidence passes; otherwise the probe deletes its own unvalidated capture files. JSON keeps counts, source SHA, unique session identity and the failure/status, without retaining foreign sample payloads. Files above 64 MiB are rejected after collection. Collection lasts five seconds; children self-exit within 30 seconds and are closed by their owner.

This probe establishes capability, not the cause of Treesi's focused-input CPU, presentation timing or native performance acceptance. Resource baselines remain separate. The policy is verified with regular FluentOperations tests; actual ETW behavior must be observed in the hosted VM.
