param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$WorkDirectory,
    [Parameter(Mandatory)][string]$ReportPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (!$IsLinux -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'This acceptance script requires the isolated hosted Linux runner.' }
$temporary = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('/') + '/'
$candidate = [IO.Path]::GetFullPath($CandidateDirectory)
$work = [IO.Path]::GetFullPath($WorkDirectory)
if (!$candidate.StartsWith($temporary, [StringComparison]::Ordinal) -or !$work.StartsWith($temporary, [StringComparison]::Ordinal)) { throw 'Acceptance paths must be inside the owned runner temp directory.' }
if (Test-Path -LiteralPath $work) { throw 'Use a new acceptance work directory.' }
New-Item -ItemType Directory $work | Out-Null
$application = Join-Path $work 'application with spaces #; dollar$ quote"'
Move-Item -LiteralPath $candidate -Destination $application
$helper = Join-Path $application 'Treesi.Integration'
$env:DOTNET_ROOT = Join-Path $work 'no-external-runtime'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:XDG_DATA_HOME = Join-Path $work 'user data'
$env:XDG_CONFIG_HOME = Join-Path $work 'user configuration'
function Invoke-Integration([string]$Command, [bool]$ExpectSuccess = $true) {
    $text = & $helper $Command
    $code = $LASTEXITCODE
    $response = $text | ConvertFrom-Json
    if (($code -eq 0) -ne $ExpectSuccess -or $response.ok -ne $ExpectSuccess) { throw "Unexpected helper result for $Command" }
    return $response
}
if ((Invoke-Integration 'status').data.status -ne 'absent' -or (Test-Path -LiteralPath $env:XDG_DATA_HOME)) { throw 'Status changed an empty profile.' }
$applications = New-Item -ItemType Directory (Join-Path $env:XDG_DATA_HOME 'applications')
$foreign = Join-Path $applications.FullName 'other.desktop'
[IO.File]::WriteAllText($foreign, 'foreign desktop entry')
New-Item -ItemType Directory $env:XDG_CONFIG_HOME | Out-Null
$preferences = Join-Path $env:XDG_CONFIG_HOME 'preferences.keep'
[IO.File]::WriteAllText($preferences, 'saved preferences')
$icon = Join-Path $application 'treesi-icon.png'
$iconBytes = [IO.File]::ReadAllBytes($icon)
try {
    [IO.File]::WriteAllBytes($icon, [byte[]]@(1, 2, 3))
    Invoke-Integration 'register' $false | Out-Null
    if (Test-Path -LiteralPath (Join-Path $applications.FullName 'io.github.jaavila-of.treesi.preview.desktop')) { throw 'An altered icon authorized registration.' }
} finally { [IO.File]::WriteAllBytes($icon, $iconBytes) }
$percentApplication = Join-Path $work 'application %U'
Move-Item -LiteralPath $application -Destination $percentApplication
try {
    $helper = Join-Path $percentApplication 'Treesi.Integration'
    $refused = Invoke-Integration 'register' $false
    if ($refused.error.code -ne 'unsupported_location' -or (Test-Path -LiteralPath (Join-Path $applications.FullName 'io.github.jaavila-of.treesi.preview.desktop'))) { throw 'A GLib-incompatible executable location authorized registration.' }
} finally {
    Move-Item -LiteralPath $percentApplication -Destination $application
    $helper = Join-Path $application 'Treesi.Integration'
}
$registration = Invoke-Integration 'register'
if ($registration.data.status -ne 'registered' -or $registration.data.installation -ne $application) { throw 'Registration identity mismatch.' }
$entry = $registration.data.entryPath
$original = [IO.File]::ReadAllBytes($entry)
$timestamp = [IO.File]::GetLastWriteTimeUtc($entry)
Invoke-Integration 'register' | Out-Null
if ([IO.File]::GetLastWriteTimeUtc($entry) -ne $timestamp) { throw 'Idempotent registration rewrote the entry.' }
& desktop-file-validate $entry
if ($LASTEXITCODE) { throw 'The desktop entry failed freedesktop validation.' }
& /usr/bin/python3 (Join-Path $PSScriptRoot 'check-linux-desktop.py') $entry (Join-Path $application 'Treesi.Desktop') (Join-Path $application 'treesi-icon.png')
if ($LASTEXITCODE) { throw 'GLib desktop entry parsing failed.' }
[IO.File]::AppendAllText($entry, "X-User-Note=retain`n")
$modified = [IO.File]::ReadAllText($entry)
$refused = Invoke-Integration 'unregister' $false
if ($refused.error.code -ne 'entry_conflict' -or [IO.File]::ReadAllText($entry) -ne $modified) { throw 'A modified entry was not retained.' }
[IO.File]::WriteAllBytes($entry, $original)
$relocated = Join-Path $work 'relocated application'
Move-Item -LiteralPath $application -Destination $relocated
$helper = Join-Path $relocated 'Treesi.Integration'
if ((Invoke-Integration 'unregister').data.status -ne 'absent' -or (Test-Path -LiteralPath $entry)) { throw 'Unregister after moving the application failed.' }
[IO.File]::CreateSymbolicLink($entry, $foreign) | Out-Null
$refused = Invoke-Integration 'unregister' $false
if ($refused.error.code -ne 'linked_path') { throw 'A linked desktop entry was accepted.' }
Remove-Item -LiteralPath $entry
if ([IO.File]::ReadAllText($foreign) -ne 'foreign desktop entry' -or [IO.File]::ReadAllText($preferences) -ne 'saved preferences') { throw 'Foreign files or preferences were changed.' }
if ((Invoke-Integration 'unregister').data.status -ne 'absent') { throw 'Repeated removal failed.' }
@{schemaVersion='1';entryValidated=$true;glibParsed=$true;iconPathParsed=$true;alteredIconRefused=$true;percentLocationRefused=$true;modifiedEntryRetained=$true;linkedEntryRefused=$true;unregisterAfterMove=$true;foreignFilesAndPreferencesRetained=$true;externalRuntimeConfigured=$false;desktopLaunched=$false} |
    ConvertTo-Json | Set-Content -LiteralPath $ReportPath
