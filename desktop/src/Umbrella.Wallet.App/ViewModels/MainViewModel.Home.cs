using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>A theme offered as a swatch on the home screen.</summary>
public sealed record ThemeSwatch(string Id, string Name, IBrush Fill, IBrush Accent, bool IsActive)
{
    /// <summary>The outline: the theme's own accent on the theme in use, a faint hairline otherwise.</summary>
    public IBrush Edge => IsActive ? Accent : new SolidColorBrush(Color.Parse("#2EFFFFFF"));
}

/// <summary>
/// The home screen of the gold design: the balance chart, the Market card's tabs, the theme swatches,
/// and whether the window is wide enough to put the assets and the transactions side by side.
/// </summary>
public partial class MainViewModel
{
    // ================= layout =================

    /// <summary>The window's width, reported by the view. Only layout reads it.</summary>
    [ObservableProperty] private double _windowWidth = 1240;

    /// <summary>Assets and recent transactions side by side once there is room, stacked otherwise.</summary>
    public int HomeColumns => WindowWidth >= 1380 && !MobileMode ? 2 : 1;

    partial void OnWindowWidthChanged(double value) => OnPropertyChanged(nameof(HomeColumns));

    // ================= the balance chart =================

    /// <summary>The chart's window: 1D, 1W, 1M or 1Y.</summary>
    [ObservableProperty] private string _portfolioRange = "1D";

    /// <summary>What today's holdings were worth across the window, oldest first, in USD.</summary>
    [ObservableProperty] private IReadOnlyList<double> _portfolioSeries = [];

    /// <summary>The line under the chart: what it is, and what it leaves out.</summary>
    [ObservableProperty] private string _portfolioChartNote = string.Empty;

    /// <summary>Shown in place of the chart when there is nothing to draw, and why.</summary>
    [ObservableProperty] private string _portfolioChartStatus = string.Empty;

    public bool HasPortfolioSeries => PortfolioSeries.Count > 1;

    partial void OnPortfolioSeriesChanged(IReadOnlyList<double> value) => OnPropertyChanged(nameof(HasPortfolioSeries));

    /// <summary>The label at the chart's lit end: the balance now, or dots while it is hidden.</summary>
    public string HeroEndLabel => IsBalanceHidden ? "•••••" : $"{CurrencySymbol}{TotalBalanceMain}{BalanceDisplayCents}";

    /// <summary>The market window each chart window reads its prices from.</summary>
    private static string MarketRangeFor(string range) => range switch
    {
        "1W" => "7D",
        "1M" => "30D",
        "1Y" => "1Y",
        _ => "24H",
    };

    /// <summary>Points on the chart. Every price series is resampled onto the same count, so coins
    /// whose sources report different numbers of candles still add up moment by moment.</summary>
    private const int PortfolioPoints = 96;

    /// <summary>Price history per market window, per coin, and when it was fetched.</summary>
    private readonly Dictionary<string, (DateTimeOffset At, Dictionary<string, IReadOnlyList<double>> Series)> _seriesByRange =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan SeriesReuse = TimeSpan.FromMinutes(10);

    private int _portfolioChartVersion;

    [RelayCommand]
    private async Task SetPortfolioRange(string? range)
    {
        if (string.IsNullOrWhiteSpace(range) || range == PortfolioRange) return;
        PortfolioRange = range;
        await RefreshPortfolioChartAsync();
    }

    /// <summary>Kept by <see cref="LoadSparklinesAsync"/>, so the chart reuses what the market list already fetched.</summary>
    private void RememberSeries(string marketRange, string symbol, IReadOnlyList<double> series)
    {
        if (!_seriesByRange.TryGetValue(marketRange, out var entry))
        {
            entry = (DateTimeOffset.UtcNow, new Dictionary<string, IReadOnlyList<double>>(StringComparer.OrdinalIgnoreCase));
            _seriesByRange[marketRange] = entry;
        }

        entry.Series[symbol] = series;
    }

    /// <summary>
    /// Price history for EVERY coin in the market list, whatever this wallet holds. Asking only for the
    /// coins you own would tell the price server which coins those are; asking for the whole list tells
    /// it nothing it could not see from any other copy of this wallet.
    /// </summary>
    private async Task<Dictionary<string, IReadOnlyList<double>>> MarketSeriesAsync(string marketRange)
    {
        if (_seriesByRange.TryGetValue(marketRange, out var cached) &&
            DateTimeOffset.UtcNow - cached.At < SeriesReuse &&
            Market.All(m => cached.Series.ContainsKey(m.Symbol) || !m.HasPrice))
        {
            return cached.Series;
        }

        var fetched = cached.Series is { } previous
            ? new Dictionary<string, IReadOnlyList<double>>(previous, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, IReadOnlyList<double>>(StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in Market.Select(m => m.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (fetched.ContainsKey(symbol)) continue;
            try
            {
                var series = await _rates.GetPriceSeriesAsync(symbol, marketRange, CancellationToken.None);
                if (series.Count > 1) fetched[symbol] = series;
            }
            catch
            {
                // A coin without history is left out of the chart and named under it — never guessed.
            }

            await Task.Delay(120);
        }

        _seriesByRange[marketRange] = (DateTimeOffset.UtcNow, fetched);
        return fetched;
    }

    /// <summary>
    /// Today's holdings valued at each moment's price across the window. This is not a record of past
    /// balances — the wallet keeps none — and the note under the chart says so.
    /// </summary>
    private async Task RefreshPortfolioChartAsync()
    {
        var version = Interlocked.Increment(ref _portfolioChartVersion);

        var holdings = Accounts
            .Where(a => a.SupportStatus is "Ready" or "Watch" or "Exchange" or "Receive only"
                        && !a.IsSuspectedSpam && a.Balance != BalanceRead.Unknown && a.Amount > 0)
            .GroupBy(a => a.Symbol.ToUpperInvariant())
            .Select(g => (Symbol: g.Key, Amount: g.Sum(a => a.Amount)))
            .ToList();

        if (holdings.Count == 0)
        {
            PortfolioSeries = [];
            PortfolioChartStatus = Loc.Instance["chart.empty"];
            PortfolioChartNote = string.Empty;
            return;
        }

        if (!HasPortfolioSeries) PortfolioChartStatus = Loc.Instance["chart.loading"];

        var series = await MarketSeriesAsync(MarketRangeFor(PortfolioRange));
        if (version != _portfolioChartVersion) return;   // a newer request superseded this one

        var total = new double[PortfolioPoints];
        var leftOut = new List<string>();
        foreach (var (symbol, amount) in holdings)
        {
            if (!series.TryGetValue(symbol, out var prices) || prices.Count < 2)
            {
                leftOut.Add(symbol);
                continue;
            }

            for (var i = 0; i < PortfolioPoints; i++) total[i] += amount * Resample(prices, i, PortfolioPoints);
        }

        if (leftOut.Count == holdings.Count)
        {
            PortfolioSeries = [];
            PortfolioChartStatus = Loc.Instance["chart.noHistory"];
            PortfolioChartNote = string.Empty;
            return;
        }

        PortfolioSeries = total;
        PortfolioChartStatus = string.Empty;
        PortfolioChartNote = leftOut.Count == 0
            ? Loc.Instance["chart.note"]
            : string.Format(Loc.Instance["chart.noteLeftOut"], string.Join(", ", leftOut));
    }

    /// <summary>The value at point <paramref name="i"/> of <paramref name="count"/>, read linearly off a
    /// series of any length. The first and last points are the series' own first and last values.</summary>
    internal static double Resample(IReadOnlyList<double> series, int i, int count)
    {
        if (series.Count == 1 || count < 2) return series[^1];
        var position = (double)i * (series.Count - 1) / (count - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, series.Count - 1);
        var t = position - lower;
        return series[lower] + ((series[upper] - series[lower]) * t);
    }

    /// <summary>Holdings changed: redraw, reusing the price history already fetched.</summary>
    private string _portfolioChartSignature = string.Empty;

    private void SchedulePortfolioChart()
    {
        var signature = string.Join(";", Accounts
            .Where(a => a.Amount > 0 && a.Balance != BalanceRead.Unknown)
            .Select(a => $"{a.Symbol}:{a.Amount:R}")
            .OrderBy(s => s, StringComparer.Ordinal)) + "|" + PortfolioRange;
        if (signature == _portfolioChartSignature) return;
        _portfolioChartSignature = signature;
        _ = RefreshPortfolioChartAsync();
    }

    // ================= the Market card =================

    /// <summary>Top (the market list's own order), Gainers or Losers.</summary>
    [ObservableProperty] private string _railMarketTab = "Top";

    public ObservableCollection<MarketRowViewModel> RailMarketRows { get; } = [];

    private const int RailMarketShown = 6;

    [RelayCommand]
    private void SetRailMarketTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab)) return;
        RailMarketTab = tab;
        RebuildRailMarket();
    }

    private void RebuildRailMarket()
    {
        var rows = RailMarketTab switch
        {
            "Gainers" => MarketGainers.ToList(),
            "Losers" => MarketLosers.ToList(),
            _ => Market.Where(m => m.HasPrice).Take(RailMarketShown).ToList(),
        };

        RailMarketRows.Clear();
        foreach (var row in rows.Take(RailMarketShown)) RailMarketRows.Add(row);
    }

    // ================= theme swatches =================

    /// <summary>The four themes offered on the home screen; every other one is in Settings.</summary>
    private static readonly string[] SwatchThemes = ["umbrella", "navy", "kraken", "black"];

    public IReadOnlyList<ThemeSwatch> ThemeSwatches => SwatchThemes
        .Where(Theming.IsKnown)
        .Select(id =>
        {
            var p = Theming.PaletteOf(id)!;
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(p["UmAccentDim"]), 0),
                    new GradientStop(Color.Parse(p["UmBg"]), 0.75),
                },
            };
            var name = Theming.Themes.First(t => t.Id == id).Name;
            return new ThemeSwatch(id, name, fill, new SolidColorBrush(Color.Parse(p["UmAccentBright"])), id == ThemeId);
        })
        .ToList();

    [RelayCommand]
    private void SelectThemeSwatch(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        ThemeId = id;
        OnPropertyChanged(nameof(ThemeSwatches));
    }
}
