namespace Umbrella.Wallet.Core.Chart;

/// <summary>One OHLC bar, independent of any price provider so the maths can be tested offline.</summary>
public readonly record struct Ohlc(double Open, double High, double Low, double Close, double Volume = 0);

/// <summary>
/// Thins a candle series so the bars are wide enough to read as candles.
///
/// A 24-hour window comes back as a few hundred fine-grained candles. Drawn across a chart that is
/// only ~950px wide, each body is about two pixels — so "Candles" rendered as a jagged line and the
/// mode looked broken even though it was working exactly as written.
///
/// Aggregating is the honest fix: merging N adjacent candles into one is what a longer timeframe IS
/// (open of the first, close of the last, the extremes across the group), so nothing is invented and
/// no price is lost — the high and low of every source candle still bound the merged bar.
/// </summary>
public static class CandleAggregator
{
    /// <summary>Bars wide enough to show a body and both wicks at a typical chart width.</summary>
    public const int DefaultTarget = 80;

    /// <summary>
    /// Returns at most <paramref name="target"/> candles, merging adjacent ones in equal-sized groups.
    /// A series already at or under the target is returned unchanged.
    /// </summary>
    public static IReadOnlyList<Ohlc> Downsample(IReadOnlyList<Ohlc> candles, int target = DefaultTarget)
    {
        if (candles is null || candles.Count == 0) return [];
        if (target < 1) target = 1;
        if (candles.Count <= target) return candles;

        // Ceiling division: the group size that brings the count to target or just under it.
        var groupSize = (candles.Count + target - 1) / target;

        var merged = new List<Ohlc>((candles.Count / groupSize) + 1);
        for (var start = 0; start < candles.Count; start += groupSize)
        {
            var end = Math.Min(start + groupSize, candles.Count);

            var open = candles[start].Open;
            var close = candles[end - 1].Close;
            var high = double.NegativeInfinity;
            var low = double.PositiveInfinity;
            double volume = 0;

            for (var i = start; i < end; i++)
            {
                var c = candles[i];
                if (c.High > high) high = c.High;
                if (c.Low < low) low = c.Low;
                volume += c.Volume;
            }

            merged.Add(new Ohlc(open, high, low, close, volume));
        }

        return merged;
    }
}
