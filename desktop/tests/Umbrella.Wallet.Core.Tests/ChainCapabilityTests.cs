using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the per-chain capability truth (roadmap §5.1): the wallet must never present a chain as
/// spendable that it cannot actually send. "Address can be derived" is not "fully supported".
/// </summary>
public sealed class ChainCapabilityTests
{
    // Chains the app can actually build, sign and broadcast a transaction on today.
    private static readonly ChainId[] Sendable =
    {
        ChainId.Btc, ChainId.Eth, ChainId.Ltc, ChainId.Doge, ChainId.Tron, ChainId.Sol, ChainId.Ton, ChainId.Ada,
        ChainId.Bch, // SIGHASH_FORKID spend via the shared spender; Haskoin UTXOs/broadcast
        ChainId.Xlm, // Payment / CreateAccount + memo, pinned to the Stellar Go SDK (StellarSendTests)
        ChainId.Near, // one Transfer from the implicit account, pinned to near-api-js (NearSendTests)
        ChainId.Xrp, // a plain XRP Payment + destination tag, pinned to xrpl.js (XrpSendTests)
        ChainId.Atom, // one bank MsgSend + memo, pinned to cosmjs (CosmosSendTests)
        ChainId.Dot, // transfer_keep_alive on Asset Hub; sr25519 pinned to polkadot.js, validated by the node (PolkadotSend*Tests)
        ChainId.Zec, // transparent v4 spend, ZIP-243 digest pinned to Zcash's own sighash vectors (ZcashSigHashTests)
    };

    [Fact]
    public void Dogecoin_can_receive_and_send()
    {
        // DOGE now has a real UTXO send path (BlockCypher UTXOs/fee/broadcast, signed by the same
        // spender as BTC/LTC), on top of deriving a real address and syncing a balance.
        var doge = ChainCatalog.Get(ChainId.Doge);
        Assert.True(ChainCatalog.HasRealAddress(ChainId.Doge));
        Assert.True(doge.CanReceive);
        Assert.True(doge.CanSend);
    }

    [Theory]
    [InlineData(ChainId.Btc)]
    [InlineData(ChainId.Eth)]
    [InlineData(ChainId.Ltc)]
    [InlineData(ChainId.Doge)]
    [InlineData(ChainId.Tron)]
    [InlineData(ChainId.Sol)]
    [InlineData(ChainId.Ton)]
    [InlineData(ChainId.Ada)]
    [InlineData(ChainId.Bch)]
    [InlineData(ChainId.Zec)]
    public void Sendable_chains_are_marked_can_send(ChainId id) =>
        Assert.True(ChainCatalog.Get(id).CanSend);

    [Fact]
    public void Only_the_known_sendable_supported_chains_are_shown_as_fully_ready()
    {
        // A coin is shown as fully "Ready" (spendable) only when it is Supported AND CanSend. This is
        // the exact predicate DeriveAccounts uses, pinned here so a future catalog edit can't quietly
        // present a non-sendable coin as spendable, or drop a sendable one.
        var fullyReady = ChainCatalog.All
            .Where(c => c.Support == ChainSupportLevel.Supported && c.CanSend)
            .Select(c => c.Id)
            .OrderBy(id => id)
            .ToArray();

        Assert.Equal(Sendable.OrderBy(id => id).ToArray(), fullyReady);
    }

    /// <summary>The per-capability matrix (§5.1) must be internally consistent — no impossible combos.</summary>
    [Fact]
    public void Every_chain_declares_consistent_capabilities()
    {
        foreach (var c in ChainCatalog.All)
        {
            if (c.CanSend) Assert.True(c.CanReceive, $"{c.Symbol}: CanSend but not CanReceive");
            if (ChainCatalog.HasRealAddress(c.Id)) Assert.True(c.CanReceive, $"{c.Symbol}: real address but not CanReceive");
            if (c.HasTokens) Assert.True(c.CanSend, $"{c.Symbol}: HasTokens but not CanSend");
            Assert.False(string.IsNullOrWhiteSpace(c.PrivacyNote), $"{c.Symbol}: missing privacy note");
        }
    }

    /// <summary>A coin the wallet cannot fully spend must never wear the most-finished maturity badge.</summary>
    [Fact]
    public void A_non_sendable_chain_is_never_stable()
    {
        foreach (var c in ChainCatalog.All.Where(c => !c.CanSend))
            Assert.NotEqual(ChainMaturity.Stable, c.Maturity);
    }
}
