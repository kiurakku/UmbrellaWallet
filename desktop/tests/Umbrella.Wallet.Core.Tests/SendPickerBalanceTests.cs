using System.Collections.ObjectModel;
using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The Send picker says, beside every asset, what the ACTIVE wallet holds of it — so choosing a coin
/// to send is also seeing whether there is anything to send.
///
/// Three things have to hold for that to be worth showing. The number must be the right row's: ETH on
/// Arbitrum is picked as "ARB" but held as ETH, and for as long as the picker matched the key against
/// the ticker every rollup read "Available: 0". An unread balance must read as unread, never as a zero.
/// And the refresh that keeps the numbers current must not move the selection — it used to clear and
/// refill the list every minute, which is how a review in progress could vanish.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class SendPickerBalanceTests : IDisposable
{
    private const string EthAddress = "0x9858EfFD232B4033E47d90003D41EC34EcaEda94";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"umbrella-picker-{Guid.NewGuid():N}");

    private MainViewModel NewViewModel() =>
        new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

    /// <summary>Rebuilds Holdings — and with them the picker — through a public command.</summary>
    private static void Rebuild(MainViewModel vm) => vm.SetHoldingsSortCommand.Execute("Name");

    private static WalletAccountViewModel Row(
        string symbol, double amount, BalanceRead read, string chain, string derivation = "BIP84",
        string address = "bc1qcr8te4kr609gcawutmrza0j4xv80jy8z306fyu") =>
        new(symbol, symbol, "Ready", address, derivation, 0, amount, chain, 0, Balance: read);

    private static SendOption Option(MainViewModel vm, string key) =>
        vm.SendableAssetOptions.Single(o => o.Symbol == key);

    [Fact]
    public void Each_entry_shows_what_the_active_wallet_holds()
    {
        var vm = NewViewModel();
        vm.Accounts.Clear();
        vm.Accounts.Add(Row("BTC", 0.5, BalanceRead.Live, "Bitcoin"));

        Rebuild(vm);

        Assert.Equal("0.5 BTC", Option(vm, "BTC").Balance);
        Assert.True(Option(vm, "BTC").HasBalance);
    }

    [Fact]
    public void An_unread_balance_is_a_dash_never_a_zero()
    {
        var vm = NewViewModel();
        vm.Accounts.Clear();
        vm.Accounts.Add(Row("BTC", 0, BalanceRead.Unknown, "Bitcoin"));

        Rebuild(vm);

        Assert.Equal("— BTC", Option(vm, "BTC").Balance);
        Assert.DoesNotContain("0", Option(vm, "BTC").Balance);
    }

    [Fact]
    public void A_cached_balance_says_it_is_the_last_known_one()
    {
        var vm = NewViewModel();
        vm.Accounts.Clear();
        vm.Accounts.Add(Row("BTC", 0.25, BalanceRead.Cached, "Bitcoin"));

        Rebuild(vm);

        Assert.StartsWith("0.25 BTC · ", Option(vm, "BTC").Balance);
        Assert.Contains(Loc.Instance["send.lastKnown"], Option(vm, "BTC").Balance);
    }

    [Fact]
    public void A_rollup_shows_its_own_eth_and_mainnet_shows_mainnets()
    {
        var vm = NewViewModel();
        vm.Accounts.Clear();
        vm.Accounts.Add(Row("ETH", 1.0, BalanceRead.Live, "Ethereum", "BIP44", EthAddress));
        vm.Accounts.Add(Row("ETH", 0.25, BalanceRead.Live, "Arbitrum", "EVM side-chain", EthAddress));

        Rebuild(vm);

        // Before: "ARB" matched no ticker, so the rollup read 0 — and nothing stopped the other mistake,
        // an Arbitrum row standing in for mainnet ETH if it happened to come first.
        Assert.Equal("0.25 ETH", Option(vm, "ARB").Balance);
        Assert.Equal("1 ETH", Option(vm, "ETH").Balance);

        vm.SelectedSendAsset = Option(vm, "ARB");
        Assert.Equal(0.25m, vm.SelectedSendBalance);
        Assert.Contains("0.25 ETH", vm.SelectedSendBalanceLabel);
    }

    [Fact]
    public void A_rollup_reads_eth_not_the_network_key()
    {
        var vm = NewViewModel();

        // The user reads the coin being sent; the key only routes.
        foreach (var key in MainViewModel.RollupNetworks.Keys)
            Assert.Equal("ETH", Option(vm, key).DisplayTicker);
    }

    [Fact]
    public void Every_rollup_key_is_a_chain_the_signer_knows_and_a_network_the_reader_reads()
    {
        var read = PublicChainBalanceClient.EvmSideNetworks
            .Where(n => n.Symbol == "ETH")
            .Select(n => n.Network)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, network) in MainViewModel.RollupNetworks)
        {
            Assert.True(EthTransactionSender.Chains.ContainsKey(key), $"{key} is not a chain the signer knows");
            Assert.Contains(network, read);
        }

        // And the other way round: a rollup the reader reads but the picker cannot find would read 0.
        Assert.Equal(read.Count, MainViewModel.RollupNetworks.Count);
    }

    [Fact]
    public void A_refresh_keeps_the_selection_and_the_review()
    {
        var vm = NewViewModel();
        vm.Accounts.Clear();
        vm.Accounts.Add(Row("BTC", 0.5, BalanceRead.Live, "Bitcoin"));
        Rebuild(vm);

        var selected = Option(vm, "BTC");
        vm.SelectedSendAsset = selected;
        vm.HasSendQuote = true;   // a review on screen

        // The balance moves, as it does on a refresh.
        vm.Accounts.Clear();
        vm.Accounts.Add(Row("BTC", 0.75, BalanceRead.Live, "Bitcoin"));
        Rebuild(vm);

        Assert.Same(selected, vm.SelectedSendAsset);
        Assert.True(vm.HasSendQuote);
        Assert.Equal("0.75 BTC", selected.Balance);
    }

    [Fact]
    public void Syncing_keeps_every_surviving_entry_as_the_same_object()
    {
        var a = new SendOption("A", "a", "n");
        var b = new SendOption("B", "b", "n");
        var c = new SendOption("C", "c", "n");
        var list = new ObservableCollection<SendOption> { a, b, c };

        // B goes, D arrives, the rest keep their identity.
        MainViewModel.SyncInPlace(list, [new SendOption("A", "a", "n"), new SendOption("D", "d", "n"), new SendOption("C", "c", "n")]);

        Assert.Equal(["A", "D", "C"], list.Select(o => o.Symbol));
        Assert.Same(a, list[0]);
        Assert.Same(c, list[2]);
    }

    [Fact]
    public void Options_are_equal_by_route_not_by_balance()
    {
        var one = new SendOption("BTC", "Bitcoin", "net") { Balance = "1 BTC" };
        var two = new SendOption("BTC", "Bitcoin", "net") { Balance = "2 BTC" };

        Assert.Equal(one, two);
        Assert.Equal(one.GetHashCode(), two.GetHashCode());
        Assert.NotEqual(one, new SendOption("LTC", "Litecoin", "net"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
