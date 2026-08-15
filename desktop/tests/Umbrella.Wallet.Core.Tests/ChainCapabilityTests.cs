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
        ChainId.Btc, ChainId.Eth, ChainId.Ltc, ChainId.Tron, ChainId.Sol, ChainId.Ton, ChainId.Ada,
    };

    [Fact]
    public void Dogecoin_can_receive_but_is_not_marked_sendable()
    {
        // DOGE derives a real address and syncs a balance, but has no send path — it must not be
        // presentable as a fully spendable ("Ready") coin.
        var doge = ChainCatalog.Get(ChainId.Doge);
        Assert.True(ChainCatalog.HasRealAddress(ChainId.Doge)); // can still receive
        Assert.False(doge.CanSend);
    }

    [Theory]
    [InlineData(ChainId.Btc)]
    [InlineData(ChainId.Eth)]
    [InlineData(ChainId.Ltc)]
    [InlineData(ChainId.Tron)]
    [InlineData(ChainId.Sol)]
    [InlineData(ChainId.Ton)]
    [InlineData(ChainId.Ada)]
    public void Sendable_chains_are_marked_can_send(ChainId id) =>
        Assert.True(ChainCatalog.Get(id).CanSend);

    [Fact]
    public void Only_the_known_sendable_supported_chains_are_shown_as_fully_ready()
    {
        // A coin is shown as fully "Ready" (spendable) only when it is Supported AND CanSend. This is
        // the exact predicate DeriveAccounts uses, pinned here so a future catalog edit can't quietly
        // present a non-sendable coin (like DOGE) as spendable again.
        var fullyReady = ChainCatalog.All
            .Where(c => c.Support == ChainSupportLevel.Supported && c.CanSend)
            .Select(c => c.Id)
            .OrderBy(id => id)
            .ToArray();

        Assert.Equal(Sendable.OrderBy(id => id).ToArray(), fullyReady);
        Assert.DoesNotContain(ChainId.Doge, fullyReady);
    }
}
