using Umbrella.Wallet.App;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the Send amount presets (25% / 50%). A preset is always a share of the balance — comfortably
/// below the fee-aware Max — formatted to the same coin precision the send field uses, and it yields
/// nothing (leaving the field untouched) when there is nothing to send or the percentage is out of range.
/// </summary>
public sealed class AmountPresetsTests
{
    [Fact]
    public void Takes_the_percentage_of_the_balance_at_coin_precision()
    {
        Assert.Equal("1", AmountPresets.Of(2m, 50));
        Assert.Equal("0.25", AmountPresets.Of(1m, 25));
        Assert.Equal("0.5", AmountPresets.Of(2m, 25));
        Assert.Equal("0.00000001", AmountPresets.Of(0.00000004m, 25)); // stays representable at 8 dp
    }

    [Fact]
    public void An_empty_balance_yields_nothing_so_the_field_is_left_untouched()
    {
        Assert.Equal(string.Empty, AmountPresets.Of(0m, 50));
        Assert.Equal(string.Empty, AmountPresets.Of(-1m, 50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    [InlineData(101)]
    public void An_out_of_range_percentage_yields_nothing(int percent) =>
        Assert.Equal(string.Empty, AmountPresets.Of(5m, percent));

    [Fact]
    public void One_hundred_percent_is_the_whole_balance()
    {
        // 100% is allowed (a caller may want it); Max is the fee-aware variant, this is the raw share.
        Assert.Equal("5", AmountPresets.Of(5m, 100));
    }
}
