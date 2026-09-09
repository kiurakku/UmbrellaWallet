# Third-party binaries

Umbrella Wallet bundles two third-party executables so Tor and Monero work out of the box. They are
**not committed** to this repository — the build stages them by running the fetch scripts, which
download a **pinned version** and **verify its SHA-256** against the value recorded here before use.
Any mismatch stops the build (fail-closed). See `desktop/scripts/fetch-tor.ps1` and
`desktop/scripts/fetch-monero.ps1`.

When bumping a pinned version, update the version **and** the hash here and in the fetch script, and
take the new hash from the upstream project's own signed hashes file (linked below) — never from a
mirror or a local copy.

## Tor Expert Bundle

| | |
|---|---|
| Component | `tor.exe` + GeoIP databases (Windows x86_64 expert bundle) |
| Version | 15.0.22 |
| Archive | `tor-expert-bundle-windows-x86_64-15.0.22.tar.gz` |
| Source URL | https://dist.torproject.org/torbrowser/15.0.22/tor-expert-bundle-windows-x86_64-15.0.22.tar.gz |
| SHA-256 | `231dad6b9cb401a54c260db7046965ef04e4f72ff071b140d423fb5da281ab1e` |
| Hash source | https://dist.torproject.org/torbrowser/15.0.22/sha256sums-unsigned-build.txt |
| License | BSD 3-Clause (see the Tor Project's upstream `LICENSE`) |

## Monero CLI (monero-wallet-rpc)

| | |
|---|---|
| Component | `monero-wallet-rpc.exe` (from the Windows x64 CLI archive) |
| Version | 0.18.5.1 |
| Archive | `monero-win-x64-v0.18.5.1.zip` |
| Source URL | https://downloads.getmonero.org/cli/monero-win-x64-v0.18.5.1.zip |
| SHA-256 | `cf2ae8273977697d9ef2031c7337b781e6e5936578f602444b2990a173a2437d` |
| Hash source | https://www.getmonero.org/downloads/hashes.txt (PGP-signed by the Monero maintainers) |
| License | BSD 3-Clause (see the Monero project's upstream `LICENSE`) |

## Verifying by hand

The fetch scripts verify automatically, but to check a download yourself:

```powershell
Get-FileHash -Algorithm SHA256 .\tor-expert-bundle-windows-x86_64-15.0.22.tar.gz
Get-FileHash -Algorithm SHA256 .\monero-win-x64-v0.18.5.1.zip
```

For the strongest guarantee, verify the upstream hashes file's PGP signature (Monero publishes a
detached signature; the Tor Project signs its build manifests) before trusting the hash it lists.
