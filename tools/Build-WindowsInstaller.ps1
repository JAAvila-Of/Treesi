param(
    [Parameter(Mandatory)][string]$PortableDirectory,
    [Parameter(Mandatory)][string]$WorkDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Compile the Windows installer on Windows.' }
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
if (@(& git status --porcelain --untracked-files=normal).Count -ne 0 -or $LASTEXITCODE -ne 0) { throw 'Installer candidates require clean committed source.' }
$manifest = Get-Content (Join-Path $PortableDirectory 'package-manifest.json') -Raw | ConvertFrom-Json
$commit = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -or $manifest.sourceCommit -ne $commit) { throw 'The portable candidate does not match this source revision.' }
$work = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $work) { throw 'Installer builds require a new work directory.' }
New-Item -ItemType Directory -Path $work | Out-Null
$compiler = & (Join-Path $PSScriptRoot 'Get-Nsis.ps1') -WorkDirectory "$work/tool"
$compiler = @($compiler)[-1]
$maker = Join-Path $compiler 'makensis.exe'
$version = & $maker /VERSION
if ($LASTEXITCODE -or $version -ne 'v3.13') { throw 'Unexpected NSIS compiler version.' }
dotnet restore tools/Treesi.Packaging --locked-mode
if ($LASTEXITCODE) { throw 'Packaging tool restore failed.' }
dotnet build tools/Treesi.Packaging -c Release --no-restore
if ($LASTEXITCODE) { throw 'Packaging tool build failed.' }
dotnet tools/Treesi.Packaging/bin/Release/net11.0/Treesi.Packaging.dll windows-installer $PortableDirectory "$compiler/COPYING" eng/installers/Windows.nsi "$work/stage"
if ($LASTEXITCODE) { throw 'Installer payload validation failed.' }
Copy-Item assets/brand/treesi.ico -Destination "$work/stage/treesi.ico"
& $maker /V2 /WX /INPUTCHARSET UTF8 "$work/stage/installer.nsi"
if ($LASTEXITCODE) { throw 'NSIS compilation failed.' }
$output = New-Item -ItemType Directory "$work/output"
$installer = @(Get-ChildItem "$work/stage" -Filter '*-setup.exe')
if ($installer.Count -ne 1) { throw 'Expected one installer executable.' }
Copy-Item -LiteralPath $installer[0].FullName -Destination $output
$published = Join-Path $output.FullName $installer[0].Name
$hash = (Get-FileHash -LiteralPath $published -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $($installer[0].Name)" | Set-Content "$published.sha256"
@{sourceCommit=$commit;productVersion=$manifest.productVersion;runtimeIdentifier='win-x64';installerBytes=(Get-Item -LiteralPath $published).Length;sha256=$hash;compiler='NSIS 3.13';compression='zlib';scope='Per-user experimental installer; no OS publisher signature. Does not update an existing installation.'} |
    ConvertTo-Json | Set-Content "$published.report.json"
Copy-Item -LiteralPath "$compiler/COPYING" -Destination "$output/NSIS-COPYING.txt"
Write-Output $published
