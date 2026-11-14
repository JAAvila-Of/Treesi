param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64')][string]$Runtime,
    [Parameter(Mandatory)][string]$WorkDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($args[0])" }
}
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
if (@(& git status --porcelain --untracked-files=normal).Count -ne 0 -or $LASTEXITCODE -ne 0) {
    throw 'Portable candidates require a clean committed source tree.'
}
$work = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $work) { throw 'Use a new empty work directory for each package build.' }
New-Item -ItemType Directory -Path $work | Out-Null
$commit = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'A Git source commit is required.' }
[xml]$properties = Get-Content Directory.Build.props -Raw
$version = "$($properties.Project.PropertyGroup.VersionPrefix)-$($properties.Project.PropertyGroup.VersionSuffix)"
$bundleBuildVersion = $properties.Project.PropertyGroup.TreesiBundleBuildVersion
$locks = Join-Path $work 'locks'
$property = "-p:TreesiPackageLockDirectory=$locks"
Invoke-Dotnet @('restore', 'Treesi.slnx', '--locked-mode')
Invoke-Dotnet @('build', 'tools/Treesi.Packaging', '-c', 'Release', '--no-restore')
$tool = Join-Path $repo 'tools/Treesi.Packaging/bin/Release/net11.0/Treesi.Packaging.dll'
foreach ($app in @('Desktop', 'Cli')) {
    Invoke-Dotnet @('restore', "src/Treesi.$app", '-r', $Runtime, '--force-evaluate', $property)
}
Invoke-Dotnet @($tool, 'verify-graph', 'src/Treesi.Desktop/packages.lock.json', 'src/Treesi.Cli/packages.lock.json', "$locks/Treesi.Desktop.lock.json", "$locks/Treesi.Cli.lock.json")
foreach ($app in @('Desktop', 'Cli')) {
    Invoke-Dotnet @('restore', "src/Treesi.$app", '-r', $Runtime, '--locked-mode', $property)
    Invoke-Dotnet @('publish', "src/Treesi.$app", '-r', $Runtime, '-c', 'Release', '--self-contained', 'true', '--no-restore', $property,
        '-p:ContinuousIntegrationBuild=true', '-p:PublishTrimmed=false', '-p:PublishSingleFile=false', '-o', "$work/$app")
}
$legal = New-Item -ItemType Directory -Path "$work/legal"
if ($Runtime -eq 'linux-x64') {
    Invoke-Dotnet @('restore', 'tools/Treesi.Integration', '-r', $Runtime, '--force-evaluate', $property)
    Invoke-Dotnet @($tool, 'verify-graph', 'tools/Treesi.Integration/packages.lock.json', 'tools/Treesi.Integration/packages.lock.json', "$locks/Treesi.Integration.lock.json", "$locks/Treesi.Integration.lock.json")
    Invoke-Dotnet @('restore', 'tools/Treesi.Integration', '-r', $Runtime, '--locked-mode', $property)
    Invoke-Dotnet @('publish', 'tools/Treesi.Integration', '-r', $Runtime, '-c', 'Release', '--self-contained', 'true', '--no-restore', $property,
        '-p:ContinuousIntegrationBuild=true', '-p:PublishTrimmed=false', '-p:PublishSingleFile=false', '-o', "$work/Integration")
    Invoke-Dotnet @($tool, 'merge', "$work/Integration", "$work/Desktop")
}
Copy-Item LICENSE-MIT, LICENSE-APACHE -Destination $legal
if ($Runtime.StartsWith('osx-')) { Copy-Item assets/brand/treesi.icns -Destination "$legal/treesi.icns" }
Copy-Item docs/PORTABLE.md -Destination "$legal/README.txt"
if ($Runtime -eq 'linux-x64') { Copy-Item docs/LINUX_DESKTOP.md -Destination "$legal/LINUX-DESKTOP.txt" }
Invoke-Dotnet @($tool, 'notices', "$repo/.packages", "$PSScriptRoot/Treesi.Packaging/licenses", "$legal/third-party", "$locks/Treesi.Desktop.lock.json", "$locks/Treesi.Cli.lock.json")
$runtimePackage = Join-Path $repo ".packages/microsoft.netcore.app.runtime.$Runtime/11.0.0"
if (!(Test-Path -LiteralPath $runtimePackage)) { throw 'The pinned .NET 11.0.0 runtime package is missing.' }
$runtimeNotices = "$legal/third-party/dotnet-runtime"
Invoke-Dotnet @($tool, 'runtime-notices', "$repo/.packages", "$PSScriptRoot/Treesi.Packaging/licenses", $runtimeNotices, $Runtime)
$runtimeArchive = Join-Path $runtimePackage "microsoft.netcore.app.runtime.$Runtime.11.0.0.nupkg"
@{id="Microsoft.NETCore.App.Runtime.$Runtime";version='11.0.0';sha512=(Get-FileHash -LiteralPath $runtimeArchive -Algorithm SHA512).Hash.ToLowerInvariant();scope='SDK-selected runtime pack; separate from the application NuGet lock graph'} |
    ConvertTo-Json | Set-Content "$runtimeNotices/package.json"
Copy-Item $locks -Destination "$legal/build-locks" -Recurse
$name = "Treesi-$version-$Runtime-$($commit.Substring(0, 8))"
$extension = if ($Runtime -eq 'linux-x64') { '.tar.gz' } else { '.zip' }
$output = New-Item -ItemType Directory -Path "$work/output"
Invoke-Dotnet @($tool, 'assemble', "$work/Desktop", "$work/Cli", "$work/legal", "$work/$name", "$output/$name$extension", $commit, $Runtime, $version, $bundleBuildVersion)
Write-Output "Portable candidate: $output/$name$extension"
