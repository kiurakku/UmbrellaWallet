using Umbrella.Wallet.Core.Chart;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Merging candles must not invent or lose price. A chart is something people make decisions on, so
/// the aggregate has to be a faithful longer-timeframe view of the same data: the first open, the last
/// close, and extremes that still bound every source candle.
/// </summary>
public sealed class CandleAggregatorTests
{
    private static IReadOnlyList<Ohlc> Series(int n)
    {
        var list = new List<Ohlc>(n);
        for (var i = 0; i < n; i++)
        {
            double b = 100 + i;
            list.Add(new Ohlc(b, b + 2, b - 2, b + 1, 10));
        }

        return list;
    }

    [Fact]
    public void A_dense_series_is_thinned_to_the_target()
    {
        var thinned = CandleAggregator.Downsample(Series(288), target: 80);
        Assert.True(thinned.Count <= 80, $"expected at most 80 bars, got {thinned.Count}");
        Assert.True(thinned.Count > 40, $"thinned too aggressively: {thinned.Count}");
    }

    [Fact]
    public void A_series_already_short_enough_is_returned_untouched()
    {
        var original = Series(50);
        Assert.Same(original, CandleAggregator.Downsample(original, target: 80));
    }

    [Fact]
    public void The_merged_series_keeps_the_first_open_and_the_last_close()
    {
        var source = Series(288);
        var thinned = CandleAggregator.Downsample(source, target: 80);

        Assert.Equal(source[0].Open, thinned[0].Open);
        Assert.Equal(source[^1].Close, thinned[^1].Close);
    }

    [Fact]
    public void No_price_escapes_the_merged_high_and_low()
    {
        // The strongest guarantee: every source candle stays inside the bar that represents it, so the
        // chart can never show a range narrower than what actually happened.
        var source = Series(288);
        var thinned = CandleAggregator.Downsample(source, target: 80);

        Assert.Equal(source.Max(c => c.High), thinned.Max(c => c.High));
        Assert.Equal(source.Min(c => c.Low), thinned.Min(c => c.Low));
    }

    [Fact]
    public void Volume_is_summed_not_averaged()
    {
        var source = Series(288);
        var thinned = CandleAggregator.Downsample(source, target: 80);
        Assert.Equal(source.Sum(c => c.Volume), thinned.Sum(c => c.Volume), 6);
    }

    [Fact]
    public void A_volatile_group_keeps_its_extremes()
    {
        // A spike inside a group must survive the merge — losing it would hide exactly the moment a
        // trader is looking for.
        var source = new List<Ohlc>
        {
            new(100, 101, 99, 100),
            new(100, 500, 1, 100),   // the spike
            new(100, 101, 99, 102),
        };

        var merged = CandleAggregator.Downsample(source, target: 1);

        Assert.Single(merged);
        Assert.Equal(100, merged[0].Open);
        Assert.Equal(102, merged[0].Close);
        Assert.Equal(500, merged[0].High);
        Assert.Equal(1, merged[0].Low);
    }

    [Fact]
    public void Empty_and_degenerate_input_is_handled()
    {
        Assert.Empty(CandleAggregator.Downsample([], 80));
        Assert.Single(CandleAggregator.Downsample(Series(1), 80));
        Assert.Single(CandleAggregator.Downsample(Series(10), target: 0)); // target clamped to 1
    }
}
