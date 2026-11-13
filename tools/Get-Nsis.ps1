param([Parameter(Mandatory)][string]$WorkDirectory)
$ErrorActionPreference = 'Stop'
$work = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $work) { throw 'NSIS needs a new work directory.' }
New-Item -ItemType Directory -Path $work | Out-Null
$manifest = Get-Content (Join-Path $PSScriptRoot '../eng/installer-tools/nsis.json') -Raw | ConvertFrom-Json
$archive = Join-Path $work 'nsis.zip'
$url = $manifest.url
for ($attempt = 0; $attempt -lt 3; $attempt++) {
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archive -TimeoutSec 60
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -eq $manifest.sha256) { break }
    if ((Get-Item -LiteralPath $archive).Length -gt 512KB) { throw 'NSIS digest mismatch.' }
    $page = Get-Content -LiteralPath $archive -Raw
    $redirect = [regex]::Match($page, 'content="5; url=([^"]+)"')
    if (!$redirect.Success) { throw 'NSIS download did not match its pinned digest.' }
    $uri = [uri][Net.WebUtility]::HtmlDecode($redirect.Groups[1].Value)
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'downloads.sourceforge.net' -or $uri.AbsolutePath -ne '/project/nsis/NSIS%203/3.13/nsis-3.13.zip') {
        throw 'Unexpected SourceForge download redirect.'
    }
    $url = $uri.AbsoluteUri
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.sha256 -or (Get-Item -LiteralPath $archive).Length -ne $manifest.bytes) {
    throw 'NSIS archive failed its pinned digest/size check.'
}
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $work 'compiler')
Write-Output (Join-Path $work "compiler/nsis-$($manifest.version)")
