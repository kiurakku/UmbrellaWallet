using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// A linked (watch-only) wallet must contribute to the total balance.
///
/// It previously didn't: the row was priced by the chain string the user typed, so linking a
/// wallet as "ERC20" or "Ethereum" resolved the right chain but missed the price table, leaving
/// price 0 — and 0 × amount contributed nothing to the total, so the wallet looked ignored.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class WatchBalanceTests : IDisposable
{
    private const string GoodPassword = "umbrella-test-vault-2026";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"umbrella-watch-{Guid.NewGuid():N}");

    private MainViewModel NewViewModel() =>
        new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

    [Theory]
    [InlineData("ETH")]
    [InlineData("ERC20")]
    [InlineData("Ethereum")]
    [InlineData("BTC")]
    [InlineData("TRC20")]
    public void Watch_rows_are_priced_by_the_canonical_ticker(string typedChain)
    {
        // Whatever the user types, the row must end up under a ticker the price feed knows.
        var symbol = MainViewModel.CanonicalSymbolForChain(typedChain);

        Assert.False(string.IsNullOrWhiteSpace(symbol));
        Assert.Equal(symbol, symbol.ToUpperInvariant());
        Assert.Contains(symbol, new[] { "ETH", "BTC", "TRX" });
    }

    [Fact]
    public void Unknown_chain_text_is_rejected_rather_than_priced_as_zero()
    {
        Assert.Null(MainViewModel.CanonicalSymbolForChain("not-a-chain"));
    }

    [Fact]
    public async Task Holdings_include_watch_rows_so_they_reach_the_total()
    {
        var vm = NewViewModel();
        vm.Password = GoodPassword;
        vm.ConfirmPassword = GoodPassword;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);

        // A watch row with a real price and amount must be counted, not filtered out.
        vm.Accounts.Add(new WalletAccountViewModel(
            "ETH", "Linked wallet", "Watch",
            "0x742d35Cc6634C0532925a3b844Bc454e4438f44e", "external",
            Price: 2000, Amount: 1.5, Chain: "ETH", Change24h: 0));

        vm.RecomputeHoldingsForTest();

        var watchRow = vm.Holdings.FirstOrDefault(h => h.SupportStatus == "Watch");
        Assert.NotNull(watchRow);
        Assert.Equal(3000, watchRow!.Value);
        Assert.Contains(vm.Holdings, h => h.Value > 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
