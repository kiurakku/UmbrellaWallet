<!-- Keep changes small and phase-isolated. See docs/ROADMAP.md and CONTRIBUTING.md. -->

## Description

<!-- What does this PR change, and why? -->

## Type of change

- [ ] Bug fix (non-breaking)
- [ ] New feature (non-breaking)
- [ ] Breaking change
- [ ] Documentation update
- [ ] Security improvement
- [ ] Dependencies / CI only

## Testing performed

- [ ] `dotnet test desktop/Umbrella.Wallet.sln` passes
- [ ] `dotnet build desktop/Umbrella.Wallet.sln -c Release` passes
- [ ] Manual testing completed (describe briefly below)
- [ ] Security implications reviewed

## Checklist

- [ ] Follows project style; self-reviewed
- [ ] Complex logic commented where needed
- [ ] Docs updated if behaviour or guarantees changed
- [ ] No new warnings that hide real failures
- [ ] No secret is logged (mnemonic, private key, API secret, raw signed tx)
- [ ] New network calls go through the shared `PublicHttp` (inherit Tor/proxy)
- [ ] No capability announced in README/CHANGELOG/UI before its end-to-end path passes
- [ ] LICENSE / trademark / attribution unchanged unless intentional

## Release-only (tick when this PR bumps the version)

- [ ] Version consistency check passes (`VERSION` == csproj == installer == README == CHANGELOG)
- [ ] CHANGELOG has an entry for this version
- [ ] After tagging, the GitHub Release has: Windows installer, portable zip, Linux tarball **and** `SHA256SUMS.txt`
- [ ] Checksums verify every attached artifact
- [ ] Third-party binary versions/hashes in `THIRD_PARTY_NOTICES.md` match what shipped

## Related issues

Closes #

## Additional notes

<!-- Screenshots for UI changes; anything reviewers should know. -->
