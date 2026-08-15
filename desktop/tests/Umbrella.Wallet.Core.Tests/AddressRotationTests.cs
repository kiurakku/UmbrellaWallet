using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

public sealed class AddressRotationTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    [Theory]
    [InlineData(ChainId.Btc)]
    [InlineData(ChainId.Ltc)]
    [InlineData(ChainId.Doge)]
    public void Each_index_gives_a_distinct_receive_address(ChainId chain)
    {
        var d = new HdAddressDeriver();
        var a0 = d.DeriveReceiveAddress(Phrase, chain, 0).Address;
        var a1 = d.DeriveReceiveAddress(Phrase, chain, 1).Address;
        var a2 = d.DeriveReceiveAddress(Phrase, chain, 2).Address;

        Assert.NotEqual(a0, a1);
        Assert.NotEqual(a1, a2);
        Assert.NotEqual(a0, a2);

        // Same index must be stable (re-derivable after a restart).
        Assert.Equal(a1, d.DeriveReceiveAddress(Phrase, chain, 1).Address);
    }

    [Fact]
    public void AddressIndexStore_increments_and_persists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"umbrella-addridx-{Guid.NewGuid():N}.json");
        try
        {
            var store = new AddressIndexStore(path);
            Assert.Equal(0, store.Get("w1", "BTC"));
            Assert.Equal(1, store.Increment("w1", "BTC"));
            Assert.Equal(2, store.Increment("w1", "BTC"));
            Assert.Equal(0, store.Get("w1", "LTC"));       // per-chain
            Assert.Equal(0, store.Get("w2", "BTC"));       // per-wallet

            // A fresh instance reads the persisted value back.
            Assert.Equal(2, new AddressIndexStore(path).Get("w1", "btc")); // case-insensitive
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
