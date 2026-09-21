using Umbrella.Wallet.Core.Cardano;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Codecs;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The shared Bech32 codec, pinned to BIP-173's own test strings — and the Cardano defect it fixes: the
/// old decoder dropped the checksum unread, so a mistyped destination decoded to someone else's
/// (nobody's) key hashes and the send path would have paid it.
/// </summary>
public sealed class Bech32Tests
{
    [Theory]
    [InlineData("A12UEL5L")]
    [InlineData("a12uel5l")]
    [InlineData("an83characterlonghumanreadablepartthatcontainsthenumber1andtheexcludedcharactersbio1tt5tgs")]
    [InlineData("abcdef1qpzry9x8gf2tvdw0s3jn54khce6mua7lmqqqxw")]
    [InlineData("split1checkupstagehandshakeupstreamerranterredcaperred2y9e3w")]
    [InlineData("?1ezyfcl")]
    public void BIP173_valid_strings_verify(string text)
    {
        Assert.True(Bech32.TryDecodeWords(text, out _, out _));
    }

    [Fact]
    public void BIP173_valid_string_at_exactly_the_length_limit_verifies()
    {
        // "11" + 82 × 'q' + "c8247j": 90 characters, the maximum. Built rather than typed, because one
        // 'q' too many is both easy to type and exactly the case the length rule exists for.
        var text = "11" + new string('q', 82) + "c8247j";
        Assert.Equal(90, text.Length);
        Assert.True(Bech32.TryDecodeWords(text, out _, out _));
        Assert.False(Bech32.TryDecodeWords("11" + new string('q', 83) + "c8247j", out _, out _));
    }

    public static IEnumerable<object[]> InvalidStrings() =>
    [
        ["\u0020" + "1nwldj5"],        // HRP character out of range
        ["\u007F" + "1axkwrx"],
        ["\u0080" + "1eym55h"],
        ["an84characterslonghumanreadablepartthatcontainsthenumber1andtheexcludedcharactersbio1569pvx"],   // too long
        ["pzry9x0s0muk"],              // no separator
        ["1pzry9x0s0muk"],             // empty HRP
        ["x1b4n0q5v"],                 // invalid data character
        ["li1dgmt3"],                  // checksum too short
        ["de1lg7wt" + "\u00FF"],       // invalid character in checksum
        ["A1G7SGD8"],                  // checksum computed over the upper-case HRP
        ["10a06t8"],                   // empty HRP
        ["1qzzfhee"],                  // empty HRP
    ];

    [Theory]
    [MemberData(nameof(InvalidStrings))]
    public void BIP173_invalid_strings_are_refused(string text)
    {
        Assert.False(Bech32.TryDecodeWords(text, out _, out _));
    }

    [Fact]
    public void Mixed_case_is_refused()
    {
        Assert.False(Bech32.TryDecodeWords("A12uEL5L", out _, out _));
    }

    [Fact]
    public void Encoding_then_decoding_returns_the_same_bytes()
    {
        var data = Enumerable.Range(0, 20).Select(i => (byte)(i * 13)).ToArray();
        var text = Bech32.Encode("cosmos", data);

        Assert.True(Bech32.TryDecode(text, out var hrp, out var back));
        Assert.Equal("cosmos", hrp);
        Assert.Equal(data, back);
    }

    // --- the Cardano fix -------------------------------------------------------------------------

    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    [Fact]
    public void A_real_Cardano_address_still_decodes()
    {
        var address = new HdAddressDeriver().DeriveReceiveAddress(Phrase, ChainId.Ada, passphrase: "").Address;

        Assert.True(AdaTransfer.IsValidAddress(address));
        Assert.Equal(57, AdaTransfer.DecodeAddress(address).Length);   // header + payment hash + stake hash
    }

    [Fact]
    public void Every_single_character_typo_of_a_Cardano_address_is_refused()
    {
        // Before this fix, every one of these decoded to a valid-looking address and could have been paid.
        var address = new HdAddressDeriver().DeriveReceiveAddress(Phrase, ChainId.Ada, passphrase: "").Address;
        var dataStart = address.LastIndexOf('1') + 1;
        var tried = 0;

        for (var i = dataStart; i < address.Length; i++)
        {
            foreach (var c in Bech32.Charset)
            {
                if (c == address[i]) continue;
                var typo = address[..i] + c + address[(i + 1)..];
                Assert.Throws<FormatException>(() => AdaTransfer.DecodeAddress(typo));
                tried++;
            }
        }

        Assert.True(tried > 3000, $"tested {tried} typos");
    }

    [Fact]
    public void The_inspector_now_calls_a_typo_invalid_instead_of_unverified()
    {
        var address = new HdAddressDeriver().DeriveReceiveAddress(Phrase, ChainId.Ada, passphrase: "").Address;
        var i = address.Length - 10;
        var typo = address[..i] + (address[i] == 'q' ? 'p' : 'q') + address[(i + 1)..];

        Assert.Equal(AddressValidity.Valid, AddressInspector.Inspect(address).Validity);
        Assert.Equal(AddressValidity.Invalid, AddressInspector.Inspect(typo).Validity);
    }

    [Fact]
    public void A_testnet_Cardano_address_is_refused_on_mainnet()
    {
        var address = new HdAddressDeriver().DeriveReceiveAddress(Phrase, ChainId.Ada, passphrase: "").Address;
        Assert.True(Bech32.TryDecode(address, out _, out var data, 1023));

        var testnet = Bech32.Encode("addr_test", data);
        Assert.Throws<FormatException>(() => AdaTransfer.DecodeAddress(testnet));
    }
}
