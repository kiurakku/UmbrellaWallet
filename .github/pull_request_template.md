<!-- Thanks for contributing to Umbrella Wallet. Keep changes small and phase-isolated
     (see docs/CLAUDE_IMPLEMENTATION_ROADMAP_UK.md §1). -->

## What & why

<!-- One or two sentences. Link the roadmap phase/section if relevant. -->

## Checklist

- [ ] `dotnet test desktop/Umbrella.Wallet.sln` passes
- [ ] `dotnet build desktop/Umbrella.Wallet.sln -c Release` passes
- [ ] No secret is logged (mnemonic, private key, API secret, raw signed tx)
- [ ] New network calls go through the shared `PublicHttp` (inherit Tor/proxy)
- [ ] No capability announced in README/CHANGELOG/UI before its end-to-end path passes

## Release-only (tick when this PR bumps the version)

- [ ] `bash scripts/check-version-consistency.sh` passes (VERSION == csproj == installer == README == CHANGELOG)
- [ ] CHANGELOG has an entry for this version
- [ ] After tagging, the GitHub Release has: Windows installer, portable zip, Linux tarball **and** `SHA256SUMS.txt`
- [ ] `sha256sum -c SHA256SUMS.txt` verifies every attached artifact
- [ ] Third-party binary versions/hashes in `THIRD_PARTY_NOTICES.md` match what shipped
