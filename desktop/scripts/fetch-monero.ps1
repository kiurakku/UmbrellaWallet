<#
.SYNOPSIS
    Downloads the official Monero CLI bundle and stages monero-wallet-rpc.exe so the desktop
    wallet can run Monero in-process (real balance + real sending).

.DESCRIPTION
    The binary is ~39 MB of third-party build output and is deliberately not committed, so a
    fresh clone runs this once. The build copies whatever lands in monero/ into the application
    output, and the Inno Setup installer packages it from there.
#>
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\Umbrella.Wallet.App\monero')
)

$ErrorActionPreference = 'Stop'

# Idempotent: skip the ~85 MB download when the daemon is already staged, so repeat builds are
# fast and offline-friendly and don't break if the upstream URL changes.
if (Test-Path (Join-Path $Destination 'monero-wallet-rpc.exe')) {
    Write-Host "monero-wallet-rpc already staged in $Destination — skipping download."
    return
}

$url = 'https://downloads.getmonero.org/cli/win64'
$work = Join-Path ([System.IO.Path]::GetTempPath()) "umbrella-monero-$(Get-Random)"
New-Item -ItemType Directory -Force -Path $work | Out-Null
$archive = Join-Path $work 'monero-win64.zip'

Write-Host "Downloading $url (~85 MB)…"
Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing

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
Write-Host "monero-wallet-rpc staged in $Destination"
