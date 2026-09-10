using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the fee-level scaling (roadmap §3 / send functionality). The one rule that protects funds: no
/// level may ever fall below the chain's relay floor (a too-low fee strands a tx) or overpay past the
/// cap, and Standard must equal the rate the wallet used before the selector existed.
/// </summary>
public sealed class FeeLevelsTests
{
    [Fact]
    public void Standard_is_the_input_rate_unchanged()
    {
        // A user who never touches the selector must pay exactly today's fee.
        Assert.Equal(2.0, FeeLevels.Adjust(2.0, FeeLevel.Standard, 1.0, 200.0));
        Assert.Equal(37.5, FeeLevels.Adjust(37.5, FeeLevel.Standard, 1.0, 200.0));
    }

    [Fact]
    public void Economy_is_cheaper_and_priority_is_dearer_than_standard()
    {
        const double standard = 20.0;
        var economy = FeeLevels.Adjust(standard, FeeLevel.Economy, 1.0, 200.0);
        var priority = FeeLevels.Adjust(standard, FeeLevel.Priority, 1.0, 200.0);

        Assert.Equal(10.0, economy);   // ~half
        Assert.Equal(40.0, priority);  // ~double
        Assert.True(economy < standard && standard < priority);
    }

    [Fact]
    public void Economy_never_drops_below_the_relay_floor()
    {
        // Dogecoin's band floor is 1000 sat/vB; half of the floor rate is still clamped up to it, so a
        // cheap send can never be built below what nodes reliably accept.
        Assert.Equal(1000.0, FeeLevels.Adjust(1000.0, FeeLevel.Economy, 1000.0, 10000.0));
        // A 1 sat/vB Esplora floor: economy of the minimum stays at the minimum.
        Assert.Equal(1.0, FeeLevels.Adjust(1.0, FeeLevel.Economy, 1.0, 200.0));
    }

    [Fact]
    public void Priority_never_overpays_past_the_cap()
    {
        // Double of a rate already near the cap is clamped to the cap, never above it.
        Assert.Equal(200.0, FeeLevels.Adjust(150.0, FeeLevel.Priority, 1.0, 200.0));
        Assert.Equal(10000.0, FeeLevels.Adjust(8000.0, FeeLevel.Priority, 1000.0, 10000.0));
    }

    [Theory]
    [InlineData(FeeLevel.Economy)]
    [InlineData(FeeLevel.Standard)]
    [InlineData(FeeLevel.Priority)]
    public void Every_level_stays_inside_the_band(FeeLevel level)
    {
        foreach (var standard in new[] { 1.0, 2.0, 50.0, 199.0, 200.0 })
        {
            var rate = FeeLevels.Adjust(standard, level, 1.0, 200.0);
            Assert.InRange(rate, 1.0, 200.0);
        }
    }
}
