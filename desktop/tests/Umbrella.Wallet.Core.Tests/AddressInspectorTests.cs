using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the Address checker: it names the network from an address's shape and, where it can be done
/// without guessing, verifies the checksum — reporting a tampered address as Invalid, an unchecksummed
/// EVM address as Valid, chains it can't deeply verify as Unverified, and gibberish as unrecognised.
/// </summary>
public sealed class AddressInspectorTests
{
    [Theory]
    [InlineData("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa", "BTC")]              // legacy P2PKH (genesis)
    [InlineData("bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4", "BTC")]     // native segwit (BIP173)
    [InlineData("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed", "ETH")]     // EIP-55 checksummed
    [InlineData("TNvxWShQmqxskvFvh2TGYjskVwVWEisPCA", "TRX")]             // TRON base58check
    public void Recognises_and_validates_a_good_address(string addr, string net)
    {
        var r = AddressInspector.Inspect(addr);
        Assert.Equal(net, r.Network);
        Assert.Equal(AddressValidity.Valid, r.Validity);
    }

    [Fact]
    public void A_tampered_checksum_reads_as_invalid_on_the_detected_network()
    {
        var btc = AddressInspector.Inspect("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNb"); // last char changed
        Assert.Equal("BTC", btc.Network);
        Assert.Equal(AddressValidity.Invalid, btc.Validity);

        var eth = AddressInspector.Inspect("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAeD"); // case flip breaks EIP-55
        Assert.Equal("ETH", eth.Network);
        Assert.Equal(AddressValidity.Invalid, eth.Validity);
    }

    [Fact]
    public void An_all_lowercase_evm_address_is_valid_but_unchecksummed()
    {
        var r = AddressInspector.Inspect("0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed");
        Assert.Equal("ETH", r.Network);
        Assert.Equal(AddressValidity.Valid, r.Validity); // no-checksum is still a valid address
    }

    [Fact]
    public void A_chain_we_cannot_deeply_verify_is_recognised_but_unverified()
    {
        var r = AddressInspector.Inspect("UQ" + new string('A', 46));
        Assert.Equal("TON", r.Network);
        Assert.Equal(AddressValidity.Unverified, r.Validity);
    }

    [Fact]
    public void A_made_up_Cardano_address_is_recognised_and_called_invalid()
    {
        // It used to be "unverified" — and the send path would have decoded and paid it. The bech32
        // checksum is now checked, so a string that merely looks like addr1… is caught.
        var r = AddressInspector.Inspect("addr1q9abcdefghijklmnopqrstuvwxyz");
        Assert.Equal("ADA", r.Network);
        Assert.Equal(AddressValidity.Invalid, r.Validity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello world")]
    public void Unrecognised_input_is_not_recognised(string addr) =>
        Assert.False(AddressInspector.Inspect(addr).Recognised);

    [Fact]
    public void An_xrp_address_is_named_and_verified_not_guessed()
    {
        // XRP is one of the coins people most often send over the wrong network, so the checker gives
        // a definitive answer rather than "recognised by shape": the alphabet and the double-SHA256
        // checksum are both verifiable without a chain-specific library.
        var r = AddressInspector.Inspect("rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh");
        Assert.Equal("XRP", r.Network);
        Assert.Equal(AddressValidity.Valid, r.Validity);
    }

    [Fact]
    public void A_corrupted_xrp_address_is_named_but_reported_invalid()
    {
        // Naming the network while rejecting the checksum is the useful answer: it tells the user
        // they meant XRP and got a character wrong, rather than just "unrecognised".
        var r = AddressInspector.Inspect("rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTa");
        Assert.Equal("XRP", r.Network);
        Assert.Equal(AddressValidity.Invalid, r.Validity);
    }
}
