<#
.SYNOPSIS
    Downloads a PINNED, hash-verified Monero CLI bundle and stages monero-wallet-rpc.exe so the
    desktop wallet can run Monero in-process (real balance + real sending).

.DESCRIPTION
    The binary is ~85 MB of third-party build output and is deliberately not committed, so a fresh
    clone runs this once. The version and its SHA-256 are pinned from Monero's official signed
    hashes.txt (see THIRD_PARTY_NOTICES.md). The downloaded archive is verified against that hash
    before extraction and the build FAILS CLOSED on any mismatch — a tampered or wrong binary must
    never make it into a release.
#>
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\Umbrella.Wallet.App\monero'),
    # Pinned version + SHA-256 of monero-win-x64-v<Version>.zip, from https://www.getmonero.org/downloads/hashes.txt
    [string]$Version = '0.18.5.1',
    [string]$ExpectedSha256 = 'cf2ae8273977697d9ef2031c7337b781e6e5936578f602444b2990a173a2437d',
    # Escape hatch for staging an unpinned version locally; never use it for a release build.
    [switch]$AllowUnverified
)

$ErrorActionPreference = 'Stop'

# Idempotent: skip the ~85 MB download when the daemon is already staged, so repeat builds are
# fast and offline-friendly and don't break if the upstream URL changes.
if (Test-Path (Join-Path $Destination 'monero-wallet-rpc.exe')) {
    Write-Host "monero-wallet-rpc already staged in $Destination — skipping download."
    return
}

$file = "monero-win-x64-v$Version.zip"
$url = "https://downloads.getmonero.org/cli/$file"
$work = Join-Path ([System.IO.Path]::GetTempPath()) "umbrella-monero-$(Get-Random)"
New-Item -ItemType Directory -Force -Path $work | Out-Null
$archive = Join-Path $work $file

Write-Host "Downloading $url (~85 MB)…"
Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing

# Verify the archive against the pinned SHA-256 BEFORE trusting a single byte of it.
$expected = $ExpectedSha256.Trim().ToLowerInvariant()
$actual = (Get-FileHash -Algorithm SHA256 -Path $archive).Hash.ToLowerInvariant()
if ($expected) {
    if ($actual -ne $expected) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
        throw "Monero archive SHA-256 mismatch — refusing to use it.`n  expected $expected`n  actual   $actual`nSee THIRD_PARTY_NOTICES.md; the pinned version may have moved or the download was tampered with."
    }
    Write-Host "  sha256 verified: $actual"
}
elseif ($AllowUnverified) {
    Write-Warning "Staging Monero $Version WITHOUT hash verification (-AllowUnverified). Do NOT ship this build."
}
else {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    throw "No pinned SHA-256 for Monero $Version. Pin it in THIRD_PARTY_NOTICES.md (from the official signed hashes.txt) or pass -AllowUnverified for a throwaway local build."
}

Write-Host 'Extracting…'
Expand-Archive -Path $archive -DestinationPath $work -Force

$rpc = Get-ChildItem $work -Recurse -Filter 'monero-wallet-rpc.exe' | Select-Object -First 1
if (-not $rpc) { throw 'monero-wallet-rpc.exe was not found in the downloaded bundle.' }

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
Copy-Item $rpc.FullName (Join-Path $Destination 'monero-wallet-rpc.exe') -Force
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Verifying…'
& (Join-Path $Destination 'monero-wallet-rpc.exe') --version
Write-Host ''
Write-Host "monero-wallet-rpc (v$Version) staged in $Destination"
