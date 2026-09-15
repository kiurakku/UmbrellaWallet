# Check relative markdown links in the repo (Windows-friendly).
# Usage (repo root): pwsh scripts/check-doc-links.ps1
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$files = Get-ChildItem -Recurse -Filter *.md |
  Where-Object {
    $_.FullName -notmatch '\\.git\\' -and
    $_.FullName -notmatch '\\docs\\archive\\' -and
    $_.FullName -notmatch '\\node_modules\\' -and
    $_.FullName -notmatch '\\bin\\' -and
    $_.FullName -notmatch '\\obj\\'
  }

$broken = 0
$checked = 0
$rx = '\[[^\]]*\]\(([^)]+)\)'

foreach ($file in $files) {
  $text = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
  if (-not $text) { continue }
  foreach ($m in [regex]::Matches($text, $rx)) {
    $link = $m.Groups[1].Value.Trim()
    if ($link -match '^(https?://|mailto:|#)') { continue }
    # strip title
    if ($link -match '^([^ ]+)') { $link = $Matches[1] }
    $path = ($link -split '#')[0]
    if ([string]::IsNullOrWhiteSpace($path)) { continue }
    $checked++
    $target = if ([System.IO.Path]::IsPathRooted($path)) {
      Join-Path $root.Path $path.TrimStart('/', '\')
    } else {
      Join-Path $file.DirectoryName $path
    }
    try {
      $full = [System.IO.Path]::GetFullPath($target)
    } catch {
      Write-Host "BROKEN  $($file.FullName.Substring($root.Path.Length + 1))  ->  $link"
      $broken++
      continue
    }
    if (-not (Test-Path -LiteralPath $full)) {
      $rel = $file.FullName.Substring($root.Path.Length + 1)
      Write-Host "BROKEN  $rel  ->  $link"
      $broken++
    }
  }
}

Write-Host "Checked ~$checked relative link refs across $($files.Count) markdown files."
if ($broken -gt 0) {
  Write-Host "Found $broken broken link(s)."
  exit 1
}
Write-Host "OK — no broken relative markdown links found."
