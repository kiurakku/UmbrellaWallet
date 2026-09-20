<#
  release-windows.ps1 — one-shot Windows release for Umbrella Wallet (desktop).

  Produces, under $OutRoot (default D:\umbrella-dist, which the Inno Setup script expects):
    app\         folder publish that the installer bundles
    portable\    self-contained single-file Umbrella.Wallet.App.exe
    UmbrellaWallet-Setup-<version>.exe   the installer (built by ISCC)

  The version is read from the .csproj so it always matches VERSION / the .iss.
  Run from anywhere:  pwsh desktop/scripts/release-windows.ps1
  Requires: the .NET 8 SDK on PATH, and Inno Setup 6 (ISCC.exe) for the installer step.
#>
param(
  [string]$OutRoot = "D:\umbrella-dist",
  [string]$Iscc    = "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe",
  [switch]$SkipInstaller
)
$ErrorActionPreference = "Stop"

# Repo root = two levels up from this script (desktop/scripts/ -> repo/).
$repo    = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$csproj  = Join-Path $repo "desktop\src\Umbrella.Wallet.App\Umbrella.Wallet.App.csproj"
$iss     = Join-Path $repo "desktop\installer\umbrella.iss"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet not found on PATH. Restore the .NET 8 SDK first (winget install Microsoft.DotNet.SDK.8 --force)."
}

$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "Releasing Umbrella Wallet $version (win-x64)…" -ForegroundColor Cyan

# 1) Stage the bundled Tor + Monero binaries (idempotent; skip if already present).
# -RequireSignature: this builds something other people install, so the upstream sums files are
# checked against their publishers' keys rather than trusted because they downloaded cleanly.
& (Join-Path $PSScriptRoot "fetch-tor.ps1") -RequireSignature
& (Join-Path $PSScriptRoot "fetch-monero.ps1") -RequireSignature

$appDir      = Join-Path $OutRoot "app"
$portableDir = Join-Path $OutRoot "portable"
# Clean stale output from a previous version, otherwise the apphost rename below trips over an
# Umbrella.exe left behind by the last build (Rename-Item won't overwrite an existing file).
foreach ($d in @($appDir, $portableDir)) {
  if (Test-Path $d) { Remove-Item $d -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $appDir, $portableDir | Out-Null

# 2) Folder publish (what the installer packages).
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -o $appDir

# 3) Portable single-file build.
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $portableDir

# Ship the apphost as Umbrella.exe. The assembly stays Umbrella.Wallet.App (so avares:// resource URIs
# keep resolving); the apphost loads its dll by the embedded name, not its own filename, so renaming
# just the exe is safe. The installer's AppExe is set to Umbrella.exe to match.
foreach ($dir in @($appDir, $portableDir)) {
  $src = Join-Path $dir "Umbrella.Wallet.App.exe"
  if (Test-Path $src) { Rename-Item $src "Umbrella.exe" -Force }
}

Write-Host "Portable: $(Join-Path $portableDir 'Umbrella.exe')" -ForegroundColor Green

# 4) Installer (Inno Setup). The .iss reads its own AppVersion; keep it in sync with $version.
if ($SkipInstaller) {
  Write-Host "Skipping installer (--SkipInstaller)." -ForegroundColor Yellow
} elseif (-not (Test-Path $Iscc)) {
  Write-Warning "ISCC.exe not found at '$Iscc' — skipping installer. Install Inno Setup 6 or pass -Iscc <path>."
} else {
  # Pass the version the build actually produced. The .iss used to carry its own literal that had to
  # be updated by hand, and it drifted — a 4.6.0 build shipped as "UmbrellaWallet-Setup-4.5.0.exe".
  & $Iscc "/DAppVersion=$version" $iss
  $setup = Join-Path $OutRoot ("UmbrellaWallet-Setup-{0}.exe" -f $version)
  if (-not (Test-Path $setup)) {
    throw "Installer was expected at '$setup' but is not there — the .iss version and the build disagree."
  }
  Write-Host "Installer: $setup" -ForegroundColor Green
}

Write-Host "Done. Verify by launching the portable exe, then unlock and open Settings -> Developer." -ForegroundColor Cyan
