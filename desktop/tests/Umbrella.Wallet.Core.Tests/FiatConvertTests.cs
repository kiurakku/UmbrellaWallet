using Umbrella.Wallet.App;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the fiat→coin quick-entry maths used on the Send screen. It must parse locale-safely (so "0,5"
/// is a half, never five), guard a missing price, and format to the same 8-dp coin precision the send
/// field uses — the value it writes is what the user then confirms and signs.
/// </summary>
public sealed class FiatConvertTests
{
    [Fact]
    public void Divides_fiat_by_price_at_coin_precision()
    {
        Assert.Equal("0.001", FiatConvert.FiatToCoinAmount("50", 50_000m));   // $50 of a $50k coin
        Assert.Equal("0.00001282", FiatConvert.FiatToCoinAmount("1", 78_000m)); // rounds to 8 dp, trims
    }

    [Fact]
    public void No_usable_price_returns_empty()
    {
        Assert.Equal(string.Empty, FiatConvert.FiatToCoinAmount("50", 0m));
        Assert.Equal(string.Empty, FiatConvert.FiatToCoinAmount("50", -1m));
    }

    [Fact]
    public void An_empty_or_non_positive_amount_returns_empty()
    {
        Assert.Equal(string.Empty, FiatConvert.FiatToCoinAmount("", 100m));
        Assert.Equal(string.Empty, FiatConvert.FiatToCoinAmount("   ", 100m));
        Assert.Equal(string.Empty, FiatConvert.FiatToCoinAmount("abc", 100m));
        Assert.Equal(string.Empty, FiatConvert.FiatToCoinAmount("0", 100m));
    }

    [Fact]
    public void A_comma_decimal_is_not_misread_as_a_larger_number()
    {
        // The whole reason the send path uses AmountInput: "0,5" must be a half, not five. At $1 that is
        // 0.5 of the coin — a naive parse would send ten times too much.
        Assert.Equal("0.5", FiatConvert.FiatToCoinAmount("0,5", 1m));
    }
}
