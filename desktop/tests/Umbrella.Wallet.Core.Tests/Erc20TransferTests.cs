using System.Numerics;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// ERC-20 transfer calldata. Two mistakes here move the wrong amount of somebody's money:
///
/// Decimals. USDT has 6, most tokens have 18. Reading "1" with the wrong decimals is wrong by a
/// factor of a trillion — dust in one direction, the entire balance in the other.
///
/// Precision. An amount finer than the token can represent must be refused rather than rounded:
/// rounding down loses the remainder silently, rounding up spends more than the user agreed to.
///
/// The selector is checked against the value fixed by the ERC-20 standard, not against this code's
/// own output.
/// </summary>
public sealed class Erc20TransferTests
{
    // Vitalik's address, used purely as a well-known 20-byte value.
    private const string Recipient = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";

    [Fact]
    public void The_selector_is_the_one_fixed_by_the_standard()
    {
        // First four bytes of keccak256("transfer(address,uint256)"). Every ERC-20 on every EVM chain
        // uses this exact value; if it changed, every transfer would call something else entirely.
        Assert.Equal("a9059cbb", Erc20Transfer.TransferSelector);
    }

    [Fact]
    public void Calldata_is_the_selector_then_two_padded_words()
    {
        var data = Erc20Transfer.EncodeCallData(Recipient, new BigInteger(1_000_000));

        Assert.StartsWith("0xa9059cbb", data, StringComparison.Ordinal);
        Assert.Equal(2 + 8 + 64 + 64, data.Length);

        // The recipient occupies the low 20 bytes of the first word, left-padded with zeros.
        Assert.Equal(
            "000000000000000000000000d8da6bf26964af9d7eed9e03e53415d37aa96045",
            data.Substring(10, 64));

        // 1_000_000 = 0xF4240
        Assert.Equal(
            "00000000000000000000000000000000000000000000000000000000000f4240",
            data.Substring(74, 64));
    }

    // --- decimals: the expensive mistake ---------------------------------------------------------

    [Theory]
    // USDT / USDC: 6 decimals. One dollar is 1_000_000 base units, not 10^18.
    [InlineData("1", 6, "1000000")]
    [InlineData("0.000001", 6, "1")]
    [InlineData("1234.567891", 6, "1234567891")]
    // Most tokens: 18 decimals.
    [InlineData("1", 18, "1000000000000000000")]
    [InlineData("0.000000000000000001", 18, "1")]
    // Tokens with no decimals at all exist.
    [InlineData("5", 0, "5")]
    public void An_amount_converts_exactly_for_the_token_it_belongs_to(string amount, int decimals, string expected)
    {
        var units = Erc20Transfer.ToBaseUnits(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), decimals);
        Assert.Equal(BigInteger.Parse(expected), units);
    }

    [Fact]
    public void The_same_number_means_wildly_different_amounts_on_different_tokens()
    {
        // This is the whole reason decimals are carried explicitly rather than assumed.
        var asUsdt = Erc20Transfer.ToBaseUnits(1m, 6);
        var asEighteen = Erc20Transfer.ToBaseUnits(1m, 18);

        Assert.Equal(BigInteger.Pow(10, 12), asEighteen / asUsdt);
    }

    [Fact]
    public void An_amount_finer_than_the_token_is_refused_not_rounded()
    {
        // 1.9999999 USDT needs 7 decimals; USDT has 6. Truncating would silently drop the last digit.
        var ex = Assert.Throws<ArgumentException>(() => Erc20Transfer.ToBaseUnits(1.9999999m, 6));
        Assert.Contains("6 decimal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trailing_zeros_beyond_the_token_precision_are_fine()
    {
        // "1.500000000" on a 6-decimal token is still exactly 1.5 — no information is lost, so it is
        // accepted rather than refused on a technicality.
        Assert.Equal(new BigInteger(1_500_000), Erc20Transfer.ToBaseUnits(1.500000000m, 6));
    }

    [Fact]
    public void A_huge_supply_token_does_not_overflow_on_the_way_to_base_units()
    {
        // A trillion SHIB at 18 decimals is 10^30 base units — far past what decimal can hold, which
        // is why the conversion works in integer arithmetic rather than by multiplying a decimal.
        var units = Erc20Transfer.ToBaseUnits(1_000_000_000_000m, 18);

        Assert.Equal(BigInteger.Pow(10, 30), units);
        Assert.True(units < Erc20Transfer.MaxUint256);
    }

    [Fact]
    public void Base_units_round_trip_back_to_the_amount()
    {
        foreach (var (amount, decimals) in new[]
                 {
                     (1m, 6), (0.000001m, 6), (1234.567891m, 6),
                     (1m, 18), (42.5m, 18), (7m, 0),
                 })
        {
            var units = Erc20Transfer.ToBaseUnits(amount, decimals);
            Assert.Equal(amount, Erc20Transfer.FromBaseUnits(units, decimals));
        }
    }

    // --- refusals ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_amount_is_refused(int amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Erc20Transfer.ToBaseUnits(amount, 6));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x")]
    [InlineData("not an address")]
    [InlineData("0xd8dA6BF26964aF9D7eEd9e03E53415D37aA960")]      // too short
    [InlineData("0xd8dA6BF26964aF9D7eEd9e03E53415D37aA9604567")]  // too long
    [InlineData("0xZZda6bf26964af9d7eed9e03e53415d37aa96045")]    // not hex
    public void A_recipient_that_is_not_an_address_is_refused(string recipient)
    {
        Assert.Throws<ArgumentException>(() => Erc20Transfer.EncodeCallData(recipient, new BigInteger(1)));
    }

    [Fact]
    public void An_amount_beyond_uint256_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Erc20Transfer.EncodeCallData(Recipient, Erc20Transfer.MaxUint256 + 1));
    }

    [Fact]
    public void The_maximum_uint256_still_encodes()
    {
        var data = Erc20Transfer.EncodeCallData(Recipient, Erc20Transfer.MaxUint256);
        Assert.EndsWith(new string('f', 64), data, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_address_is_accepted_with_or_without_the_prefix_and_in_any_case()
    {
        var a = Erc20Transfer.EncodeCallData(Recipient, new BigInteger(1));
        var b = Erc20Transfer.EncodeCallData(Recipient[2..], new BigInteger(1));
        var c = Erc20Transfer.EncodeCallData(Recipient.ToUpperInvariant().Replace("0X", "0x"), new BigInteger(1));

        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void The_gas_limit_leaves_room_for_contract_execution()
    {
        // A token transfer runs contract code; the flat 21,000 of a coin send would run out and strand
        // the transfer. Unused gas is refunded, so erring high costs nothing.
        Assert.True(Erc20Transfer.DefaultGasLimit > 21_000);
    }
}
