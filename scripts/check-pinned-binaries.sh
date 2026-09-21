#!/usr/bin/env bash
# The pinned third-party binaries must be described identically in the fetch scripts and in
# THIRD_PARTY_NOTICES.md (roadmap P0.2).
#
# Why this exists: the notices file is what a user reads before deciding to trust a build, and it is
# the only place the pins are stated in a form anyone checks by hand. It had already drifted — the
# fetch script pinned Tor "15.0.22", a version the Tor Project had pruned from its mirror, while the
# hash beside it was the one published for 15.0.23. A fresh clone got a 404, and anybody verifying
# the documented archive by hand was verifying a file that no longer exists.
#
# Offline and deterministic: it compares two files in this repository and nothing else.
#
# Usage (repo root): bash scripts/check-pinned-binaries.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

NOTICES="THIRD_PARTY_NOTICES.md"
fail=0

note() { printf '  %-14s %s\n' "$1" "$2"; }

problem() {
  echo "MISMATCH  $1"
  fail=1
}

# A single-quoted PowerShell parameter default: [string]$Name = 'value'
param_value() {
  sed -n "s/.*\\\$$2 = '\([^']*\)'.*/\1/p" "$1" | head -1
}

# The notices, read once, lowercased — so the checks below are plain substring tests with no
# external process involved. (grep is avoided here on purpose: it aborts on this pattern under
# git-bash, which would have made every pin look like a mismatch.)
NOTICES_TEXT="$(tr '[:upper:]' '[:lower:]' < "$NOTICES")"

contains() {
  local needle
  needle="$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')"
  [[ "$NOTICES_TEXT" == *"$needle"* ]]
}

# A value cell from a two-column table row, within ONE section of the notices. Both components have
# a "| Version | … |" row, so a document-wide match would compare Monero's pin against Tor's line
# and pass while being wrong.
notice_value() {
  awk -v section="$1" -v label="$2" '
    $0 ~ "^### " section { inside = 1; next }
    /^### / { inside = 0 }
    inside && $0 ~ "^\\| *" label " *\\|" {
      sub("^\\| *" label " *\\| *", "")
      sub(" *\\|$", "")
      print
      exit
    }
  ' "$NOTICES"
}

check() {
  local what="$1" script="$2" archive_pattern="$3" section="$4"

  local version hash fingerprint archive documented
  version="$(param_value "$script" 'Version')"
  hash="$(param_value "$script" 'ExpectedSha256')"
  fingerprint="$(param_value "$script" 'SigningKeyFingerprint')"

  echo "$what ($script)"
  note "version" "${version:-<none>}"
  note "sha256" "${hash:-<none>}"
  note "signing key" "${fingerprint:-<none>}"

  [ -n "$version" ] || problem "$what: no pinned version in $script"
  [ -n "$hash" ] || problem "$what: no pinned SHA-256 in $script"
  [ -n "$fingerprint" ] || problem "$what: no signing-key fingerprint in $script"

  # The archive the script builds must be the one the notices document, version and all.
  archive="${archive_pattern//VERSION/$version}"
  contains "$archive" || problem "$what: $NOTICES does not mention the pinned archive $archive"

  # The hash and the key have to appear there too, or the file a user reads describes a different
  # download from the one the build performs.
  contains "$hash" || problem "$what: $NOTICES does not carry the pinned SHA-256 $hash"
  contains "$fingerprint" || problem "$what: $NOTICES does not name the signing key $fingerprint"

  # And the documented version cell must agree, so a bump cannot land half-done.
  documented="$(notice_value "$section" "Version")"
  if [ -n "$documented" ] && [ "$documented" != "$version" ]; then
    problem "$what: $NOTICES documents version '$documented' but the script pins '$version'"
  fi

  echo
}

check "Tor expert bundle" "desktop/scripts/fetch-tor.ps1" \
  "tor-expert-bundle-windows-x86_64-VERSION.tar.gz" "Tor Expert Bundle"

check "Monero CLI" "desktop/scripts/fetch-monero.ps1" \
  "monero-win-x64-vVERSION.zip" "Monero CLI"

if [ "$fail" -ne 0 ]; then
  echo "Pinned binaries and $NOTICES disagree — fix both before shipping."
  exit 1
fi

echo "OK — fetch scripts and $NOTICES describe the same pinned downloads."
