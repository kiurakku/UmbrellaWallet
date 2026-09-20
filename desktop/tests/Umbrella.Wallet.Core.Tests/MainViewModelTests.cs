using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Drives the same commands the buttons are bound to.
///
/// Reported as "the phrase is not generated when you create a wallet". Generation was never
/// broken — the form rejected the password and wrote the reason to StatusMessage in the title
/// bar, so the button looked dead. Every section then looked dead too, because they are gated
/// behind ShowWorkspace => IsUnlocked. These tests pin both the happy path and the feedback.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class MainViewModelTests : IDisposable
{
    private const string GoodPassword = "umbrella-test-vault-2026";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"umbrella-vm-{Guid.NewGuid():N}");

    private MainViewModel NewViewModel()
    {
        // These tests assert on the onboarding stages, which sit behind the first-run
        // acknowledgement. A wipe test running earlier in the same collection deletes the settings
        // file — correctly — so the baseline is restored rather than assumed.
        TestDataIsolation.RestoreBaselineSettings();
        return new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));
    }

    [Fact]
    public async Task CreateWallet_ShowsA24WordPhrase_AndOpensTheWorkspace()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;

        await vm.CreateWalletCommand.ExecuteAsync(null);

        // Create shows the phrase-backup page first; the workspace opens only after ack.
        Assert.Equal(24, vm.RecoveryPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.True(vm.IsUnlocked);
        Assert.True(vm.IsBackupStage);
        Assert.False(vm.IsWorkspace);
        Assert.False(vm.IsWelcomeStage);
        Assert.Empty(vm.FormError);

        vm.ConfirmPhraseBackupCommand.Execute(null);
        Assert.True(vm.IsWorkspace);
        Assert.False(vm.IsBackupStage);
        Assert.Empty(vm.RecoveryPhrase);
    }

    [Fact]
    public async Task CreateWallet_ShortPassword_ReportsVisibly_AndGeneratesNothing()
    {
        var vm = NewViewModel();
        vm.Password = "short";
        vm.ConfirmPassword = "short";

        await vm.CreateWalletCommand.ExecuteAsync(null);

        // The regression: this used to be reachable only via StatusMessage in the title bar.
        Assert.True(vm.HasFormError);
        Assert.Contains("12", vm.FormError);
        Assert.Empty(vm.RecoveryPhrase);
        Assert.False(vm.IsUnlocked);
    }

    [Fact]
    public async Task CreateWallet_MismatchedConfirm_ReportsVisibly()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword + "-typo";

        await vm.CreateWalletCommand.ExecuteAsync(null);

        Assert.True(vm.HasFormError);
        Assert.Contains("do not match", vm.FormError);
        Assert.Empty(vm.RecoveryPhrase);
    }

    [Fact]
    public void TypingAPassword_ClearsTheError_AndMovesTheMeter()
    {
        var vm = NewViewModel();
        vm.FormError = "stale error";

        vm.Password = GoodPassword;

        Assert.False(vm.HasFormError);
        Assert.Contains("strong", vm.PasswordMeterLabel);
        Assert.True(vm.CanSubmitVaultForm);
    }

    [Fact]
    public async Task Sections_TrackTheNavButtons()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);

        vm.SelectSectionCommand.Execute("Settings");
        Assert.True(vm.IsSettings);
        Assert.False(vm.IsPortfolio);

        vm.SelectSectionCommand.Execute("Send");
        Assert.True(vm.IsSend);
        Assert.False(vm.IsSettings);
    }

    /// <summary>
    /// The Send picker must offer EXACTLY the symbols the send path can actually broadcast — the bug
    /// where ADA and the EVM side-chains were offered but rejected by the guard, and TON worked but
    /// was hidden. Both now read one capability set, pinned here so they can never drift.
    /// </summary>
    [Fact]
    public void SendableAssets_exactly_match_the_sendable_capability_set()
    {
        var vm = NewViewModel();
        var offered = vm.SendableAssets.Select(a => a.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(offered.SetEquals(MainViewModel.SendableSymbols),
            $"picker=[{string.Join(",", offered.OrderBy(s => s))}] " +
            $"capability=[{string.Join(",", MainViewModel.SendableSymbols.OrderBy(s => s))}]");

        // The exact assets the roadmap called out as broken must now be offered AND declared sendable.
        // ARB/BASE/OP are the Ethereum L2 rollups (native ETH, same 0x address, EIP-155 with the L2 id).
        // BCH is a real UTXO spend (SIGHASH_FORKID, Haskoin UTXOs/broadcast). DOGE is the UTXO spend the
        // 4.5.0 changelog announced ("DOGE is now spendable") — now actually reachable in the picker.
        foreach (var sym in new[] { "ADA", "BNB", "MATIC", "AVAX", "FTM", "CRO", "TON", "ARB", "BASE", "OP", "BCH", "DOGE" })
            Assert.Contains(sym, offered);
    }

    /// <summary>With no vault, the app shows the full-screen Welcome, not the workspace.</summary>
    [Fact]
    public void NoVault_ShowsWelcome_NotWorkspace()
    {
        var vm = NewViewModel();

        Assert.True(vm.IsWelcomeStage);
        Assert.False(vm.ShowSidebar);
        Assert.False(vm.IsWorkspace);

        vm.GoToImportCommand.Execute(null);
        Assert.True(vm.IsImportStage);
        Assert.False(vm.IsWelcomeStage);

        vm.GoToWelcomeCommand.Execute(null);
        Assert.True(vm.IsWelcomeStage);
    }

    /// <summary>The market list is populated at startup and states support honestly.</summary>
    [Fact]
    public void Market_ListsEveryCoin_WithHonestSupport()
    {
        var vm = NewViewModel();

        // Snapshot once: the market refresh the view model starts on construction mutates this
        // collection, and enumerating it mid-update fails with "collection was modified" — a flake
        // about timing, not about the listing.
        var market = vm.Market.ToList();

        // The wallet's own chains plus popular market-only coins (priced + charted, held via the
        // EVM/token paths), so the market is broader than the account list.
        Assert.True(market.Count > Chains.ChainCatalog.All.Count);
        Assert.Contains(market, m => m.Symbol == "BTC" && m.IsSupported);
        Assert.Contains(market, m => m.Symbol == "SOL" && m.IsSupported);
        Assert.Contains(market, m => m.Symbol == "XMR" && !m.IsSupported);
        Assert.Contains(market, m => m.Symbol == "BNB"); // a market-only coin
        Assert.Contains(market, m => m.Symbol == "XRP");
    }

    /// <summary>Reveal must never expose the phrase without the correct password.</summary>
    [Fact]
    public async Task RevealInSettings_NeverShowsPhraseWithoutPassword()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);

        // No password entered.
        vm.SettingsPassword = string.Empty;
        await vm.RevealPhraseCommand.ExecuteAsync(null);
        Assert.False(vm.IsSettingsPhraseVisible);
        Assert.Empty(vm.SettingsRevealedPhrase);

        // Wrong password.
        vm.SettingsPassword = "wrong-password-xxxx";
        await vm.RevealPhraseCommand.ExecuteAsync(null);
        Assert.False(vm.IsSettingsPhraseVisible);
        Assert.Empty(vm.SettingsRevealedPhrase);

        // Correct password.
        vm.SettingsPassword = GoodPassword;
        await vm.RevealPhraseCommand.ExecuteAsync(null);
        Assert.True(vm.IsSettingsPhraseVisible);
        Assert.Equal(24, vm.SettingsRevealedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

        // Leaving Settings clears it from the screen.
        vm.SelectSectionCommand.Execute("Portfolio");
        Assert.False(vm.IsSettingsPhraseVisible);
        Assert.Empty(vm.SettingsRevealedPhrase);
    }

    /// <summary>
    /// Send is real for ETH/BTC/LTC/SOL. Unsupported assets and malformed destinations must be
    /// refused before anything is signed, and neither case may write a fake Activity row.
    /// </summary>
    [Fact]
    public async Task Send_RefusesUnsupportedAssetsAndBadAddresses_WithoutFakeActivity()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);

        var activityBefore = vm.Activity.Count;

        // XMR can be sent, but only once the bundled Monero daemon is running — with it off,
        // the user must be told to turn it on rather than silently getting nothing.
        vm.SendChain = "XMR";
        vm.SendTo = "44AFFq5kSiGBoZ4NMDwYtN18obc8AemS33DBLWs3H7otXft3XjrpDtQGv7SqSsaBYBb98uNbr2VBBEt7f2wfn3RVGQBEP3A";
        vm.SendAmount = "0.01";
        await vm.PrepareSendCommand.ExecuteAsync(null);
        Assert.Contains("Monero wallet service", vm.SendError);
        Assert.False(vm.HasSendQuote);

        // A coin with a real address but no send path (ZEC — transparent receive only) is refused
        // outright — the send picker never offers it, and the guard rejects it if reached programmatically.
        vm.SendChain = "ZEC";
        vm.SendTo = "t1XVXWCvpMgBvUaed4XDqWtgQgJSu1Ghz7F";
        await vm.PrepareSendCommand.ExecuteAsync(null);
        Assert.Contains("not available", vm.SendError);
        Assert.False(vm.HasSendQuote);

        // The route gate (P0.7) refuses a send whose transport is not what the user asked for, and
        // this suite runs with the kill-switch armed and no proxy — which is that exact state. The
        // address checks below are about the chain layer, so the test gives the wallet a coherent
        // route first: a proxy on a dead loopback port satisfies the kill-switch and keeps the run
        // offline, since nothing is listening there.
        var proxyBefore = PublicHttp.ActiveProxy;
        PublicHttp.SetProxy("socks5://127.0.0.1:1");
        try
        {
            // ETH with a malformed destination → rejected before any signing.
            vm.SendChain = "ETH";
            vm.SendTo = "not-an-address";
            await vm.PrepareSendCommand.ExecuteAsync(null);
            Assert.Contains("0x", vm.SendError);
            Assert.False(vm.HasSendQuote);

            // BTC with a destination that isn't a valid mainnet address → rejected too.
            vm.SendChain = "BTC";
            vm.SendTo = "definitely-not-bitcoin";
            await vm.PrepareSendCommand.ExecuteAsync(null);
            Assert.False(vm.HasSendQuote);
            Assert.NotEmpty(vm.SendError);
        }
        finally
        {
            PublicHttp.SetProxy(proxyBefore);
        }

        // Confirm without a quote must refuse.
        await vm.ConfirmSendCommand.ExecuteAsync(null);
        Assert.Contains("Prepare", vm.SendError);

        Assert.Equal(activityBefore, vm.Activity.Count);
        Assert.DoesNotContain(vm.Activity, a => a.Kind.Contains("Sen", StringComparison.Ordinal));
    }

    /// <summary>
    /// Regression for the stranded-onboarding bug: after "Add wallet" the create/import screen must NOT
    /// get stuck asking for a password it hides (which blocked creating OR importing any wallet).
    /// Creating the added wallet with no password typed must succeed and open it.
    /// </summary>
    [Fact]
    public async Task MultiWallet_AddWallet_IsNeverStranded_WithoutAPassword()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);

        vm.NewWalletLabel = "Second";
        vm.BeginAddWalletCommand.Execute(null);

        // The app password is retained across the add-lock, so the fields hide and reuse kicks in.
        Assert.True(vm.HasSessionPassword);
        Assert.True(vm.ReuseAppPassword);

        // Create with NOTHING typed into the (hidden) password fields — must not fail with a password error.
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);

        Assert.Empty(vm.FormError);
        Assert.False(vm.IsAddingWallet);
        Assert.True(vm.IsUnlocked);
        Assert.Equal(2, vm.Wallets.Count);
    }

    /// <summary>An import into an additional wallet must also work with no password typed (same bug).</summary>
    [Fact]
    public async Task MultiWallet_AddWallet_ViaImport_ReusesAppPassword()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);

        vm.NewWalletLabel = "Imported";
        vm.BeginAddWalletCommand.Execute(null);

        // A valid 12-word phrase, no password typed.
        vm.ImportPhrase =
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
        await vm.ImportWalletCommand.ExecuteAsync(null);

        Assert.Empty(vm.FormError);
        Assert.True(vm.IsUnlocked);
        Assert.Equal("Imported", vm.ActiveWalletLabel);
        Assert.Equal(2, vm.Wallets.Count);
    }

    /// <summary>One common app password: adding a wallet reuses it (no new password asked), and
    /// switching between wallets unlocks seamlessly without re-prompting.</summary>
    [Fact]
    public async Task MultiWallet_OneCommonPassword_AddsAndSwitchesSeamlessly()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);
        var mainId = vm.Wallets.Single(w => w.IsActive).Id;

        // Add a second wallet WITHOUT typing any password — it silently reuses the app password.
        vm.NewWalletLabel = "Savings";
        vm.BeginAddWalletCommand.Execute(null);
        Assert.True(vm.ReuseAppPassword);          // reusing, so no password prompt
        Assert.False(vm.ShowVaultPasswordFields);  // password fields hidden
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);
        Assert.True(vm.IsUnlocked);
        Assert.Equal("Savings", vm.ActiveWalletLabel);
        var savingsId = vm.Wallets.Single(w => w.IsActive).Id;

        // Switch back to Main → seamless, no password prompt.
        await vm.SwitchWalletCommand.ExecuteAsync(mainId);
        Assert.True(vm.IsUnlocked);
        Assert.Equal("Main wallet", vm.ActiveWalletLabel);

        // Forward to Savings again → also seamless.
        await vm.SwitchWalletCommand.ExecuteAsync(savingsId);
        Assert.True(vm.IsUnlocked);
        Assert.Equal("Savings", vm.ActiveWalletLabel);
    }

    /// <summary>Cancelling an add-wallet must de-register the pending wallet and leave exactly the
    /// original wallet behind.</summary>
    [Fact]
    public async Task MultiWallet_CancelAddWallet_RemovesThePendingWallet()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);

        vm.NewWalletLabel = "Throwaway";
        vm.BeginAddWalletCommand.Execute(null);
        Assert.Equal(2, vm.Wallets.Count);

        await vm.CancelAddWalletCommand.ExecuteAsync(null);
        Assert.False(vm.IsAddingWallet);
        Assert.Single(vm.Wallets);
        Assert.Equal("Main wallet", vm.ActiveWalletLabel);
    }

    /// <summary>Change password re-encrypts the vault: afterwards the old password fails and the new
    /// one unlocks.</summary>
    [Fact]
    public async Task ChangePassword_ReEncryptsSoNewPasswordUnlocks()
    {
        const string newPw = "brand-new-password-2026";
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);

        vm.ChangePwCurrent = GoodPassword;
        vm.ChangePwNew = newPw;
        vm.ChangePwConfirm = newPw;
        await vm.ChangePasswordCommand.ExecuteAsync(null);
        Assert.Empty(vm.FormError);

        vm.LockVault();
        vm.Password = GoodPassword;            // old password no longer works
        await vm.UnlockCommand.ExecuteAsync(null);
        Assert.False(vm.IsUnlocked);

        vm.Password = newPw;                   // new password does
        await vm.UnlockCommand.ExecuteAsync(null);
        Assert.True(vm.IsUnlocked);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_IsRejected()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);

        vm.ChangePwCurrent = "not-the-current-password";
        vm.ChangePwNew = "another-long-password-2026";
        vm.ChangePwConfirm = "another-long-password-2026";
        await vm.ChangePasswordCommand.ExecuteAsync(null);

        Assert.Contains("incorrect", vm.FormError);
    }

    /// <summary>Forgot password: restoring the active wallet from its recovery phrase sets a new
    /// password and unlocks it.</summary>
    [Fact]
    public async Task ForgotPassword_RestoreWithSeed_SetsNewPasswordAndUnlocks()
    {
        const string seed =
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
        const string newPw = "restored-password-2026";
        var vm = NewViewModel();

        vm.GoToImportCommand.Execute(null);
        vm.ImportPhrase = seed;
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.ImportWalletCommand.ExecuteAsync(null);
        Assert.True(vm.IsUnlocked);

        vm.LockVault();
        Assert.True(vm.IsUnlockStage);

        vm.BeginPasswordResetCommand.Execute(null);
        Assert.True(vm.IsResettingPassword);

        vm.ImportPhrase = seed;
        vm.Password = newPw;
        vm.ConfirmPassword = newPw;
        await vm.ResetWithSeedCommand.ExecuteAsync(null);
        Assert.Empty(vm.FormError);
        Assert.True(vm.IsUnlocked);
        Assert.False(vm.IsResettingPassword);

        vm.LockVault();
        vm.Password = newPw;
        await vm.UnlockCommand.ExecuteAsync(null);
        Assert.True(vm.IsUnlocked);
    }

    /// <summary>Importing a TON-native phrase (Telegram Wallet / Tonkeeper) creates a Toncoin-only
    /// wallet showing the exact TON address that wallet displays.</summary>
    [Fact]
    public async Task Import_TonMnemonic_CreatesTonOnlyWallet_WithCorrectAddress()
    {
        const string tonPhrase =
            "derive earn future trumpet gallery nation antique cabin seat object rival wrong thank humor " +
            "glide mobile reduce often scale beauty youth base horn brisk";
        var vm = NewViewModel();

        vm.GoToImportCommand.Execute(null);
        vm.ImportPhrase = tonPhrase;
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.ImportWalletCommand.ExecuteAsync(null);

        Assert.Empty(vm.FormError);
        Assert.True(vm.IsUnlocked);
        Assert.Contains(vm.Accounts, a =>
            a.Symbol == "TON" && a.Address == "UQCLhuuBbuTzBY7oDkmYAF8vhnB7c2f0XDDWUflQUvHdLYFm");
        Assert.DoesNotContain(vm.Accounts, a => a.Symbol == "BTC"); // TON-only, no BIP39 chains
    }

    [Fact]
    public async Task DeleteVault_RequiresTypedConfirmation()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);

        vm.DeleteConfirmation = "yes";
        vm.DeleteVaultCommand.Execute(null);
        Assert.True(vm.HasVault);

        vm.DeleteConfirmation = "DELETE";
        vm.DeleteVaultCommand.Execute(null);
        Assert.False(vm.HasVault);
        Assert.True(vm.IsWelcomeStage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
