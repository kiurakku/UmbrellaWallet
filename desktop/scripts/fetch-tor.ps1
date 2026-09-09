<#
.SYNOPSIS
    Downloads the official Tor Expert Bundle and stages tor.exe + GeoIP databases so the
    desktop wallet can run Tor in-process.

.DESCRIPTION
    The binaries are deliberately not committed (~35 MB of third-party build output), so a
    fresh clone runs this once. The build copies whatever lands in the tor/ folder into the
    application output, and the Inno Setup installer packages it from there.
#>
[CmdletBinding()]
param(
    # Pinned version + SHA-256 of the expert bundle, from
    # https://dist.torproject.org/torbrowser/<Version>/sha256sums-unsigned-build.txt (see THIRD_PARTY_NOTICES.md).
    [string]$Version = '15.0.22',
    [string]$ExpectedSha256 = '231dad6b9cb401a54c260db7046965ef04e4f72ff071b140d423fb5da281ab1e',
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\Umbrella.Wallet.App\tor'),
    # Escape hatch for staging an unpinned version locally; never use it for a release build.
    [switch]$AllowUnverified
)

$ErrorActionPreference = 'Stop'

# Idempotent: if the bundle is already staged, skip the download entirely. This keeps repeat
# builds offline-friendly and — importantly — means a build never breaks just because the pinned
# upstream version has been rotated off the Tor mirror (they prune old releases).
$haveAll = @('tor.exe', 'geoip', 'geoip6') |
    ForEach-Object { Test-Path (Join-Path $Destination $_) }
if ($haveAll -notcontains $false) {
    Write-Host "Tor already staged in $Destination — skipping download."
    return
}

$archive = "tor-expert-bundle-windows-x86_64-$Version.tar.gz"
$url = "https://dist.torproject.org/torbrowser/$Version/$archive"
$work = Join-Path ([System.IO.Path]::GetTempPath()) "umbrella-tor-$Version"

Write-Host "Downloading $url"
New-Item -ItemType Directory -Force -Path $work | Out-Null
$archivePath = Join-Path $work $archive
Invoke-WebRequest -Uri $url -OutFile $archivePath -UseBasicParsing

# Verify the archive against the pinned SHA-256 BEFORE extracting anything from it.
$expected = $ExpectedSha256.Trim().ToLowerInvariant()
$actual = (Get-FileHash -Algorithm SHA256 -Path $archivePath).Hash.ToLowerInvariant()
if ($expected) {
    if ($actual -ne $expected) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
        throw "Tor expert bundle SHA-256 mismatch — refusing to use it.`n  expected $expected`n  actual   $actual`nSee THIRD_PARTY_NOTICES.md; the pinned version may have moved or the download was tampered with."
    }
    Write-Host "  sha256 verified: $actual"
}
elseif ($AllowUnverified) {
    Write-Warning "Staging Tor $Version WITHOUT hash verification (-AllowUnverified). Do NOT ship this build."
}
else {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    throw "No pinned SHA-256 for Tor $Version. Pin it in THIRD_PARTY_NOTICES.md (from the official sha256sums) or pass -AllowUnverified for a throwaway local build."
}

Write-Host 'Extracting…'
# tar ships with Windows 10+ and handles .tar.gz natively.
tar -xzf $archivePath -C $work
if ($LASTEXITCODE -ne 0) { throw "Failed to extract $archivePath" }

New-Item -ItemType Directory -Force -Path $Destination | Out-Null

# Expert bundle layout: tor/tor.exe and data/geoip*
$sources = @{
    'tor.exe' = Join-Path $work 'tor\tor.exe'
    'geoip'   = Join-Path $work 'data\geoip'
    'geoip6'  = Join-Path $work 'data\geoip6'
}

foreach ($name in $sources.Keys) {
    $src = $sources[$name]
    if (-not (Test-Path $src)) { throw "Expected $src in the expert bundle but it was missing." }
    Copy-Item $src (Join-Path $Destination $name) -Force
    Write-Host "  staged $name"
}

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue

$torExe = Join-Path $Destination 'tor.exe'
Write-Host ''
Write-Host 'Verifying…'
& $torExe --version
Write-Host ''
Write-Host "Tor staged in $Destination"
