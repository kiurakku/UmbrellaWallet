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

    // --- The reverse direction, used for the "≈ $42.10" hint beside a coin amount (Receive screen). ---

    [Fact]
    public void Multiplies_coin_by_price_at_cent_precision()
    {
        Assert.Equal("42.10", FiatConvert.CoinToFiatText("0.001", 42_100m));
        Assert.Equal("1.00", FiatConvert.CoinToFiatText("1", 1m));
    }

    [Fact]
    public void A_sub_cent_value_keeps_precision_instead_of_showing_zero()
    {
        // Rounding to "0.00" would tell the user their requested amount is worth nothing. A real value is
        // never displayed as zero.
        Assert.Equal("0.005", FiatConvert.CoinToFiatText("0.005", 1m));
        Assert.Equal("0.0001", FiatConvert.CoinToFiatText("0.0001", 1m));
    }

    [Fact]
    public void Reverse_conversion_guards_missing_price_and_bad_input()
    {
        Assert.Equal(string.Empty, FiatConvert.CoinToFiatText("1", 0m));
        Assert.Equal(string.Empty, FiatConvert.CoinToFiatText("1", -5m));
        Assert.Equal(string.Empty, FiatConvert.CoinToFiatText("", 100m));
        Assert.Equal(string.Empty, FiatConvert.CoinToFiatText("abc", 100m));
        Assert.Equal(string.Empty, FiatConvert.CoinToFiatText("0", 100m));
        Assert.Equal(string.Empty, FiatConvert.CoinToFiatText(null, 100m));
    }

    [Fact]
    public void Reverse_conversion_is_locale_safe_too()
    {
        // "0,5" of a $100 coin is $50 — a naive parse would claim $500.
        Assert.Equal("50.00", FiatConvert.CoinToFiatText("0,5", 100m));
    }

    [Fact]
    public void Round_trips_back_to_the_typed_fiat_amount()
    {
        // What the Receive screen does: USD in → coin field → USD hint. The user must see the number back.
        var coin = FiatConvert.FiatToCoinAmount("25", 2_000m);
        Assert.Equal("0.0125", coin);
        Assert.Equal("25.00", FiatConvert.CoinToFiatText(coin, 2_000m));
    }
}
