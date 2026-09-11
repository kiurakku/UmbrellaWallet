using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The Market list is built from two sources: every chain the wallet actually holds, plus a hand-kept
/// list of extra coins shown for price only. Nothing deduplicated them, so a coin that graduated from
/// "price only" to a real chain appeared twice — once correctly, and once claiming you cannot hold it.
/// Bitcoin Cash was in exactly that state.
/// </summary>
public sealed class MarketListTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"umbrella-market-{Guid.NewGuid():N}");

    private MainViewModel NewViewModel() =>
        new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

    [Fact]
    public void No_coin_appears_twice_in_the_market_list()
    {
        var vm = NewViewModel();

        var duplicates = vm.Market
            .GroupBy(m => m.Symbol, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} x{g.Count()}")
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void A_coin_the_wallet_can_hold_is_never_listed_as_unholdable()
    {
        // IsSupported drives whether the row offers to open that asset. A real chain marked
        // false is a wallet telling the user it cannot hold a coin it demonstrably holds.
        var vm = NewViewModel();
        var realChains = ChainCatalog.All
            .Where(c => c.Support == ChainSupportLevel.Supported)
            .Select(c => c.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var wrong = vm.Market
            .Where(m => realChains.Contains(m.Symbol) && !m.IsSupported)
            .Select(m => m.Symbol)
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void Every_chain_the_wallet_holds_has_a_market_row()
    {
        var vm = NewViewModel();
        var listed = vm.Market.Select(m => m.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = ChainCatalog.All
            .Where(c => c.Support == ChainSupportLevel.Supported && !listed.Contains(c.Symbol))
            .Select(c => c.Symbol)
            .ToList();

        Assert.Empty(missing);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
