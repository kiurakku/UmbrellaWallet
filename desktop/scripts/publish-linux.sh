#!/usr/bin/env bash
# Publishes the Linux build of Umbrella Wallet as a self-contained tar.gz.
#
# The Tor and Monero helpers are per-OS binaries; the Windows staging scripts fetch .exe
# files, so on Linux the two helpers are fetched here from the same upstream projects.
# Everything else — wallet, signing, UI — is the same cross-platform .NET/Avalonia code.
set -euo pipefail

cd "$(dirname "$0")/.."
# Portable version extraction (avoids grep -P, which some Git Bash locales reject).
VERSION="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj)"
OUT="dist/linux/umbrella-wallet-${VERSION}-linux-x64"

echo "Publishing Umbrella Wallet ${VERSION} for linux-x64…"
dotnet publish src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj \
    -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "${OUT}"

# --- Tor (expert bundle, linux-x86_64) — best-effort; the core wallet runs without it ---
TOR_VERSION="${TOR_VERSION:-14.5.7}"
TOR_ARCHIVE="tor-expert-bundle-linux-x86_64-${TOR_VERSION}.tar.gz"
if [ ! -f "${OUT}/tor/tor" ]; then
    echo "Fetching Tor ${TOR_VERSION}…"
    if mkdir -p "${OUT}/tor" /tmp/umbrella-tor \
        && curl -fL "https://dist.torproject.org/torbrowser/${TOR_VERSION}/${TOR_ARCHIVE}" -o "/tmp/umbrella-tor/${TOR_ARCHIVE}" \
        && tar -xzf "/tmp/umbrella-tor/${TOR_ARCHIVE}" -C /tmp/umbrella-tor; then
        cp /tmp/umbrella-tor/tor/tor "${OUT}/tor/tor" && chmod +x "${OUT}/tor/tor"
        cp /tmp/umbrella-tor/data/geoip  "${OUT}/tor/geoip"  2>/dev/null || true
        cp /tmp/umbrella-tor/data/geoip6 "${OUT}/tor/geoip6" 2>/dev/null || true
    else
        echo "  warning: could not fetch Tor — Linux build ships without the (optional) Tor helper."
    fi
fi

# --- monero-wallet-rpc (official CLI bundle, linux-x64) — best-effort ---
MONERO_VERSION="${MONERO_VERSION:-v0.18.5.1}"
if [ ! -f "${OUT}/monero/monero-wallet-rpc" ]; then
    echo "Fetching Monero ${MONERO_VERSION}…"
    if mkdir -p "${OUT}/monero" /tmp/umbrella-monero \
        && curl -fL "https://downloads.getmonero.org/cli/monero-linux-x64-${MONERO_VERSION}.tar.bz2" -o /tmp/umbrella-monero/monero.tar.bz2 \
        && tar -xjf /tmp/umbrella-monero/monero.tar.bz2 -C /tmp/umbrella-monero; then
        cp /tmp/umbrella-monero/monero-*/monero-wallet-rpc "${OUT}/monero/monero-wallet-rpc" && chmod +x "${OUT}/monero/monero-wallet-rpc"
    else
        echo "  warning: could not fetch Monero — Linux build ships without the (optional) Monero helper."
    fi
fi

# Strip the Windows helpers that the build copies from the source tree — dead weight here.
rm -f "${OUT}/tor/tor.exe" "${OUT}/monero/monero-wallet-rpc.exe"

# Ensure the app itself is executable in the tarball (the .NET apphost + bundled helpers).
chmod +x "${OUT}/Umbrella.Wallet.App" 2>/dev/null || true
chmod +x "${OUT}/tor/tor" "${OUT}/monero/monero-wallet-rpc" 2>/dev/null || true

echo "Packing…"
tar -czf "dist/linux/umbrella-wallet-${VERSION}-linux-x64.tar.gz" -C dist/linux \
    "umbrella-wallet-${VERSION}-linux-x64"
echo "Done: dist/linux/umbrella-wallet-${VERSION}-linux-x64.tar.gz"
echo "Run with: ./Umbrella.Wallet.App"
