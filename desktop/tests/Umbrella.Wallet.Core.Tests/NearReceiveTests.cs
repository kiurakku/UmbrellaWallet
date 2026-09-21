using System.Text.Json;
using NBitcoin.DataEncoders;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// NEAR receive and balance (roadmap N.7), pinned to near-seed-phrase — the library NEAR wallets restore
/// phrases with — not to this wallet's own output.
/// </summary>
public sealed class NearReceiveTests
{
    /// <summary>near-seed-phrase test/index.test.js, "parse seed phrase" (normalised to lower case, as the
    /// library itself does before deriving).</summary>
    private const string Phrase = "shoot island position soft burden budget tooth cruel issue economy destroy above";
    private const string PublicKeyBase58 = "r4yuiZE45mzeZAENDEF2pWeFBJkW8mQYGx3rU46zCqh";

    private static readonly HdAddressDeriver Deriver = new();

    [Fact]
    public void The_key_is_the_one_near_seed_phrase_derives()
    {
        var key = Deriver.DeriveNearPublicKey(Phrase, passphrase: "");
        Assert.Equal(PublicKeyBase58, Encoders.Base58.EncodeData(key));
    }

    [Fact]
    public void The_receive_address_is_the_implicit_account_of_that_key()
    {
        var address = Deriver.DeriveReceiveAddress(Phrase, ChainId.Near, passphrase: "");
        var expected = Convert.ToHexString(Encoders.Base58.DecodeData(PublicKeyBase58)).ToLowerInvariant();

        Assert.Equal(expected, address.Address);
        Assert.True(NearAccounts.IsImplicitAccountId(address.Address));
        Assert.Equal("m/44'/397'/0'", address.DerivationPath);
    }

    [Theory]
    [InlineData("alice.near")]
    [InlineData("ABCDEF0000000000000000000000000000000000000000000000000000000000")]   // upper case
    [InlineData("abc")]
    public void Only_a_64_character_lower_case_hex_id_is_an_implicit_account(string id)
    {
        Assert.False(NearAccounts.IsImplicitAccountId(id));
    }

    [Fact]
    public void NEAR_receives_shows_a_balance_and_sends()
    {
        // Send was switched on only once a signed transfer matched near-api-js byte for byte
        // (NearSendTests); before that this test pinned it off.
        var info = ChainCatalog.Get(ChainId.Near);
        Assert.True(info.CanReceive && info.CanSyncBalance && info.CanSend);
    }

    private static decimal? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return NearAccounts.ParseViewAccount(doc.RootElement);
    }

    [Fact]
    public void A_live_account_reads_in_NEAR()
    {
        Assert.Equal(1.5m, Parse("""{"jsonrpc":"2.0","result":{"amount":"1500000000000000000000000","locked":"0"},"id":"u"}"""));
    }

    [Fact]
    public void An_amount_beyond_what_decimal_can_hold_in_yocto_still_reads()
    {
        // 69 264.5… NEAR as the live RPC returned it for the "near" account — 6.9 × 10^28 yocto, right at
        // decimal's edge — and ten times that, which is past it. A holder of ~80 000 NEAR must not see
        // "could not read".
        Assert.Equal(69264.537675116505177595460580m, Parse("""{"result":{"amount":"69264537675116505177595460580"}}"""));
        Assert.Equal(692645.37675116505177595460580m, Parse("""{"result":{"amount":"692645376751165051775954605800"}}"""));
    }

    [Fact]
    public void An_account_the_chain_does_not_have_yet_is_a_real_zero()
    {
        // The shape both public RPCs answered for an unfunded implicit id.
        Assert.Equal(0m, Parse("""
            {"jsonrpc":"2.0","error":{"name":"HANDLER_ERROR","cause":{"name":"UNKNOWN_ACCOUNT"},"code":-32000}}
            """));
    }

    [Theory]
    [InlineData("""{"error":{"name":"HANDLER_ERROR","cause":{"name":"UNAVAILABLE_SHARD"}}}""")]
    [InlineData("""{"error":"This endpoint has been discontinued."}""")]
    [InlineData("""{"result":{"amount":"1.5"}}""")]
    [InlineData("""{"result":{"amount":15}}""")]
    [InlineData("""{"result":{}}""")]
    [InlineData("""{"result":"ok"}""")]
    [InlineData("""{"error":{"cause":"UNKNOWN_ACCOUNT"}}""")]
    [InlineData("""[]""")]
    public void Anything_else_is_unknown_never_zero(string json)
    {
        Assert.Null(Parse(json));
    }
}
