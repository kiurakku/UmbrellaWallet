using Avalonia.Controls;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Seed;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// The market overview: movers and the local watchlist, from prices already fetched.
///
/// Split out of MainViewModel.cs (roadmap §8.3.1) as a partial class: the code is unchanged
/// and still one type, so nothing about behaviour moved with it — only the file it lives in.
/// </summary>
public partial class MainViewModel
{
    // ================= MARKET OVERVIEW =================
    // Movers and a watchlist computed from the prices the wallet has ALREADY fetched — no extra
    // endpoint, no extra request, nothing new leaving the device. A privacy wallet should not buy an
    // overview screen with more traffic.

    /// <summary>Biggest 24h risers among the coins that actually have a live price.</summary>
    public ObservableCollection<MarketRowViewModel> MarketGainers { get; } = [];

    /// <summary>Biggest 24h fallers.</summary>
    public ObservableCollection<MarketRowViewModel> MarketLosers { get; } = [];

    /// <summary>The user's starred coins, in the order the market list shows them.</summary>
    public ObservableCollection<MarketRowViewModel> MarketWatchlist { get; } = [];

    public bool HasMarketMovers => MarketGainers.Count > 0 || MarketLosers.Count > 0;
    public bool HasWatchlist => MarketWatchlist.Count > 0;

    private const int MoversShown = 5;

    private HashSet<string> LoadWatchlist() =>
        new((_uiSettings.Watchlist ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Stars or unstars a coin. Local only, persisted immediately so it survives a restart.</summary>
    [RelayCommand]
    private void ToggleWatch(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;
        var sym = symbol.Trim().ToUpperInvariant();

        var set = LoadWatchlist();
        if (!set.Remove(sym)) set.Add(sym);
        _uiSettings.Watchlist = string.Join(",", set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        _uiSettings.Save();

        ApplyWatchlist();
    }

    /// <summary>Re-stamps every market row with its watch state and rebuilds the overview lists.</summary>
    private void ApplyWatchlist()
    {
        var set = LoadWatchlist();
        for (var i = 0; i < Market.Count; i++)
        {
            var watched = set.Contains(Market[i].Symbol);
            if (Market[i].IsWatched != watched) Market[i] = Market[i] with { IsWatched = watched };
        }

        RebuildMarketOverview();
    }

    /// <summary>
    /// Rebuilds movers and the watchlist from the current rows. Coins without a live price are left
    /// out entirely rather than shown as a flat 0% — an unknown price is not "unchanged".
    /// </summary>
    private void RebuildMarketOverview()
    {
        var priced = Market.Where(m => m.HasPrice && m.Change24h != 0).ToList();

        MarketGainers.Clear();
        foreach (var row in priced.Where(m => m.Change24h > 0)
                     .OrderByDescending(m => m.Change24h).Take(MoversShown))
            MarketGainers.Add(row);

        MarketLosers.Clear();
        foreach (var row in priced.Where(m => m.Change24h < 0)
                     .OrderBy(m => m.Change24h).Take(MoversShown))
            MarketLosers.Add(row);

        MarketWatchlist.Clear();
        foreach (var row in Market.Where(m => m.IsWatched)) MarketWatchlist.Add(row);

        OnPropertyChanged(nameof(HasMarketMovers));
        OnPropertyChanged(nameof(HasWatchlist));
    }
}
