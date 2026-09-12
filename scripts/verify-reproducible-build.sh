#!/usr/bin/env bash
# Proves the claim the release makes: the same commit compiles to the same bytes, from any directory.
#
# Reproducibility is what makes "verify the SHA256" mean anything. Without it, a user checking a
# checksum is comparing the download against a manifest published by the same release an attacker
# would have had to compromise. With it, anyone can rebuild from the tag and prove the binary IS the
# source.
#
# What this does: clones the repository at the current commit into a temp directory with a DIFFERENT
# absolute path, builds both, and compares the three assemblies that actually run. It does not compare
# the self-contained publish output, because that also bundles the .NET runtime, which comes from
# NuGet and is identical by construction.
#
#   ./scripts/verify-reproducible-build.sh
#
# Exit code 0 means reproducible. Anything else names the assembly that differed.
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$(pwd)"
COMMIT="$(git rev-parse HEAD)"

if ! git diff --quiet || ! git diff --cached --quiet; then
  echo "::warning::working tree is dirty — the clone will build the COMMIT, not what is on disk"
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

CLONE="$WORK/clone"
OUT_A="$WORK/here"
OUT_B="$WORK/there"

echo "commit:    $COMMIT"
echo "path A:    $ROOT"
echo "path B:    $CLONE"
echo

git clone --quiet --no-hardlinks "$ROOT" "$CLONE"
git -C "$CLONE" checkout --quiet "$COMMIT"

DOTNET="${DOTNET:-dotnet}"
build() {
  # -c Release matters: symbols are dropped only in Release, and the absolute path of a .pdb is the
  # one thing that makes an otherwise deterministic build differ between directories.
  ( cd "$1/desktop" && "$DOTNET" build src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj \
      -c Release -o "$2" -v quiet --nologo >/dev/null )
}

echo "building in place…"
build "$ROOT" "$OUT_A"
echo "building the clone…"
build "$CLONE" "$OUT_B"
echo

fail=0
for dll in Umbrella.Wallet.Core Umbrella.Wallet.Infrastructure Umbrella.Wallet.App; do
  a="$(sha256sum "$OUT_A/$dll.dll" | cut -d' ' -f1)"
  b="$(sha256sum "$OUT_B/$dll.dll" | cut -d' ' -f1)"
  if [ "$a" = "$b" ]; then
    printf '  ok    %-34s %s\n' "$dll" "${a:0:16}…"
  else
    printf '  DIFF  %-34s %s vs %s\n' "$dll" "${a:0:16}…" "${b:0:16}…"
    fail=1
  fi
done

echo
if [ "$fail" -ne 0 ]; then
  echo "NOT reproducible. Something in the build depends on where it ran."
  echo "The usual culprit is a path embedded in the assembly — check DebugType and PathMap in"
  echo "desktop/Directory.Build.props, and see docs/building.md for how this was diagnosed before."
  exit 1
fi

echo "Reproducible: the same commit produced identical assemblies from two different paths."
