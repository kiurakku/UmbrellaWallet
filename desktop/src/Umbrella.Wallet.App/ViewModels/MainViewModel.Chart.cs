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
/// The market detail chart: plot geometry, candles, the moving average and the crosshair.
///
/// Split out of MainViewModel.cs (roadmap §8.3.1) as a partial class: the code is unchanged
/// and still one type, so nothing about behaviour moved with it — only the file it lives in.
/// </summary>
public partial class MainViewModel
{
    // --- Detail chart geometry (exchange-style: gridded plot with labelled axes) ----
    // The grid is always five levels and five ticks, so their pixel positions are constants and
    // the view can place them directly. An ItemsControl over a Canvas does not position its
    // generated containers, which silently dropped the whole grid.
    private const double PlotLeft = 58;    // room for price labels
    private const double PlotRight = 860;
    private const double PlotTop = 10;
    private const double PlotBottom = 130; // room for time labels below

    /// <summary>Closed polygon under the price line, so the chart reads as an area not a wire.</summary>
    [ObservableProperty] private List<Avalonia.Point> _chartArea = [];
    /// <summary>Moving-average overlay for the market chart — a smooth trend line over the candles.</summary>
    [ObservableProperty] private List<Avalonia.Point> _chartMa = [];

    /// <summary>Candlesticks (real OHLC) drawn over the area — green up, red down, like a pro chart.</summary>
    [ObservableProperty] private List<CandleVm> _chartCandles = [];
    /// <summary>Volume bars along the bottom of the price chart (pro-terminal look).</summary>
    [ObservableProperty] private List<VolumeBarVm> _chartVolumeBars = [];

    // Price levels, top to bottom.
    [ObservableProperty] private string _chartLevel0 = string.Empty;
    [ObservableProperty] private string _chartLevel1 = string.Empty;
    [ObservableProperty] private string _chartLevel2 = string.Empty;
    [ObservableProperty] private string _chartLevel3 = string.Empty;
    [ObservableProperty] private string _chartLevel4 = string.Empty;

    // Time ticks, oldest to newest.
    [ObservableProperty] private string _chartTime0 = string.Empty;
    [ObservableProperty] private string _chartTime1 = string.Empty;
    [ObservableProperty] private string _chartTime2 = string.Empty;
    [ObservableProperty] private string _chartTime3 = string.Empty;
    [ObservableProperty] private string _chartTime4 = string.Empty;

    [ObservableProperty] private string _chartHigh = string.Empty;
    [ObservableProperty] private string _chartLow = string.Empty;

    // --- Chart view mode (Candles ⇄ Line), like a pro charting UI ---------------
    // Default to the clean Uniswap-style line/area; candlesticks are one tap away for pro users.
    [ObservableProperty] private string _chartViewMode = "Line";
    public bool IsCandleView => ChartViewMode == "Candles";
    public bool IsLineView => ChartViewMode == "Line";
    partial void OnChartViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsCandleView));
        OnPropertyChanged(nameof(IsLineView));
    }

    [RelayCommand]
    private void SetChartView(string? mode)
    {
        if (!string.IsNullOrWhiteSpace(mode)) ChartViewMode = mode;
    }

    // --- Change over the selected window (first→last close), shown in the header --
    [ObservableProperty] private string _chartChangeLabel = string.Empty;
    [ObservableProperty] private string _chartChangeColor = "#8A9099";

    /// <summary>Soft vertical gradient under the price line — the change colour fading to nothing,
    /// like Kraken/TradingView. Rebuilt each time a chart is drawn so it tracks the up/down colour.</summary>
    [ObservableProperty] private Avalonia.Media.IBrush _chartAreaBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Transparent);

    // --- Crosshair (hover) state, driven from the view code-behind ----------------
    [ObservableProperty] private bool _crosshairVisible;
    [ObservableProperty] private double _crosshairLineLeft;
    [ObservableProperty] private double _crosshairDotLeft;
    [ObservableProperty] private double _crosshairDotTop;
    [ObservableProperty] private double _crosshairLabelLeft;
    [ObservableProperty] private string _crosshairPrice = string.Empty;

    // The dot where the line ends, and its halo — positioned on the plot canvas.
    [ObservableProperty] private double _chartEndLeft;
    [ObservableProperty] private double _chartEndTop;
    [ObservableProperty] private double _chartEndHaloLeft;
    [ObservableProperty] private double _chartEndHaloTop;
    [ObservableProperty] private bool _hasChartEnd;
    [ObservableProperty] private string _crosshairTime = string.Empty;

    // Raw candles + scale of the open chart, so the crosshair can map pixels back to price/time.
    private IReadOnlyList<PriceCandle> _detailCandles = [];
    private double _chartMin;
    private double _chartMax;

    /// <summary>Bridges the provider's candle type to the pure aggregator in Core and back.</summary>
    private static IReadOnlyList<PriceCandle> Downsample(IReadOnlyList<PriceCandle> candles)
    {
        var thinned = Umbrella.Wallet.Core.Chart.CandleAggregator.Downsample(
            candles.Select(c => new Umbrella.Wallet.Core.Chart.Ohlc(c.Open, c.High, c.Low, c.Close, c.Volume)).ToList());

        return thinned.Count == candles.Count
            ? candles // untouched — avoid rebuilding an identical list
            : thinned.Select(o => new PriceCandle(o.Open, o.High, o.Low, o.Close, o.Volume)).ToList();
    }

    /// <summary>
    /// Turns a price series into a plotted chart: gridlines with price labels, time labels along
    /// the bottom, a stroked line and the filled area beneath it.
    /// </summary>
    private void BuildDetailChart(IReadOnlyList<PriceCandle> candles)
    {
        ChartArea = [];
        ChartPoints = [];
        HasChartEnd = false;
        ChartCandles = [];
        ChartMa = [];
        ChartVolumeBars = [];
        ChartHigh = ChartLow = string.Empty;
        CrosshairVisible = false;

        // A 24h window arrives as a few hundred fine-grained candles. Across a ~950px plot each body
        // came out about two pixels wide, so "Candles" drew a jagged line and the mode looked broken.
        // Merging adjacent candles is what a longer timeframe IS, so no price is invented or lost —
        // every source candle still sits inside the bar that represents it.
        candles = Downsample(candles);

        _detailCandles = candles;
        if (candles.Count < 2) return;

        var min = candles.Min(c => c.Low);
        var max = candles.Max(c => c.High);
        var range = max - min;
        // A dead-flat series would divide by zero; give it a nominal band so it renders centred.
        if (range <= 0) { min -= 1; max += 1; range = max - min; }
        _chartMin = min;
        _chartMax = max;

        var plotW = PlotRight - PlotLeft;
        var plotH = PlotBottom - PlotTop;
        double Y(double price) => PlotTop + (1 - (price - min) / range) * plotH;

        // Faint close-line + area behind the candles.
        var line = new List<Avalonia.Point>(candles.Count);
        for (var i = 0; i < candles.Count; i++)
        {
            var x = PlotLeft + plotW * i / (candles.Count - 1);
            line.Add(new Avalonia.Point(x, Y(candles[i].Close)));
        }

        ChartPoints = line;
        ChartArea = new List<Avalonia.Point>(line) { new(PlotRight, PlotBottom), new(PlotLeft, PlotBottom) };

        // The lit dot at the line's end (now): centred on the last close.
        var end = line[^1];
        ChartEndLeft = end.X - 5;
        ChartEndTop = end.Y - 5;
        ChartEndHaloLeft = end.X - 11;
        ChartEndHaloTop = end.Y - 11;
        HasChartEnd = true;

        // Moving-average overlay: a smooth trailing SMA of the closes, so the chart reads like a pro
        // trading view rather than a bare line. Period scales with the window (a few candles to ~3 weeks).
        var maPeriod = Math.Clamp(candles.Count / 6, 3, 21);
        var ma = new List<Avalonia.Point>(candles.Count);
        for (var i = 0; i < candles.Count; i++)
        {
            var start = Math.Max(0, i - maPeriod + 1);
            double sum = 0;
            var n = 0;
            for (var j = start; j <= i; j++) { sum += candles[j].Close; n++; }
            var x = PlotLeft + plotW * i / (candles.Count - 1);
            ma.Add(new Avalonia.Point(x, Y(sum / n)));
        }
        ChartMa = ma;

        // Candlesticks: green when close ≥ open, red otherwise (TradingView colours).
        var w = Math.Max(1.5, plotW / candles.Count * 0.62);
        var built = new List<CandleVm>(candles.Count);
        for (var i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            var xc = PlotLeft + plotW * (i + 0.5) / candles.Count;
            var yHigh = Y(c.High);
            var yLow = Y(c.Low);
            var bodyTop = Math.Min(Y(c.Open), Y(c.Close));
            built.Add(new CandleVm(
                xc - (w / 2), yHigh, w, Math.Max(1, yLow - yHigh),
                (w / 2) - 0.7, bodyTop - yHigh, Math.Max(1, Math.Abs(Y(c.Close) - Y(c.Open))),
                c.Close >= c.Open ? "#26A69A" : "#EF5350"));
        }

        ChartCandles = built;

        // Volume bars: a faint green/red strip along the bottom of the plot, scaled to the window's
        // peak volume — the pro-terminal cue that the price line alone doesn't give.
        var maxVol = candles.Max(cc => cc.Volume);
        var bars = new List<VolumeBarVm>(candles.Count);
        if (maxVol > 0)
        {
            const double band = 32; // px of the plot bottom the bars may reach
            for (var i = 0; i < candles.Count; i++)
            {
                var c = candles[i];
                if (c.Volume <= 0) continue;
                var xc = PlotLeft + plotW * (i + 0.5) / candles.Count;
                var barH = Math.Max(1.5, c.Volume / maxVol * band);
                bars.Add(new VolumeBarVm(xc - (w / 2), PlotBottom - barH, w, barH,
                    c.Close >= c.Open ? "#7026A69A" : "#70EF5350")); // ~44% alpha green/red, clearly visible
            }
        }
        ChartVolumeBars = bars;

        // Five price levels, top to bottom.
        ChartLevel0 = FormatPrice(max);
        ChartLevel1 = FormatPrice(max - range * 0.25);
        ChartLevel2 = FormatPrice(max - range * 0.5);
        ChartLevel3 = FormatPrice(max - range * 0.75);
        ChartLevel4 = FormatPrice(min);

        // Time axis derived from the selected window — the series is evenly spaced within it.
        var ticks = TimeAxisLabels(ChartRange);
        ChartTime0 = ticks[0];
        ChartTime1 = ticks[1];
        ChartTime2 = ticks[2];
        ChartTime3 = ticks[3];
        ChartTime4 = ticks[4];

        ChartHigh = $"H {FormatPrice(max)}";
        ChartLow = $"L {FormatPrice(min)}";

        // Change across the whole window (first open → last close), like the header on an exchange.
        var open0 = candles[0].Open != 0 ? candles[0].Open : candles[0].Close;
        var closeN = candles[^1].Close;
        var pct = open0 != 0 ? (closeN - open0) / open0 * 100 : 0;
        var up = pct >= 0;
        ChartIsUp = up;
        ChartChangeColor = up ? "#26A69A" : "#EF5350";
        // The price line + area must match the chart window's own direction, not the coin's 24h
        // change — otherwise a green (up-over-window) chart could draw a red line, which is bug #24.
        SelectedMarketChangeColor = ChartChangeColor;
        ChartChangeLabel = $"{(up ? "▲" : "▼")} {Math.Abs(pct):0.00}% · {ChartRange}";

        // Gradient fill under the line: change-colour → transparent, top to bottom.
        var baseColor = Avalonia.Media.Color.Parse(up ? "#26A69A" : "#EF5350");
        ChartAreaBrush = new Avalonia.Media.LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new Avalonia.Media.GradientStop(Avalonia.Media.Color.FromArgb(0x66, baseColor.R, baseColor.G, baseColor.B), 0),
                new Avalonia.Media.GradientStop(Avalonia.Media.Color.FromArgb(0x1F, baseColor.R, baseColor.G, baseColor.B), 0.55),
                new Avalonia.Media.GradientStop(Avalonia.Media.Color.FromArgb(0x00, baseColor.R, baseColor.G, baseColor.B), 1),
            },
        };
    }

    /// <summary>Called from the view as the pointer moves over the chart: snaps to the nearest candle
    /// and updates the crosshair line, dot and floating price/time readout.</summary>
    public void UpdateCrosshair(double canvasX)
    {
        var n = _detailCandles.Count;
        if (n < 2) { CrosshairVisible = false; return; }

        var plotW = PlotRight - PlotLeft;
        var plotH = PlotBottom - PlotTop;
        var range = _chartMax - _chartMin;
        if (range <= 0) { CrosshairVisible = false; return; }

        var frac = Math.Clamp((canvasX - PlotLeft) / plotW, 0, 1);
        var i = Math.Clamp((int)Math.Round(frac * (n - 1)), 0, n - 1);
        var c = _detailCandles[i];

        var x = PlotLeft + plotW * i / (double)(n - 1);
        var y = PlotTop + (1 - (c.Close - _chartMin) / range) * plotH;

        CrosshairLineLeft = x;
        CrosshairDotLeft = x - 4;
        CrosshairDotTop = y - 4;
        CrosshairLabelLeft = Math.Clamp(x - 62, PlotLeft, PlotRight - 124);
        CrosshairPrice = FormatPrice(c.Close);
        CrosshairTime = CrosshairTimeLabel(i, n);
        CrosshairVisible = true;
    }

    public void HideCrosshair() => CrosshairVisible = false;

    /// <summary>Approximate wall-clock label for a hovered candle: the window is evenly spaced, so
    /// candle i of n maps to now − span·(1 − i/(n−1)).</summary>
    private string CrosshairTimeLabel(int i, int n)
    {
        var frac = (double)i / (n - 1);
        var span = ChartRange switch
        {
            "1H" => TimeSpan.FromHours(1),
            "24H" => TimeSpan.FromHours(24),
            "7D" => TimeSpan.FromDays(7),
            "30D" => TimeSpan.FromDays(30),
            _ => TimeSpan.FromDays(365),
        };
        var t = DateTime.Now - TimeSpan.FromTicks((long)(span.Ticks * (1 - frac)));
        // Month names follow the wallet's language, like every other date it shows. On InvariantCulture
        // the crosshair read "Sep 10" while the Activity feed right next to it said "вер. 10".
        // "HH:mm" carries no words, so it is the same either way.
        return ChartRange switch
        {
            "1H" or "24H" => t.ToString("HH:mm", Fx.Culture),
            "7D" or "30D" => t.ToString("MMM d · HH:mm", Fx.Culture),
            _ => t.ToString("MMM d, yyyy", Fx.Culture),
        };
    }

    /// <summary>Price-axis labels, in the same locale as every other money figure. They were
    /// InvariantCulture, so the axis read "3 519 010.80" while the 24h-high card directly beneath it
    /// read "₴3 519 010,80".</summary>
    private static string FormatPrice(double value) => value switch
    {
        >= 1000 => value.ToString("N0", Fx.Culture),
        >= 1 => value.ToString("N2", Fx.Culture),
        _ => value.ToString("N6", Fx.Culture),
    };

    /// <summary>Evenly spaced ticks labelled for the selected window, oldest on the left.</summary>
    private static string[] TimeAxisLabels(string range) => range switch
    {
        "1H" => ["-60m", "-45m", "-30m", "-15m", "now"],
        "24H" => ["-24h", "-18h", "-12h", "-6h", "now"],
        "7D" => ["-7d", "-5d", "-3d", "-2d", "now"],
        "30D" => ["-30d", "-22d", "-15d", "-7d", "now"],
        _ => ["-1y", "-9m", "-6m", "-3m", "now"],
    };

    /// <summary>Free-text Market filter — matches ticker or name. Drives per-row visibility via
    /// <see cref="MarketFilterConverter"/>, so live price updates (indexed by symbol) are untouched.</summary>
    [ObservableProperty] private string _marketQuery = string.Empty;

    [RelayCommand]
    private void ClearMarketQuery() => MarketQuery = string.Empty;

    /// <summary>Selected chart window. Changing it reloads every chart at the new resolution.</summary>
    [ObservableProperty] private string _chartRange = "24H";

    public IReadOnlyList<string> ChartRanges => PublicMarketRatesClient.ChartRanges;

    [RelayCommand]
    private async Task SelectChartRangeAsync(string? range)
    {
        if (string.IsNullOrWhiteSpace(range) || range == ChartRange) return;
        ChartRange = range;
        OnPropertyChanged(nameof(IsRange1H));
        OnPropertyChanged(nameof(IsRange24H));
        OnPropertyChanged(nameof(IsRange7D));
        OnPropertyChanged(nameof(IsRange30D));
        OnPropertyChanged(nameof(IsRange1Y));

        // Only the open coin's chart is on screen, so refresh just that — one fetch, instant. The
        // row sparklines for every other coin refresh quietly in the background and never hold up the
        // range switch (they were what made changing the window feel slow).
        if (HasChart)
        {
            var open = Market.FirstOrDefault(m => m.Symbol == SelectedMarketSymbol);
            if (open is not null) await SelectMarketCoinAsync(open);
        }

        _ = LoadSparklinesAsync(force: true);
    }

    public bool IsRange1H => ChartRange == "1H";
    public bool IsRange24H => ChartRange == "24H";
    public bool IsRange7D => ChartRange == "7D";
    public bool IsRange30D => ChartRange == "30D";
    public bool IsRange1Y => ChartRange == "1Y";

    [RelayCommand]
    private void CloseChart()
    {
        HasChart = false;
        CrosshairVisible = false;
        _detailCandles = [];
        ChartPoints = new System.Collections.Generic.List<Avalonia.Point>();
        HasChartEnd = false;
    }

    private const double ChartWidth = 620;
    private const double ChartHeight = 150;

    /// <summary>Scales a price series into polyline points inside the chart box (top-left origin).</summary>
    private static System.Collections.Generic.List<Avalonia.Point> BuildChartPoints(
        System.Collections.Generic.IReadOnlyList<double> series, double width, double height)
    {
        var points = new System.Collections.Generic.List<Avalonia.Point>();
        if (series.Count < 2) return points;

        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in series)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        var range = max - min;
        const double pad = 10;
        var usableH = height - 2 * pad;
        for (var i = 0; i < series.Count; i++)
        {
            var x = width * i / (series.Count - 1);
            // Flat series → draw a centred line rather than dividing by zero.
            var norm = range > 0 ? (series[i] - min) / range : 0.5;
            var y = pad + (1 - norm) * usableH;
            points.Add(new Avalonia.Point(x, y));
        }

        return points;
    }
}
