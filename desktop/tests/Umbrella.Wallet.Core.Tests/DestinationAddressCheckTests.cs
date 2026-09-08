using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Sending to an address on the wrong chain is unrecoverable, so the shape check that warns about it
/// gets a real matrix: every chain accepts its own address forms, rejects the obvious foreign ones,
/// and stays silent where the wallet has no rule (it must never cry "wrong network" on a guess).
///
/// These rules were lifted out of the view model unchanged (roadmap §8.3.2); the tests pin them so a
/// later tidy-up cannot quietly loosen or invert one.
/// </summary>
public sealed class DestinationAddressCheckTests
{
    // Real-shaped mainnet addresses (public destinations only — nothing here is spendable by us).
    private const string Btc = "bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq";
    private const string BtcLegacy = "1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2";
    private const string BtcP2sh = "3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy";
    private const string Ltc = "ltc1qhzjptwpjy0urqvfr0dgqgqfnjmvhq0z2z6f6df";
    private const string Doge = "D6MiH8HdqpNuGPXRn6JXjk4z2Mt3dN3VFy";
    private const string Eth = "0x742d35Cc6634C0532925a3b844Bc454e4438f44e";
    private const string Tron = "TQ5NMqJjCJ4qLGYQ8FvNRk3Kzm3ScTFNVK";
    private const string Sol = "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM";
    private const string Ton = "UQAvDfWFG0oYX19jwNDNBBL1rKNT9UXMup2uAxSCmqEEIY68";
    private const string Ada = "addr1qx2fxv2umyhttkxyxp8x0dlpdt3k6cwng5pxj3jhsydzer3n0d3vllmyqwsx5wktcd8cc3sq835lu7drv2xwl2wywfgse35a3x";
    private const string Xmr = "44AFFq5kSiGBoZ4NMDwYtN18obc8AemS33DBLWs3H7otXft3XjrpDtQGv7SqSsaBYBb98uNbr2VBBEt7f2wfn3RVGQBEP3A";

    [Theory]
    [InlineData("BTC", Btc)]
    [InlineData("BTC", BtcLegacy)]
    [InlineData("BTC", BtcP2sh)]
    [InlineData("LTC", Ltc)]
    [InlineData("DOGE", Doge)]
    [InlineData("ETH", Eth)]
    [InlineData("BNB", Eth)]
    [InlineData("MATIC", Eth)]
    [InlineData("AVAX", Eth)]
    [InlineData("TRX", Tron)]
    [InlineData("USDT", Tron)]
    [InlineData("SOL", Sol)]
    [InlineData("TON", Ton)]
    [InlineData("ADA", Ada)]
    [InlineData("XMR", Xmr)]
    public void A_chains_own_address_shape_is_accepted(string symbol, string address)
    {
        Assert.Equal(AddressShape.Matches, DestinationAddressCheck.Check(symbol, address));
        Assert.False(DestinationAddressCheck.IsProbablyWrongNetwork(symbol, address));
    }

    /// <summary>The costly mistake: the right-looking address for the wrong network.</summary>
    [Theory]
    [InlineData("BTC", Eth)]
    [InlineData("BTC", Tron)]
    [InlineData("LTC", Eth)]
    [InlineData("DOGE", Eth)]
    [InlineData("ETH", Btc)]
    [InlineData("ETH", Tron)]
    [InlineData("TRX", Eth)]
    [InlineData("USDT", Eth)]
    [InlineData("SOL", Eth)]
    [InlineData("TON", Eth)]
    [InlineData("ADA", Eth)]
    [InlineData("XMR", Eth)]
    public void A_foreign_address_is_flagged_as_the_wrong_network(string symbol, string address)
    {
        Assert.Equal(AddressShape.Mismatch, DestinationAddressCheck.Check(symbol, address));
        Assert.True(DestinationAddressCheck.IsProbablyWrongNetwork(symbol, address));
    }

    /// <summary>An ETH address that is the wrong length is not an ETH address.</summary>
    [Fact]
    public void A_truncated_evm_address_is_flagged()
    {
        Assert.Equal(AddressShape.Mismatch, DestinationAddressCheck.Check("ETH", "0x742d35Cc6634C053"));
    }

    /// <summary>No rule for the ticker means no verdict — the check must not invent one.</summary>
    [Theory]
    [InlineData("WHATEVER")]
    [InlineData("DOT")]
    public void An_unknown_ticker_produces_no_verdict(string symbol)
    {
        Assert.Equal(AddressShape.Unknown, DestinationAddressCheck.Check(symbol, Eth));
        Assert.False(DestinationAddressCheck.IsProbablyWrongNetwork(symbol, Eth));
    }

    /// <summary>An empty field is a field the user has not filled in yet, not a mistake.</summary>
    [Theory]
    [InlineData("BTC", "")]
    [InlineData("BTC", "   ")]
    [InlineData("", Btc)]
    [InlineData(null, null)]
    public void Empty_input_is_never_a_warning(string? symbol, string? address)
    {
        Assert.Equal(AddressShape.Unknown, DestinationAddressCheck.Check(symbol, address));
    }

    /// <summary>A pasted address usually arrives with whitespace around it; that must not flip the verdict.</summary>
    [Fact]
    public void Surrounding_whitespace_is_ignored()
    {
        Assert.Equal(AddressShape.Matches, DestinationAddressCheck.Check("BTC", "  " + Btc + "  "));
        Assert.Equal(AddressShape.Matches, DestinationAddressCheck.Check(" eth ", " " + Eth + " "));
    }

    /// <summary>The ticker is case-insensitive: it can come from a picker, a URI or the palette.</summary>
    [Fact]
    public void The_ticker_is_case_insensitive()
    {
        Assert.Equal(AddressShape.Matches, DestinationAddressCheck.Check("btc", Btc));
        Assert.Equal(AddressShape.Mismatch, DestinationAddressCheck.Check("btc", Eth));
    }
}
