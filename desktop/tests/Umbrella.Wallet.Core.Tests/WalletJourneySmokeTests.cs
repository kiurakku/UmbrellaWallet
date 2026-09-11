using System.Text.Json;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The journey smoke suite the roadmap asks for (§8.3.4): create → unlock → receive → send
/// validation → backup verify → lock, driven end to end through the same commands the buttons are
/// bound to. Its job is to be the safety net for the view/view-model split — if a screen is moved out
/// of MainWindow and something stops being wired up, this fails before a user ever sees it.
///
/// Everything runs against a throwaway vault in a temp directory; no network, no real funds, and the
/// live data directory is never touched.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class WalletJourneySmokeTests : IDisposable
{
    private const string Password = "umbrella-journey-2026";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"umbrella-journey-{Guid.NewGuid():N}");

    private string VaultPath => Path.Combine(_directory, "vault.json");

    private MainViewModel NewViewModel() => new(new EncryptedFileSeedVault(VaultPath));

    /// <summary>Create → back up the phrase → workspace → lock → unlock again, in one run.</summary>
    [Fact]
    public async Task Create_backup_lock_and_unlock_is_one_unbroken_journey()
    {
        var vm = NewViewModel();

        // 1. A fresh install opens on Welcome, not on a wallet.
        Assert.True(vm.IsWelcomeStage);
        Assert.False(vm.IsWorkspace);

        // 2. Create: the phrase is shown for backup before the workspace opens.
        vm.Password = Password;
        vm.ConfirmPassword = Password;
        await vm.CreateWalletCommand.ExecuteAsync(null);

        Assert.True(vm.IsBackupStage);
        Assert.Equal(24, vm.RecoveryPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

        // 3. Acknowledging the backup opens the workspace and drops the phrase from memory.
        vm.ConfirmPhraseBackupCommand.Execute(null);
        Assert.True(vm.IsWorkspace);
        Assert.Empty(vm.RecoveryPhrase);

        // 4. Lock: no workspace, no accounts with real addresses left on screen.
        vm.LockCommand.Execute(null);
        Assert.True(vm.IsUnlockStage);
        Assert.False(vm.IsUnlocked);

        // 5. Unlock with the same password re-opens the same wallet.
        vm.Password = Password;
        await vm.UnlockCommand.ExecuteAsync(null);

        Assert.True(vm.IsWorkspace);
        Assert.Empty(vm.FormError);
    }

    /// <summary>
    /// Receive has to hand out a real address for a real chain — the whole point of the screen. This
    /// pins the wiring from unlock → derived accounts → the selected receive target and its QR.
    /// </summary>
    [Fact]
    public async Task Receive_offers_a_real_address_with_a_qr_after_unlock()
    {
        var vm = await UnlockedWalletAsync();

        vm.SelectSectionCommand.Execute("Receive");

        Assert.True(vm.IsReceive);
        Assert.False(string.IsNullOrWhiteSpace(vm.SelectedReceiveAddress));
        Assert.DoesNotContain("Unlock wallet", vm.SelectedReceiveAddress);
        Assert.DoesNotContain(" ", vm.SelectedReceiveAddress);
        Assert.Contains(vm.SelectedReceiveSymbol, vm.SelectedReceiveNetwork);

        // The shown address is the derived account's own address, not a stray string.
        Assert.Contains(vm.Accounts, a => a.Symbol == vm.SelectedReceiveSymbol
                                          && a.Address == vm.SelectedReceiveAddress);

        // The QR bitmap itself is not asserted: decoding a PNG needs Avalonia's rendering platform,
        // which a headless test run does not have (BuildQr returns null there, by design).
    }

    /// <summary>
    /// The two-step Review → Confirm gate: a malformed destination never produces a quote, and Confirm
    /// without a quote is refused — so nothing is signed, broadcast or logged on the way through.
    /// </summary>
    [Fact]
    public async Task Send_refuses_a_malformed_destination_and_confirm_without_a_quote()
    {
        var vm = await UnlockedWalletAsync();
        var activityBefore = vm.Activity.Count;

        vm.SelectSectionCommand.Execute("Send");
        vm.SendChain = "ETH";
        vm.SendTo = "definitely-not-an-address";
        vm.SendAmount = "0.01";
        await vm.PrepareSendCommand.ExecuteAsync(null);

        Assert.False(vm.HasSendQuote);
        Assert.False(string.IsNullOrWhiteSpace(vm.SendError));

        await vm.ConfirmSendCommand.ExecuteAsync(null);

        Assert.Equal(activityBefore, vm.Activity.Count);
        Assert.DoesNotContain(vm.Activity, a => a.Kind.Contains("Sen", StringComparison.Ordinal));
    }

    /// <summary>
    /// A backup is only worth having if it can be restored. This proves the real verification path:
    /// the right password verifies, the wrong one is refused, and neither ever surfaces the phrase.
    /// </summary>
    [Fact]
    public async Task A_backup_of_this_vault_verifies_with_its_password_and_only_that_password()
    {
        await UnlockedWalletAsync();
        var backupPath = await WriteBackupBundleAsync();

        var good = await VaultBackup.VerifyAsync(backupPath, Password);
        Assert.True(good.Ok);
        Assert.DoesNotContain("abandon", good.Message, StringComparison.OrdinalIgnoreCase);

        var bad = await VaultBackup.VerifyAsync(backupPath, "umbrella-wrong-password-2026");
        Assert.False(bad.Ok);
    }

    /// <summary>A file that is not a backup is rejected as such, not treated as an empty vault.</summary>
    [Fact]
    public async Task A_file_that_is_not_a_backup_is_refused()
    {
        Directory.CreateDirectory(_directory);
        var junk = Path.Combine(_directory, "notes.txt");
        await File.WriteAllTextAsync(junk, "shopping list");

        var result = await VaultBackup.VerifyAsync(junk, Password);

        Assert.False(result.Ok);
    }

    /// <summary>
    /// Locking must not leave anything sensitive addressable: the phrase, the settings-revealed phrase
    /// and the receive target all go with it.
    /// </summary>
    [Fact]
    public async Task Locking_clears_the_screen_of_wallet_state()
    {
        var vm = await UnlockedWalletAsync();
        vm.SelectSectionCommand.Execute("Receive");
        Assert.False(string.IsNullOrWhiteSpace(vm.SelectedReceiveAddress));

        vm.LockCommand.Execute(null);

        Assert.False(vm.IsUnlocked);
        Assert.Empty(vm.RecoveryPhrase);
        Assert.Empty(vm.SettingsRevealedPhrase);
        Assert.Empty(vm.SelectedReceiveAddress);
    }

    /// <summary>Every section the navigation offers has to be reachable and mutually exclusive.</summary>
    [Fact]
    public async Task Every_primary_section_opens_and_only_one_is_active()
    {
        var vm = await UnlockedWalletAsync();

        foreach (var section in new[] { "Portfolio", "Receive", "Send", "Swap", "Activity", "Market", "Discover", "Security", "Settings" })
        {
            vm.SelectSectionCommand.Execute(section);
            Assert.Equal(section, vm.ActiveSection);

            var flags = new[]
            {
                vm.IsPortfolio, vm.IsReceive, vm.IsSend, vm.IsSwap, vm.IsActivity,
                vm.IsMarket, vm.IsDiscover, vm.IsSecurity, vm.IsSettings,
            };
            Assert.Single(flags, f => f);
        }
    }

    // ---- helpers ----------------------------------------------------------

    private async Task<MainViewModel> UnlockedWalletAsync()
    {
        var vm = NewViewModel();
        vm.Password = Password;
        vm.ConfirmPassword = Password;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);
        return vm;
    }

    /// <summary>
    /// Builds the same bundle shape <see cref="VaultBackup.ExportAsync"/> writes, but from THIS test's
    /// vault. Export itself reads the machine's live data directory, which a test must not touch.
    /// </summary>
    private async Task<string> WriteBackupBundleAsync()
    {
        var bundle = new Dictionary<string, string?>
        {
            ["magic"] = "umbrella-backup-v1",
            ["exportedUtc"] = DateTime.UtcNow.ToString("O"),
            ["vault"] = await File.ReadAllTextAsync(VaultPath),
        };

        var path = Path.Combine(_directory, "umbrella-backup.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(bundle));
        return path;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); } catch { }
    }
}
