using NBitcoin;
using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The screens around PSBT (roadmap H.1), driven through the same commands the buttons are bound to:
/// the verify screen exports the Taproot account the balance now includes, and the PSBT card refuses
/// to offer a signature until the wallet has seen its own coins on-chain.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class PsbtScreenTests : IDisposable
{
    private const string Password = "umbrella-psbt-2026";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"umbrella-psbt-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); } catch { }
    }

    private async Task<MainViewModel> UnlockedWalletAsync()
    {
        TestDataIsolation.RestoreBaselineSettings();
        var vm = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));
        vm.Password = Password;
        vm.ConfirmPassword = Password;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);
        Assert.True(vm.IsWorkspace);
        return vm;
    }

    [Fact]
    public async Task The_verify_screen_exports_the_Taproot_account_beside_the_SegWit_one()
    {
        var vm = await UnlockedWalletAsync();

        vm.XpubPassword = Password;
        await vm.RevealXpubsCommand.ExecuteAsync(null);

        Assert.Contains(vm.ExportedXpubs, r => r.Symbol == "BTC" && r.Path == "m/84'/0'/0'");
        var taproot = Assert.Single(vm.ExportedXpubs, r => r.Symbol.Contains("Taproot"));
        Assert.Equal("m/86'/0'/0'", taproot.Path);
        Assert.StartsWith("xpub", taproot.Key);
    }

    [Fact]
    public async Task A_PSBT_is_not_offered_for_signing_before_the_wallet_has_seen_its_coins()
    {
        // Nothing is reachable here, so the scan cannot complete — and without it the wallet has no
        // on-chain record to check the PSBT against. It must say so, not sign on the PSBT's word.
        var vm = await UnlockedWalletAsync();
        var proxyBefore = PublicHttp.ActiveProxy;
        PublicHttp.SetProxy("socks5://127.0.0.1:1");
        try
        {
            vm.PsbtInput = AnyPsbt();
            await vm.ReviewPsbtCommand.ExecuteAsync(null);

            Assert.Equal(Loc.Instance["psbt.errNoScan"], vm.PsbtError);
            Assert.False(vm.CanSignPsbt);
            Assert.False(vm.HasPsbtReview);
        }
        finally
        {
            PublicHttp.SetProxy(proxyBefore);
        }
    }

    [Fact]
    public async Task Text_that_is_not_a_PSBT_is_refused_before_anything_is_scanned()
    {
        var vm = await UnlockedWalletAsync();

        vm.PsbtInput = "definitely not a psbt";
        await vm.ReviewPsbtCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.PsbtError);
        Assert.False(vm.HasPsbtReview);
    }

    [Fact]
    public async Task There_is_nothing_to_export_without_a_Bitcoin_review()
    {
        var vm = await UnlockedWalletAsync();
        Assert.False(vm.CanExportSendPsbt);
    }

    private static string AnyPsbt()
    {
        var d = new HdAddressDeriver();
        var from = d.DeriveBitcoinLikeAt(
            "legal winner thank year wave sausage worth useful legal winner thank yellow", ChainId.Btc, 0, 0);
        var tx = Network.Main.CreateTransaction();
        tx.Inputs.Add(new TxIn(new OutPoint(uint256.Parse(new string('f', 64)), 0)));
        tx.Outputs.Add(new TxOut(Money.Satoshis(10_000), from.ScriptPubKey));
        return PSBT.FromTransaction(tx, Network.Main).ToBase64();
    }
}
