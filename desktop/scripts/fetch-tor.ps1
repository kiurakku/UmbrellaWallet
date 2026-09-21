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
    #
    # The version is part of the URL, and the Tor Project PRUNES old releases from the mirror — a pin
    # left behind becomes a 404 rather than an old-but-working download. That is exactly what had
    # happened here: 15.0.22 no longer exists upstream, while the hash below is the one Tor's own
    # signed sums file publishes for 15.0.23.
    [string]$Version = '15.0.23',
    [string]$ExpectedSha256 = '231dad6b9cb401a54c260db7046965ef04e4f72ff071b140d423fb5da281ab1e',
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\Umbrella.Wallet.App\tor'),
    # The Tor Browser Developers signing key. The pinned hash is only worth as much as the file it
    # came from, and that file is signed — checking the signature is what makes the pin mean "what
    # the Tor Project published" rather than "what some server served me".
    [string]$SigningKeyFingerprint = 'EF6E286DDA85EA2A4BA7DE684E2C6E8793298290',
    # Fail when the signature cannot be checked (no gpg, no key, bad signature) instead of falling
    # back to the hash alone. Release builds pass this.
    [switch]$RequireSignature,
    # Escape hatch for staging an unpinned version locally; never use it for a release build.
    [switch]$AllowUnverified
)

# Shared supply-chain helpers (gpg verification, honest reporting of what was actually checked).
. (Join-Path $PSScriptRoot 'SupplyChain.ps1')

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

# Check the pin against the Tor Project's OWN signed sums file before trusting it. A hash that only
# ever agrees with itself proves nothing — it says the download matches what this script expects, not
# what Tor published. The signature is what connects the two.
$sumsUrl = "https://dist.torproject.org/torbrowser/$Version/sha256sums-unsigned-build.txt"
# The key comes from the Tor Project's own Web Key Directory first — the source their docs name
# (gpg --locate-keys torbrowser@torproject.org). keys.openpgp.org strips the key's user ID, and
# keyserver.ubuntu.com refused the release runner outright, which stopped the 4.8.0 Windows build.
# Whatever serves it, the key is only used if its fingerprint is the pinned one.
Assert-PinnedBySignedSums -SumsUrl $sumsUrl -SignatureUrl "$sumsUrl.asc" `
    -FileName $archive -ExpectedSha256 $ExpectedSha256 `
    -KeyFingerprint $SigningKeyFingerprint `
    -KeyUrls @(
        # The public key itself, kept in this repository and reviewed like code — so a release does
        # not depend on any keyserver answering. It is still used only because its fingerprint is
        # the pinned one; when Tor rotates a signing subkey, verification fails closed until this
        # file is refreshed from the source below.
        (Join-Path $PSScriptRoot "keys/torbrowser-$SigningKeyFingerprint.asc"),
        'https://openpgpkey.torproject.org/.well-known/openpgpkey/torproject.org/hu/kounek7zrdx745qydx6p59t9mqjpuhdf?l=torbrowser') `
    -WorkDir $work -Required:$RequireSignature -What 'Tor expert bundle'

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
