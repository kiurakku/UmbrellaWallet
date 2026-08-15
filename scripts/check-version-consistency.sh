#!/usr/bin/env bash
# Fails if the release version in VERSION is not matched byte-for-byte everywhere it is declared.
# A release is blocked (roadmap §2) unless VERSION, the app csproj, the installer script, the README
# download links/badge and the CHANGELOG top entry all agree. Run in CI and before tagging a release.
set -euo pipefail

cd "$(dirname "$0")/.."

VERSION="$(tr -d ' \t\r\n' < VERSION)"
if [ -z "$VERSION" ]; then
  echo "::error::VERSION file is empty"; exit 1
fi
echo "Expected version: $VERSION"

fail=0
require() {
  # require <human label> <file> <fixed-string that must be present>
  local label="$1" file="$2" needle="$3"
  if [ ! -f "$file" ]; then
    echo "::error::$label: file not found: $file"; fail=1; return
  fi
  if ! grep -qF -- "$needle" "$file"; then
    echo "::error::$label: '$needle' not found in $file"; fail=1
  else
    echo "  ok  $label ($file)"
  fi
}

require "app csproj <Version>"      "desktop/src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj" "<Version>${VERSION}</Version>"
require "installer AppVersion"      "desktop/installer/umbrella.iss"                              "#define AppVersion \"${VERSION}\""
require "README version badge"      "README.md"                                                   "version-${VERSION}-"
require "README installer link"     "README.md"                                                   "UmbrellaWallet-Setup-${VERSION}.exe"
require "README portable link"      "README.md"                                                   "UmbrellaWallet-Portable-${VERSION}.zip"
require "README linux link"         "README.md"                                                   "UmbrellaWallet-${VERSION}-linux-x64.tar.gz"
require "CHANGELOG entry"           "CHANGELOG.md"                                                 "## [${VERSION}]"

if [ "$fail" -ne 0 ]; then
  echo "Version consistency check FAILED for $VERSION."
  exit 1
fi
echo "Version $VERSION is consistent across all release surfaces."
