using System.Globalization;
using System.Numerics;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Polkadot;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Polkadot send (roadmap N.8): the pieces that do not need the running chain. The era encoding is
/// Substrate's own <c>mortal_codec_works</c> vector; SCALE compacts are parity-scale-codec's documented
/// examples. The transaction itself is checked by Asset Hub in <c>PolkadotSendLiveTests</c>.
/// </summary>
public sealed class PolkadotSendTests
{
    [Fact]
    public void A_mortal_era_encodes_as_substrate_does()
    {
        // sp-runtime era.rs, mortal_codec_works: Era::mortal(64, 42) → [5 + 42 % 16 * 16, 42 / 16].
        Assert.Equal(new byte[] { 5 + (42 % 16 * 16), 42 / 16 }, PolkadotTransactions.MortalEra(42));
        Assert.Equal(PolkadotTransactions.MortalEra(42), PolkadotTransactions.MortalEra(42 + 64 * 1000));   // only the phase matters
    }

    [Theory]
    [InlineData("0", "00")]
    [InlineData("1", "04")]
    [InlineData("42", "a8")]
    [InlineData("69", "1501")]
    [InlineData("65535", "feff0300")]
    [InlineData("100000000000000", "0b00407a10f35a")]
    public void Compact_integers_match_parity_scale_codec(string value, string hex)
    {
        Assert.Equal(hex, Convert.ToHexString(Scale.Compact(BigInteger.Parse(value, CultureInfo.InvariantCulture))).ToLowerInvariant());
        Assert.Equal(BigInteger.Parse(value, CultureInfo.InvariantCulture), new ScaleReader(Convert.FromHexString(hex)).Compact());
    }

    [Theory]
    [InlineData("1", "10000000000")]
    [InlineData("0.0000000001", "1")]
    [InlineData("12.5", "125000000000")]
    public void Amounts_become_whole_planck(string dot, string planck)
    {
        Assert.True(PolkadotSendRules.TryToPlanck(decimal.Parse(dot, CultureInfo.InvariantCulture), out var p));
        Assert.Equal(BigInteger.Parse(planck, CultureInfo.InvariantCulture), p);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.00000000001")]
    public void Amounts_finer_than_a_planck_are_refused(string dot) =>
        Assert.False(PolkadotSendRules.TryToPlanck(decimal.Parse(dot, CultureInfo.InvariantCulture), out _));

    [Fact]
    public void An_account_record_gives_nonce_and_balances_and_a_missing_one_is_a_real_zero()
    {
        // nonce 7, consumers 0, providers 1, sufficients 0; free 2 DOT, reserved 0.5, frozen 1, flags.
        var hex = "0x" +
                  "07000000" + "00000000" + "01000000" + "00000000" +
                  Le128(20_000_000_000) + Le128(5_000_000_000) + Le128(10_000_000_000) + Le128(0);
        var (exists, account) = PolkadotSendRules.ParseAccount(hex, missing: false);
        Assert.True(exists);
        Assert.Equal(7u, account!.Nonce);

        // Spendable = free − max(frozen − reserved, ED) = 2 − max(0.5, 0.01) = 1.5 DOT.
        Assert.Equal(new BigInteger(15_000_000_000), account.Spendable(100_000_000));
        // With nothing frozen, the existential deposit is what stays behind.
        Assert.Equal(new BigInteger(19_900_000_000), (account with { Frozen = 0 }).Spendable(100_000_000));

        var (missingExists, missingAccount) = PolkadotSendRules.ParseAccount(null, missing: true);
        Assert.False(missingExists);
        Assert.NotNull(missingAccount);
        Assert.Null(PolkadotSendRules.ParseAccount("0x0700", missing: false).Account);
    }

    private static string Le128(ulong v) => Convert.ToHexString(BitConverter.GetBytes(v)).ToLowerInvariant() + new string('0', 16);

    [Fact]
    public void The_fee_is_read_from_the_dispatch_info()
    {
        // weight { ref_time: compact, proof_size: compact }, class Normal, partial_fee u128.
        var hex = "0x" + Convert.ToHexString([.. Scale.Compact(151_000_000), .. Scale.Compact(3_593), 0x00]).ToLowerInvariant() + Le128(16_325_300);
        Assert.Equal(new BigInteger(16_325_300), PolkadotSendRules.ParseQueryInfoFee(hex));
        Assert.Null(PolkadotSendRules.ParseQueryInfoFee("0x00"));
        Assert.Null(PolkadotSendRules.ParseQueryInfoFee(null));
    }

    [Theory]
    [InlineData("0x00", true, null)]
    [InlineData("0x010001", false, "cannot pay the fee")]
    [InlineData("0x010004", false, "signature does not match")]
    [InlineData("0x010003", false, "already sent a transaction with this nonce")]
    [InlineData("0x0101", false, "could not check")]
    public void The_nodes_validation_says_whether_it_would_take_the_transfer(string hex, bool valid, string? reason)
    {
        var (ok, why) = PolkadotSendRules.ParseValidity(hex);
        Assert.Equal(valid, ok);
        if (reason is null) Assert.Null(why);
        else Assert.Contains(reason, why);
    }

    [Fact]
    public void The_extrinsic_is_found_in_a_block_by_its_exact_bytes()
    {
        byte[] ours = [0x10, 0x84, 0x01, 0x02];
        Assert.Equal(1, PolkadotSendRules.IndexIn(["0x280402000b", "0x10840102"], ours));
        Assert.Equal(-1, PolkadotSendRules.IndexIn(["0x280402000b"], ours));
    }

    [Fact]
    public void The_signing_key_is_the_one_the_receive_address_comes_from()
    {
        const string phrase = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
        var deriver = new HdAddressDeriver();
        using var key = deriver.DeriveDotKeypair(phrase, passphrase: "");
        Assert.Equal(deriver.DeriveReceiveAddress(phrase, Chains.ChainId.Dot, passphrase: "").Address, Ss58.Encode(key.PublicKey));
    }
}
