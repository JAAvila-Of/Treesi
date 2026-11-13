# Dependency versions and licensing

Package versions are centralized in `Directory.Packages.props`. The SDK version is pinned in `global.json`, and lock files record resolved dependencies.

| Dependency | Version | License metadata | Use |
| --- | --- | --- | --- |
| .NET SDK and runtime | 11.0.100 SDK | MIT and bundled third-party notices | Build and runtime |
| Avalonia, Desktop, Fluent theme | 12.1.6 | MIT | Desktop UI |
| Avalonia.AvaloniaEdit | 12.0.0 | MIT | Virtualized read-only diff viewer |
| Avalonia.Headless.NUnit | 12.1.6 | MIT | In-memory UI verification without native windows |
| JAAvila.FluentOperations | 1.5.2 | Apache-2.0 | Test assertions |
| JAAvila.FluentOperations.Architecture | 1.5.0 | Apache-2.0 | Architecture scanning |
| JAAvila.FluentOperations.Analyzers | 1.5.0 | Apache-2.0 | Test analyzer checks |
| NUnit | 5.0.0 | MIT | Test framework |
| NUnit3TestAdapter | 6.3.1 | MIT | Test discovery and execution |
| Appium.WebDriver | 9.0.0 | Apache-2.0 | Isolated native UI tests only |
| BenchmarkDotNet | 0.15.8 | MIT | Isolated benchmarks only |
| PresentMon standalone console | 2.6.0, SHA-256 pinned in the tool manifest | MIT with upstream third-party notices retained | Isolated Windows presentation probe only; no service or product dependency |
| dotnet-trace | 10.0.750501, NuGet SHA-512 pinned in the tool manifest | MIT with packaged third-party notices retained | Managed stack sampling after resource measurements in the isolated VM; no global install or product dependency |
| Microsoft.Diagnostics.Tracing.TraceEvent | 4.0.0 | MIT | Isolated native provider probe, target-only stack export and public symbol lookup; not a product dependency |
| Microsoft.NET.Test.Sdk | 18.12.0 | MIT | Test host |
| NuGet.Packaging | 8.0.0 | Apache-2.0 | Build-time package content-hash and signed-archive integrity checks only |
| System.Security.Cryptography.Pkcs | 11.0.0 | MIT | Packaging-only cryptography override; avoids the older vulnerable transitive version |
| actions/attest | 4.2.2, commit pinned in the portable workflow | MIT | Hosted build provenance only; no product dependency |
| Git | Supplied by the user | GPL-2.0 and upstream notices | Separate CLI process |

The NuGet license metadata of the resolved graph was inspected. Managed dependencies declare MIT or Apache-2.0. ANGLE native assets supply a BSD-style license file that permits redistribution subject to preserving its notices. Native binaries can carry additional third-party notices; package metadata is not a substitute for collecting those notices for a distributed installer.

The selected dependencies do not require commercial controls or a hosted service. `NuGet.config` uses nuget.org and a repository-local `.packages` cache for reproducible restores.

## Update policy

Prefer the latest stable direct dependencies and SDK, and run the functional and architecture suite after changes. Lock files record the complete resolved graph. Native transitive versions selected by Avalonia must remain compatible with its rendering backend; do not independently replace SkiaSharp or HarfBuzz with a new major version without validating binary compatibility, rendering, and accessibility on all supported platforms.

Review transitive updates for compatibility with the framework that uses them. An available major version is not evidence that a framework supports it.

```sh
dotnet list Treesi.slnx package --outdated --include-transitive
dotnet list Treesi.slnx package --vulnerable --include-transitive
```

Use lock-file updates as reviewable changes, retain copyright/license/NOTICE material when distributing, and recheck the graph after each dependency update. Treesi's own code is dual-licensed under **MIT OR Apache-2.0**, at your option; see [MIT](../LICENSE-MIT) and [Apache-2.0](../LICENSE-APACHE). Third-party components retain their own licenses. The [portable build process](PORTABLE.md) collects original license and notice files separately.

Primary references: [.NET license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [Avalonia license](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md), [FluentOperations package and license](https://www.nuget.org/packages/JAAvila.FluentOperations), [Git license](https://git-scm.com/about/free-and-open-source).
