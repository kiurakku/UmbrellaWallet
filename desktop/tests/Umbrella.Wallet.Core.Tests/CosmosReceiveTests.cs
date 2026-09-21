using System.Text.Json;
using NBitcoin.DataEncoders;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Cosmos Hub receive and balance (roadmap N.6), pinned to cosmjs — the library Keplr and most Cosmos
/// wallets are built on — rather than to this wallet's own output.
/// </summary>
public sealed class CosmosReceiveTests
{
    /// <summary>cosmjs packages/proto-signing — DirectSecp256k1HdWallet's default test wallet.</summary>
    private const string CosmjsMnemonic =
        "special sign fit simple patrol salute grocery chicken wheat radar tonight ceiling";
    private const string CosmjsPublicKey = "02baa4ef93f2ce84592a49b1d729c074eab640112522a7a89f7d03ebab21ded7b6";
    private const string CosmjsAddress = "cosmos1jhg0e7s6gn44tfc5k37kr04sznyhedtc9rzys5";

    private static readonly HdAddressDeriver Deriver = new();

    [Fact]
    public void The_key_and_address_are_the_ones_cosmjs_derives()
    {
        var key = Deriver.DeriveCosmosPublicKey(CosmjsMnemonic, passphrase: "");
        Assert.Equal(CosmjsPublicKey, Encoders.Hex.EncodeData(key.ToBytes()));

        var address = Deriver.DeriveReceiveAddress(CosmjsMnemonic, ChainId.Atom, passphrase: "");
        Assert.Equal(CosmjsAddress, address.Address);
        Assert.Equal("m/44'/118'/0'/0/0", address.DerivationPath);
    }

    [Fact]
    public void A_mistyped_Cosmos_address_is_refused()
    {
        Assert.True(CosmosHub.IsValidAddress(CosmjsAddress));

        var i = CosmjsAddress.Length - 8;
        var typo = CosmjsAddress[..i] + (CosmjsAddress[i] == 'q' ? 'p' : 'q') + CosmjsAddress[(i + 1)..];
        Assert.False(CosmosHub.IsValidAddress(typo));
        Assert.Equal(AddressValidity.Invalid, AddressInspector.Inspect(typo).Validity);
        Assert.Equal("ATOM", AddressInspector.Inspect(CosmjsAddress).Network);
    }

    [Fact]
    public void Another_chains_bech32_address_is_not_taken_for_Cosmos()
    {
        // Same bytes, different prefix: an Osmosis address must not pass as a Cosmos Hub one.
        Assert.True(Umbrella.Wallet.Core.Codecs.Bech32.TryDecode(CosmjsAddress, out _, out var id));
        Assert.False(CosmosHub.IsValidAddress(Umbrella.Wallet.Core.Codecs.Bech32.Encode("osmo", id)));
    }

    [Fact]
    public void Cosmos_is_receive_and_balance_only_and_says_staking_is_not_counted()
    {
        var info = ChainCatalog.Get(ChainId.Atom);
        Assert.True(info.CanReceive && info.CanSyncBalance);
        Assert.False(info.CanSend);
        Assert.Contains("staked", info.PrivacyNote, StringComparison.OrdinalIgnoreCase);
    }

    private static decimal? Parse(int status, string? json)
    {
        if (json is null) return CosmosHub.ParseBalance(status, null);
        using var doc = JsonDocument.Parse(json);
        return CosmosHub.ParseBalance(status, doc.RootElement);
    }

    [Fact]
    public void The_bank_balance_reads_in_ATOM()
    {
        Assert.Equal(12.345678m, Parse(200, """{"balance":{"denom":"uatom","amount":"12345678"}}"""));
    }

    [Fact]
    public void An_address_the_chain_has_never_seen_is_a_real_zero()
    {
        // What the live servers answer for an unfunded address (checked against all three).
        Assert.Equal(0m, Parse(200, """{"balance":{"denom":"uatom","amount":"0"}}"""));
    }

    [Theory]
    [InlineData(400, null)]
    [InlineData(429, null)]
    [InlineData(200, """{"balance":{"denom":"uosmo","amount":"5"}}""")]
    [InlineData(200, """{"balance":{"denom":"uatom","amount":"1.5"}}""")]
    [InlineData(200, """{"balance":{"denom":"uatom","amount":5}}""")]
    [InlineData(200, """{"code":3,"message":"invalid address"}""")]
    [InlineData(200, """{"balance":{"denom":5,"amount":"5"}}""")]
    [InlineData(200, """{"balance":"uatom"}""")]
    public void Anything_else_is_unknown_never_zero(int status, string? json)
    {
        Assert.Null(Parse(status, json));
    }
}
