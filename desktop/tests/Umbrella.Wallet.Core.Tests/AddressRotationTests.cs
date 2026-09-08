using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

public sealed class AddressRotationTests
{
    // The canonical BIP39 all-zero-entropy test vector, shared by the BIP84 reference vectors below.
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

    /// <summary>
    /// The published BIP84 test vectors for the "abandon…about" seed. Pinning external #0, external
    /// #1 AND internal (change) #0 proves our change-level plumbing matches the standard exactly — so
    /// change outputs land on real, spendable, wallet-owned addresses.
    /// </summary>
    [Fact]
    public void Bip84_external_and_change_vectors_match_the_reference()
    {
        var d = new HdAddressDeriver();

        Assert.Equal("bc1qcr8te4kr609gcawutmrza0j4xv80jy8z306fyu",
            d.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, change: 0, index: 0).Address);
        Assert.Equal("bc1qnjg0jd8228aq7egyzacy8cys3knf9xvrerkf9g",
            d.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, change: 0, index: 1).Address);
        Assert.Equal("bc1q8c6fshw2dlwun7ekn9qwf37cu2rn755upcp6el",
            d.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, change: 1, index: 0).Address);

        // The external #0 leaf must equal what the normal receive path shows for index 0.
        Assert.Equal(d.DeriveReceiveAddress(Phrase, ChainId.Btc, 0).Address,
            d.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, change: 0, index: 0).Address);
    }

    [Theory]
    [InlineData(ChainId.Btc)]
    [InlineData(ChainId.Ltc)]
    [InlineData(ChainId.Doge)]
    public void Derived_account_address_key_and_script_all_agree(ChainId chain)
    {
        var d = new HdAddressDeriver();
        var (_, _, network, scriptType) = HdAddressDeriver.BitcoinLikeParams(chain);

        foreach (var change in new uint[] { 0, 1 })
        foreach (var index in new uint[] { 0, 1, 5 })
        {
            var acct = d.DeriveBitcoinLikeAt(Phrase, chain, change, index);

            // The address, the signing key and the scriptPubKey must describe the same output —
            // if these ever diverge, funds land where the wallet cannot sign.
            var fromKey = acct.PrivateKey.PubKey.GetAddress(scriptType, network);
            Assert.Equal(acct.Address, fromKey.ToString());
            Assert.Equal(acct.ScriptPubKey, fromKey.ScriptPubKey);
            Assert.Equal(chain, acct.Path.Chain);
            Assert.Equal(change, acct.Path.Change);
            Assert.Equal(index, acct.Path.Index);
        }

        // External and internal chains never collide at the same index.
        Assert.NotEqual(
            d.DeriveBitcoinLikeAt(Phrase, chain, 0, 0).Address,
            d.DeriveBitcoinLikeAt(Phrase, chain, 1, 0).Address);
    }

    [Fact]
    public void External_reservation_starts_at_one_and_persists()
    {
        var path = TempPath();
        try
        {
            var store = new AddressIndexStore(path);
            // Index 0 is the implicit default receive address, so the state starts empty…
            Assert.Null(store.GetState("w1", "BTC").LastIssuedExternalIndex);
            // …and the first hand-out is #1, not #0.
            Assert.Equal(1u, store.ReserveNextExternalIndex("w1", "BTC"));
            Assert.Equal(2u, store.ReserveNextExternalIndex("w1", "BTC"));

            // A fresh instance reads the persisted high-water mark back.
            Assert.Equal(2u, new AddressIndexStore(path).GetState("w1", "btc").LastIssuedExternalIndex); // case-insensitive
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Change_reservation_starts_at_zero()
    {
        var path = TempPath();
        try
        {
            var store = new AddressIndexStore(path);
            Assert.Equal(0u, store.ReserveNextChangeIndex("w1", "BTC"));
            Assert.Equal(1u, store.ReserveNextChangeIndex("w1", "BTC"));
            Assert.Equal(1u, new AddressIndexStore(path).GetState("w1", "BTC").LastIssuedInternalIndex);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void State_is_isolated_per_wallet_and_per_chain()
    {
        var path = TempPath();
        try
        {
            var store = new AddressIndexStore(path);
            store.ReserveNextExternalIndex("w1", "BTC");   // -> 1
            Assert.Null(store.GetState("w1", "LTC").LastIssuedExternalIndex);   // per-chain
            Assert.Null(store.GetState("w2", "BTC").LastIssuedExternalIndex);   // per-wallet
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void GetState_returns_a_copy_that_cannot_mutate_the_store()
    {
        var path = TempPath();
        try
        {
            var store = new AddressIndexStore(path);
            store.ReserveNextExternalIndex("w1", "BTC"); // -> 1
            var snap = store.GetState("w1", "BTC");
            snap.LastIssuedExternalIndex = 999;          // mutate the copy
            Assert.Equal(1u, store.GetState("w1", "BTC").LastIssuedExternalIndex); // store unchanged
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void RecordSeenUsed_raises_the_used_and_issued_floors()
    {
        var path = TempPath();
        try
        {
            var store = new AddressIndexStore(path);
            store.RecordSeenUsed("w1", "BTC", change: 0, index: 4);
            var s = store.GetState("w1", "BTC");
            Assert.Equal(4u, s.LastSeenUsedExternalIndex);
            Assert.Equal(4u, s.LastIssuedExternalIndex); // a used address is by definition issued

            store.RecordSeenUsed("w1", "BTC", change: 0, index: 2); // lower index must not lower the floor
            Assert.Equal(4u, store.GetState("w1", "BTC").LastSeenUsedExternalIndex);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Legacy_v1_flat_format_migrates_to_the_external_index()
    {
        var path = TempPath();
        try
        {
            // Old format: a flat { "wallet:CHAIN": intIndex } map.
            File.WriteAllText(path, "{\"w1:BTC\":3,\"w2:LTC\":0}");

            var store = new AddressIndexStore(path);
            Assert.Equal(3u, store.GetState("w1", "BTC").LastIssuedExternalIndex);
            Assert.Null(store.GetState("w2", "LTC").LastIssuedExternalIndex); // 0 -> "none issued"

            // A subsequent reservation continues from the migrated value and rewrites in the new format.
            Assert.Equal(4u, store.ReserveNextExternalIndex("w1", "BTC"));
            Assert.Contains("\"version\"", File.ReadAllText(path));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Reserve_fails_closed_when_the_state_cannot_be_written()
    {
        // A path whose parent is an existing FILE can never be created — Save must throw rather than
        // silently hand out an index the wallet would forget on restart (roadmap §3.1.6).
        var blocker = TempPath();
        File.WriteAllText(blocker, "x");
        var unwritable = Path.Combine(blocker, "addr-indexes.json");
        try
        {
            var store = new AddressIndexStore(unwritable);
            Assert.ThrowsAny<Exception>(() => store.ReserveNextExternalIndex("w1", "BTC"));
        }
        finally { Cleanup(blocker); }
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"umbrella-addridx-{Guid.NewGuid():N}.json");

    private static void Cleanup(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
