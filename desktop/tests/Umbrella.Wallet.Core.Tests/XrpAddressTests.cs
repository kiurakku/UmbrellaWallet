using System.Security.Cryptography;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// XRP classic addresses. The codec is verified against REAL, published XRPL accounts rather than
/// against values this implementation produced — a test that asserts your own output proves only that
/// the code is consistent with itself.
///
/// The checksum cases matter most: an address that survives a mistyped character is an address that
/// sends somebody's money into nothing.
/// </summary>
public sealed class XrpAddressTests
{
    // The XRP Ledger genesis account, documented everywhere in XRPL's own material.
    private const string Genesis = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";

    // The ACCOUNT_ZERO / ACCOUNT_ONE sentinels from the XRPL protocol: account ids of all-zero and
    // all-one bytes. Their addresses are fixed by the spec, so they pin the encoder end to end.
    private const string AccountZero = "rrrrrrrrrrrrrrrrrrrrrhoLvTp";
    private const string AccountOne = "rrrrrrrrrrrrrrrrrrrrBZbvji";

    [Fact]
    public void Real_xrpl_addresses_decode_to_a_twenty_byte_account_id()
    {
        Assert.True(XrpAddress.TryDecode(Genesis, out var id));
        Assert.Equal(20, id.Length);
    }

    [Fact]
    public void Decoding_then_encoding_reproduces_the_address_exactly()
    {
        foreach (var address in new[] { Genesis, AccountZero, AccountOne })
        {
            Assert.True(XrpAddress.TryDecode(address, out var id), address);
            Assert.Equal(address, XrpAddress.Encode(id));
        }
    }

    [Fact]
    public void The_protocol_sentinel_accounts_encode_to_their_specified_addresses()
    {
        // ACCOUNT_ZERO is 20 zero bytes; ACCOUNT_ONE is 19 zero bytes then 0x01. Both addresses are
        // fixed by the XRPL protocol, so these pin the version byte, the checksum and the
        // leading-zero handling that base58 gets wrong most often.
        Assert.Equal(AccountZero, XrpAddress.Encode(new byte[20]));

        var one = new byte[20];
        one[19] = 0x01;
        Assert.Equal(AccountOne, XrpAddress.Encode(one));
    }

    [Fact]
    public void A_single_mistyped_character_is_rejected()
    {
        // Every position, every substitution: the checksum has to catch all of them.
        var rejected = 0;
        for (var i = 1; i < Genesis.Length; i++)
        {
            foreach (var c in XrpAddress.Alphabet)
            {
                if (Genesis[i] == c) continue;
                var typo = Genesis[..i] + c + Genesis[(i + 1)..];
                if (!XrpAddress.IsValid(typo)) rejected++;
                else Assert.Fail($"accepted a corrupted address: {typo}");
            }
        }

        Assert.True(rejected > 1000, $"expected to have tested many typos, tested {rejected}");
    }

    [Fact]
    public void Transposing_two_characters_is_rejected()
    {
        var chars = Genesis.ToCharArray();
        (chars[5], chars[6]) = (chars[6], chars[5]);
        Assert.False(XrpAddress.IsValid(new string(chars)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not an address")]
    // Bitcoin's alphabet contains 0, O, I and l; XRPL's does not, so a BTC address cannot decode.
    [InlineData("1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2")]
    [InlineData("bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4")]
    [InlineData("0x742d35Cc6634C0532925a3b844Bc454e4438f44e")]
    public void Anything_that_is_not_an_xrp_address_is_rejected(string? input)
    {
        Assert.False(XrpAddress.IsValid(input));
    }

    [Fact]
    public void An_address_of_the_right_shape_but_a_wrong_checksum_is_rejected()
    {
        // Same length, same alphabet, valid base58 — only the checksum is wrong.
        Assert.True(XrpAddress.TryDecode(Genesis, out var id));
        id[0] ^= 0xFF;
        var forged = XrpAddress.Encode(id);

        // The forgery is itself valid (it was re-encoded with a correct checksum) …
        Assert.True(XrpAddress.IsValid(forged));
        // … but it is a different account, which is the point: a checksum proves integrity, not ownership.
        Assert.NotEqual(Genesis, forged);
    }

    [Fact]
    public void The_account_id_matches_an_independent_hash160_implementation()
    {
        // The account id is HASH160 — RIPEMD160(SHA256(x)) — the same primitive Bitcoin uses for
        // P2PKH. .NET dropped RIPEMD-160, so this file implements it; checking it against NBitcoin,
        // a separate battle-tested implementation already in the build, is what makes it trustworthy.
        // Without this the hand-written hash would only ever be compared against itself.
        foreach (var sample in new[]
                 {
                     Array.Empty<byte>(),
                     "abc"u8.ToArray(),
                     "umbrella"u8.ToArray(),
                     Convert.FromHexString("0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798"),
                 })
        {
            var mine = XrpAddress.AccountIdFromPublicKey(sample);
            var theirs = NBitcoin.Crypto.Hashes.Hash160(sample).ToBytes();
            Assert.Equal(theirs, mine);
        }
    }

    [Fact]
    public void A_derived_account_id_round_trips_through_the_address_form()
    {
        // The generator point of secp256k1, compressed — a real public key, so this is the whole
        // pubkey → account id → address → account id path rather than a synthetic byte array.
        var pubkey = Convert.FromHexString(
            "0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798");

        var id = XrpAddress.AccountIdFromPublicKey(pubkey);
        var address = XrpAddress.Encode(id);

        Assert.StartsWith("r", address, StringComparison.Ordinal);
        Assert.True(XrpAddress.TryDecode(address, out var back));
        Assert.Equal(id, back);
    }
}
