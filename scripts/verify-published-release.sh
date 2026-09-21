#!/usr/bin/env bash
# Verify a PUBLISHED GitHub release against its own checksum manifest (roadmap P0.3).
#
# The release workflow already self-verifies before publishing: it downloads the attached artifacts,
# checks the expected set is complete, generates SHA256SUMS and runs `sha256sum -c` over it. What it
# cannot check is what the release page serves AFTERWARDS — an asset replaced or re-uploaded later
# looks exactly like the original to anybody who trusts the page.
#
# So this downloads what the release actually serves, right now, and checks every file against the
# manifest attached to it. It is the same thing a user does by hand from SECURITY.md, which is the
# point: the instructions the project gives people are run by the project too.
#
# Usage:
#   bash scripts/verify-published-release.sh            # the latest release
#   bash scripts/verify-published-release.sh v4.8.0     # a specific tag
#
# Requires: gh (authenticated), sha256sum.
set -euo pipefail

TAG="${1:-}"
REPO="${GH_REPO:-thefear078/UmbrellaWallet}"

command -v gh > /dev/null || { echo "gh is required (https://cli.github.com)"; exit 2; }

if [ -z "$TAG" ]; then
  TAG="$(gh release view --repo "$REPO" --json tagName --jq .tagName)"
fi

echo "Verifying published release $TAG of $REPO"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

gh release download "$TAG" --repo "$REPO" --dir "$WORK" --pattern "UmbrellaWallet-*"
gh release download "$TAG" --repo "$REPO" --dir "$WORK" --pattern "SHA256SUMS-*.txt"

cd "$WORK"

manifest="$(ls SHA256SUMS-*.txt 2> /dev/null | head -1 || true)"
if [ -z "$manifest" ]; then
  echo "::error::$TAG has no SHA256SUMS manifest attached — nothing to verify against."
  echo "A release without one asks every downloader to trust the page instead of checking it."
  exit 1
fi

# v4.5.0 shipped a manifest that was zero bytes. It verified nothing, and nothing said so.
if [ ! -s "$manifest" ]; then
  echo "::error::$manifest is empty."
  exit 1
fi

echo "----- $manifest -----"
cat "$manifest"
echo "---------------------"

# Every artifact the release serves must be named in the manifest. A file that is downloadable but
# unlisted is precisely the one worth substituting.
unlisted=0
for f in UmbrellaWallet-*; do
  [ -e "$f" ] || continue
  if ! grep -Fq " $f" "$manifest" && ! grep -Fq "*$f" "$manifest"; then
    echo "::error::$f is attached to the release but missing from $manifest"
    unlisted=1
  fi
done
[ "$unlisted" -eq 0 ] || exit 1

# And every line of the manifest must match the bytes actually served.
sha256sum -c "$manifest"

echo
echo "OK — $TAG matches $manifest, byte for byte."
