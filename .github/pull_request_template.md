## Summary

<!-- What does this PR change and why? -->

## Security checklist

- [ ] No seed phrases, private keys, or API secrets committed
- [ ] Wallet / signing changes reviewed against existing test vectors
- [ ] `desktop` + `Security` + `CodeQL` CI checks are green

## Test plan

- [ ] `dotnet test desktop/Umbrella.Wallet.sln`
- [ ] Manual smoke test (create/unlock wallet, receive address) if UI changed
