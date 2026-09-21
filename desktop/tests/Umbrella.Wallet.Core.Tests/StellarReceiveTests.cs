using System.Text.Json;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Stellar receive and balance (roadmap N.5). Addresses are checked against SEP-0005's own published
/// test vectors — the standard every Stellar wallet that restores from a phrase follows — never
/// against this wallet's output.
/// </summary>
public sealed class StellarReceiveTests
{
    private static readonly HdAddressDeriver Deriver = new();

    [Theory]
    // SEP-0005 "Test 1" (12 words): m/44'/148'/0' and m/44'/148'/1'.
    [InlineData("illness spike retreat truth genius clock brain pass fit cave bargain toe", 0u,
        "GDRXE2BQUC3AZNPVFSCEZ76NJ3WWL25FYFK6RGZGIEKWE4SOOHSUJUJ6")]
    [InlineData("illness spike retreat truth genius clock brain pass fit cave bargain toe", 1u,
        "GBAW5XGWORWVFE2XTJYDTLDHXTY2Q2MO73HYCGB3XMFMQ562Q2W2GJQX")]
    // SEP-0005 "Test 3" (24 words): m/44'/148'/0'.
    [InlineData("bench hurt jump file august wise shallow faculty impulse spring exact slush thunder author capable act festival slice deposit sauce coconut afford frown better", 0u,
        "GC3MMSXBWHL6CPOAVERSJITX7BH76YU252WGLUOM5CJX3E7UCYZBTPJQ")]
    public void Addresses_match_the_SEP_0005_vectors(string mnemonic, uint index, string expected)
    {
        var address = Deriver.DeriveReceiveAddress(mnemonic, ChainId.Xlm, index, passphrase: "");
        Assert.Equal(expected, address.Address);
        Assert.Equal($"m/44'/148'/{index}'", address.DerivationPath);
    }

    [Fact]
    public void A_valid_account_id_passes_and_every_single_typo_is_caught()
    {
        const string good = "GDRXE2BQUC3AZNPVFSCEZ76NJ3WWL25FYFK6RGZGIEKWE4SOOHSUJUJ6";
        Assert.True(StellarKeys.IsValidAccountId(good));

        // Every position, every other base32 character: CRC16 must reject each one.
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var tried = 0;
        for (var i = 1; i < good.Length; i++)
        {
            foreach (var c in alphabet)
            {
                if (c == good[i]) continue;
                var typo = good[..i] + c + good[(i + 1)..];
                Assert.False(StellarKeys.IsValidAccountId(typo), typo);
                tried++;
            }
        }

        Assert.True(tried > 1000);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SDRXE2BQUC3AZNPVFSCEZ76NJ3WWL25FYFK6RGZGIEKWE4SOOHSUJUJ6")]   // S… is a SECRET seed, never an address
    [InlineData("GDRXE2BQUC3AZNPVFSCEZ76NJ3WWL25FYFK6RGZGIEKWE4SOOHSUJUJ")]    // one short
    [InlineData("gdrxe2bquc3aznpvfscez76nj3wwl25fyfk6rgzgiekwe4soohsujuj6")]   // StrKey is upper-case
    public void Anything_that_is_not_an_account_id_is_refused(string text)
    {
        Assert.False(StellarKeys.IsValidAccountId(text));
    }

    [Fact]
    public void The_inspector_names_Stellar_for_a_G_address()
    {
        var result = AddressInspector.Inspect("GDRXE2BQUC3AZNPVFSCEZ76NJ3WWL25FYFK6RGZGIEKWE4SOOHSUJUJ6");
        Assert.Equal("XLM", result.Network);
        Assert.Equal(AddressValidity.Valid, result.Validity);
    }

    [Fact]
    public void Stellar_is_receive_and_balance_only()
    {
        var info = ChainCatalog.Get(ChainId.Xlm);
        Assert.True(info.CanReceive && info.CanSyncBalance);
        Assert.False(info.CanSend);
    }

    // --- Horizon ----------------------------------------------------------------------------------

    private static decimal? Parse(int status, string? json)
    {
        if (json is null) return StellarHorizon.ParseAccount(status, null);
        using var doc = JsonDocument.Parse(json);
        return StellarHorizon.ParseAccount(status, doc.RootElement);
    }

    [Fact]
    public void A_funded_account_reads_its_native_balance()
    {
        Assert.Equal(12.3456789m, Parse(200, """
            {"balances":[
              {"balance":"100.0000000","asset_type":"credit_alphanum4","asset_code":"USDC"},
              {"balance":"12.3456789","asset_type":"native"}]}
            """));
    }

    [Fact]
    public void An_address_nobody_has_funded_is_a_real_zero()
    {
        Assert.Equal(0m, Parse(404, null));
    }

    [Theory]
    [InlineData(429, null)]
    [InlineData(500, null)]
    [InlineData(200, """{"balances":[]}""")]
    [InlineData(200, """{"balances":[{"balance":"1,5","asset_type":"native"}]}""")]
    [InlineData(200, """{"balances":[{"balance":"-1","asset_type":"native"}]}""")]
    [InlineData(200, """{"id":"G…"}""")]
    public void Anything_else_is_unknown_never_zero(int status, string? json)
    {
        Assert.Null(Parse(status, json));
    }
}
