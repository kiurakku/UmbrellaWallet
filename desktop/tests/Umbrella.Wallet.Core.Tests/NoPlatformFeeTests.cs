using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Umbrella takes no cut of a send. This is a promise made in the README and the in-app guide, so it
/// is pinned here rather than left to a constant nobody re-reads: if someone sets the baked rate back
/// above zero, these fail and the promise cannot be broken silently.
/// </summary>
public sealed class NoPlatformFeeTests
{
    private static readonly string[] EveryChain =
    [
        "BTC", "LTC", "DOGE", "BCH", "ETH", "SOL", "TRX", "USDT", "XMR", "TON", "ADA",
        // the alias spellings QuoteFee canonicalises
        "TRON", "TRC20", "USDT-TRC20", "MONERO",
    ];

    [Fact]
    public void No_fee_is_quoted_on_any_chain_at_any_amount()
    {
        var fee = DeveloperFeeConfig.Load();

        foreach (var chain in EveryChain)
        {
            foreach (var amount in new[] { 0.00000001m, 0.5m, 1m, 1000m, 21_000_000m })
            {
                Assert.Null(fee.QuoteFee(chain, amount));
            }
        }
    }

    [Fact]
    public void The_advertised_rate_is_zero()
    {
        var fee = DeveloperFeeConfig.Load();
        Assert.Equal(0, fee.EffectiveBps);
        Assert.Equal(0m, fee.FeePercent);
    }

    [Fact]
    public void The_fee_amount_for_any_send_is_zero()
    {
        var fee = DeveloperFeeConfig.Load();
        Assert.Equal(0m, fee.FeeAmount(1m));
        Assert.Equal(0m, fee.FeeAmount(1_000_000m));
    }

    [Fact]
    public void The_hard_ceiling_still_exists_for_the_day_it_is_ever_switched_back_on()
    {
        // Switching the fee on must never be able to quote more than 2%, whatever value is typed in.
        Assert.Equal(200, DeveloperFeeConfig.MaxBps);
    }
}
