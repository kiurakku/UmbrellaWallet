using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The home screen's balance chart adds up today's holdings at each moment's price. Coins come from
/// sources that report different numbers of candles, so every series is resampled onto one count —
/// and that must never move a price: the first and last points are the series' own, and anything in
/// between lies on the line between two real candles.
/// </summary>
public sealed class BalanceChartTests
{
    [Fact]
    public void The_ends_of_a_resampled_series_are_its_own_first_and_last_prices()
    {
        double[] series = [10, 11, 13, 12, 20];
        Assert.Equal(10, MainViewModel.Resample(series, 0, 96));
        Assert.Equal(20, MainViewModel.Resample(series, 95, 96));
    }

    [Fact]
    public void Points_between_two_candles_lie_on_the_line_between_them()
    {
        double[] series = [100, 200];
        Assert.Equal(150, MainViewModel.Resample(series, 1, 3), 9);
        Assert.Equal(125, MainViewModel.Resample(series, 1, 5), 9);
    }

    [Fact]
    public void Resampling_never_goes_above_the_high_or_below_the_low()
    {
        double[] series = [5, 9, 1, 7, 3, 8];
        for (var i = 0; i < 96; i++)
        {
            var v = MainViewModel.Resample(series, i, 96);
            Assert.InRange(v, 1, 9);
        }
    }

    [Fact]
    public void Two_coins_with_different_candle_counts_add_up_moment_by_moment()
    {
        // BTC from 96 candles, a coin from CoinGecko with 7: both end at "now", both start at the window's
        // start, so the total's last point is exactly today's holdings at today's prices.
        var btc = Enumerable.Range(0, 96).Select(i => 50_000.0 + i).ToArray();
        double[] other = [1, 2, 3, 4, 5, 6, 7];

        var last = (0.5 * MainViewModel.Resample(btc, 95, 96)) + (10 * MainViewModel.Resample(other, 95, 96));
        Assert.Equal((0.5 * 50_095) + (10 * 7), last, 9);
    }
}
