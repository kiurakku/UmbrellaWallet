using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The market overview is derived from prices the wallet already has — it must never invent a mover
/// out of a coin whose price is unknown, and the watchlist is a local, persisted list of tickers.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class MarketOverviewTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"umbrella-market-{Guid.NewGuid():N}");

    private MainViewModel NewViewModel() =>
        new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

    /// <summary>An unknown price is not "unchanged": with no live feed there are no movers at all.</summary>
    [Fact]
    public void Coins_without_a_live_price_are_never_shown_as_movers()
    {
        var vm = NewViewModel();

        Assert.NotEmpty(vm.Market);
        Assert.All(vm.MarketGainers, r => Assert.True(r.HasPrice));
        Assert.All(vm.MarketLosers, r => Assert.True(r.HasPrice));
        Assert.DoesNotContain(vm.MarketGainers, r => r.Change24h <= 0);
        Assert.DoesNotContain(vm.MarketLosers, r => r.Change24h >= 0);
    }

    /// <summary>Starring a coin stars the row and lists it; un-starring puts everything back.</summary>
    [Fact]
    public void Starring_a_coin_adds_it_to_the_watchlist_and_unstarring_removes_it()
    {
        var vm = NewViewModel();
        var btc = vm.Market.First(m => m.Symbol == "BTC");
        var wasWatched = btc.IsWatched;

        try
        {
            if (wasWatched) vm.ToggleWatchCommand.Execute("BTC");

            vm.ToggleWatchCommand.Execute("BTC");
            Assert.True(vm.Market.First(m => m.Symbol == "BTC").IsWatched);
            Assert.Contains(vm.MarketWatchlist, r => r.Symbol == "BTC");
            Assert.True(vm.HasWatchlist);

            vm.ToggleWatchCommand.Execute("BTC");
            Assert.False(vm.Market.First(m => m.Symbol == "BTC").IsWatched);
            Assert.DoesNotContain(vm.MarketWatchlist, r => r.Symbol == "BTC");
        }
        finally
        {
            // Leave the user's real settings exactly as they were.
            if (vm.Market.First(m => m.Symbol == "BTC").IsWatched != wasWatched)
                vm.ToggleWatchCommand.Execute("BTC");
        }
    }

    /// <summary>The star is case-insensitive: "btc" from a palette or a row must hit the same coin.</summary>
    [Fact]
    public void The_watchlist_is_case_insensitive()
    {
        var vm = NewViewModel();
        var wasWatched = vm.Market.First(m => m.Symbol == "LTC").IsWatched;

        try
        {
            if (wasWatched) vm.ToggleWatchCommand.Execute("LTC");
            vm.ToggleWatchCommand.Execute("ltc");

            Assert.True(vm.Market.First(m => m.Symbol == "LTC").IsWatched);
        }
        finally
        {
            if (vm.Market.First(m => m.Symbol == "LTC").IsWatched != wasWatched)
                vm.ToggleWatchCommand.Execute("LTC");
        }
    }

    [Fact]
    public void An_empty_symbol_changes_nothing()
    {
        var vm = NewViewModel();
        var before = vm.MarketWatchlist.Count;

        vm.ToggleWatchCommand.Execute("  ");

        Assert.Equal(before, vm.MarketWatchlist.Count);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); } catch { }
    }
}
