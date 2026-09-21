using System.Numerics;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Polkadot;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Polkadot receive (roadmap N.8). Every stage is checked against something this wallet did not write:
/// subkey's documented output for phrase → mini secret → public key → address, Polkadot.js's address
/// tests for the SS58 prefixes, and an independent BLAKE2b (Python's hashlib) for the storage key the
/// balance is read from.
/// </summary>
public sealed class PolkadotReceiveTests
{
    // substrate/bin/utils/subkey/README.md — `subkey generate` example (sr25519, no derivation path).
    private const string SubkeyPhrase = "hotel forest jar hover kite book view eight stuff angle legend defense";
    private const string SubkeyMiniSecret = "a05c75731970cc7868a2fb7cb577353cd5b31f62dccced92c441acd8fee0c92d";
    private const string SubkeyPublicKey = "fec70cfbf1977c6965b5af10a4534a6a35d548eb14580594d0bc543286892515";
    private const string SubkeySs58Generic = "5Hpm9fq3W3dQgwWpAwDS2ZHKAdnk86QRCu7iX4GnmDxycrte";

    [Fact]
    public void The_mini_secret_is_the_one_subkey_derives_from_the_phrase()
    {
        var entropy = AdaKeys.EntropyFromMnemonic(SubkeyPhrase);
        Assert.Equal(SubkeyMiniSecret, Convert.ToHexString(PolkadotKeys.MiniSecretFromEntropy(entropy)).ToLowerInvariant());
    }

    [Fact]
    public void The_sr25519_public_key_is_the_one_subkey_derives()
    {
        var key = PolkadotKeys.PublicKeyFromMiniSecret(Convert.FromHexString(SubkeyMiniSecret));
        Assert.Equal(SubkeyPublicKey, Convert.ToHexString(key).ToLowerInvariant());
        Assert.Equal(SubkeySs58Generic, Ss58.Encode(key, prefix: 42));
    }

    [Fact]
    public void The_wallet_shows_that_same_account_in_Polkadot_format()
    {
        var address = new HdAddressDeriver().DeriveReceiveAddress(SubkeyPhrase, Umbrella.Wallet.Core.Chains.ChainId.Dot, passphrase: "");

        Assert.Equal(Ss58.Encode(Convert.FromHexString(SubkeyPublicKey), Ss58.PolkadotPrefix), address.Address);
        Assert.StartsWith("1", address.Address);
        Assert.True(Ss58.IsPolkadotAddress(address.Address));
    }

    [Fact]
    public void A_passphrase_gives_a_different_Polkadot_account()
    {
        var d = new HdAddressDeriver();
        Assert.NotEqual(
            d.DeriveReceiveAddress(SubkeyPhrase, Umbrella.Wallet.Core.Chains.ChainId.Dot, passphrase: "").Address,
            d.DeriveReceiveAddress(SubkeyPhrase, Umbrella.Wallet.Core.Chains.ChainId.Dot, passphrase: "hidden").Address);
    }

    [Theory]
    // polkadot-js/common util-crypto address/encode.spec.ts
    [InlineData("5GrwvaEF5zXb26Fz9rcQpDWS57CtERHpNehXCPcNoHGKutQY", (byte)0, "15oF4uVJwmo4TdGW7VfQxNLavjCXviqxT9S1MgbjMNHr6Sp5")]
    [InlineData("5GrwvaEF5zXb26Fz9rcQpDWS57CtERHpNehXCPcNoHGKutQY", (byte)2, "HNZata7iMYWmk5RvZRTiAsSDhV8366zq2YGb3tLH5Upf74F")]
    public void SS58_reencodes_as_Polkadot_js_does(string generic, byte prefix, string expected)
    {
        Assert.True(Ss58.TryDecode(generic, out var genericPrefix, out var key));
        Assert.Equal(42, genericPrefix);
        Assert.Equal(expected, Ss58.Encode(key, prefix));
    }

    [Fact]
    public void A_raw_key_encodes_as_Polkadot_js_does()
    {
        var key = Convert.FromHexString("3050f8456519829fe03302da802d22d3233a5f4037b9a3e2bcc403ccfcb2d735");
        Assert.Equal("5DA4D4GL5iakrn22h5uKoevgvo18Pqj5BcdEUv8etEDPdijA", Ss58.Encode(key, 42));
    }

    [Fact]
    public void Every_single_character_typo_is_refused()
    {
        const string good = "15oF4uVJwmo4TdGW7VfQxNLavjCXviqxT9S1MgbjMNHr6Sp5";
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        Assert.True(Ss58.IsPolkadotAddress(good));

        var tried = 0;
        for (var i = 1; i < good.Length; i++)
        foreach (var c in alphabet)
        {
            if (c == good[i]) continue;
            Assert.False(Ss58.IsPolkadotAddress(good[..i] + c + good[(i + 1)..]));
            tried++;
        }

        Assert.True(tried > 2000);
    }

    [Fact]
    public void A_Kusama_or_generic_address_is_not_taken_for_Polkadot()
    {
        Assert.False(Ss58.IsPolkadotAddress("HNZata7iMYWmk5RvZRTiAsSDhV8366zq2YGb3tLH5Upf74F"));
        Assert.False(Ss58.IsPolkadotAddress("5GrwvaEF5zXb26Fz9rcQpDWS57CtERHpNehXCPcNoHGKutQY"));
    }

    // --- balance ----------------------------------------------------------------------------------

    [Fact]
    public void The_storage_key_matches_an_independent_BLAKE2b()
    {
        // Built with Python's hashlib.blake2b(digest_size=16) and used against the live RPC.
        var alice = Convert.FromHexString("d43593c715fdd31c61141abd04a99fd6822c8558854ccde39a5684e7a56da27d");
        Assert.Equal(
            "0x26aa394eea5630e07c48ae0c9558cef7b99d880ec681799c0cf30e8886371da9" +
            "de1e86a9a8c739864cf3cc5ec2bea59f" +
            "d43593c715fdd31c61141abd04a99fd6822c8558854ccde39a5684e7a56da27d",
            PolkadotAccounts.SystemAccountKey(alice));
    }

    private static string AccountInfo(BigInteger free, BigInteger reserved)
    {
        var bytes = new byte[80];
        bytes[8] = 1;   // providers = 1
        free.ToByteArray(isUnsigned: true, isBigEndian: false).CopyTo(bytes, 16);
        reserved.ToByteArray(isUnsigned: true, isBigEndian: false).CopyTo(bytes, 32);
        bytes[79] = 0x80;   // flags, as the live chain sets them
        return "0x" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [Fact]
    public void Free_and_reserved_together_read_in_DOT()
    {
        // The relay-chain treasury entry as read live: 26 999 310 283 510 planck free, nothing reserved.
        Assert.Equal(2699.931028351m, PolkadotAccounts.ParseAccountInfo(AccountInfo(26_999_310_283_510, 0), entryMissing: false));
        Assert.Equal(1.5m, PolkadotAccounts.ParseAccountInfo(AccountInfo(10_000_000_000, 5_000_000_000), entryMissing: false));
    }

    [Fact]
    public void A_missing_entry_is_an_account_that_does_not_exist_a_real_zero()
    {
        Assert.Equal(0m, PolkadotAccounts.ParseAccountInfo(null, entryMissing: true));
    }

    [Theory]
    [InlineData("0x00")]
    [InlineData("0xzz")]
    [InlineData("")]
    [InlineData(null)]
    public void A_record_that_is_not_an_AccountInfo_is_unknown(string? hex)
    {
        Assert.Null(PolkadotAccounts.ParseAccountInfo(hex, entryMissing: false));
    }
}
