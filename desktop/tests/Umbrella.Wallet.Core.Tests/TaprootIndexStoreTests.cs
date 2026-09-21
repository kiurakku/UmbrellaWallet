using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The Taproot branch keeps its own index counters (roadmap P2.1).
///
/// If the two branches shared one counter, a Taproot change output could be derived at an index the
/// Taproot scan never reaches — money sent to an address the wallet then does not look for. These
/// tests pin the separation and the round-trip that the next scan depends on.
/// </summary>
public sealed class TaprootIndexStoreTests
{
    [Fact]
    public void The_two_branches_reserve_change_on_independent_counters()
    {
        var path = TempPath();
        try
        {
            var store = new AddressIndexStore(path);
            var taproot = AddressIndexStore.BranchKey("BTC", UtxoScriptKind.Taproot);

            Assert.Equal(0u, store.ReserveNextChangeIndex("w1", "BTC"));
            Assert.Equal(1u, store.ReserveNextChangeIndex("w1", "BTC"));

            // The Taproot branch starts at its own zero, untouched by the SegWit reservations above…
            Assert.Equal(0u, store.ReserveNextChangeIndex("w1", taproot));

            // …and reserving there did not advance the SegWit counter.
            Assert.Equal(2u, store.ReserveNextChangeIndex("w1", "BTC"));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void The_default_branch_key_is_the_chain_itself()
    {
        // Existing wallets have state filed under "BTC". The default branch must keep reading it, or
        // every upgrade would forget where its change addresses were.
        Assert.Equal("BTC", AddressIndexStore.BranchKey("BTC", UtxoScriptKind.Default));
        Assert.NotEqual("BTC", AddressIndexStore.BranchKey("BTC", UtxoScriptKind.Taproot));
    }

    [Fact]
    public void A_recorded_scan_raises_the_Taproot_floors_the_next_scan_starts_from()
    {
        var path = TempPath();
        try
        {
            var store = new AddressIndexStore(path);
            var scan = new UtxoScanResult(
                ChainId.Btc, Array.Empty<OwnedUtxo>(), 0, 0,
                HighestUsedExternalIndex: 3, HighestUsedInternalIndex: null,
                ExternalAddresses: Array.Empty<string>(), Partial: false,
                HighestUsedTaprootExternalIndex: 7, HighestUsedTaprootInternalIndex: 2);

            store.RecordScan("w1", "BTC", scan);

            // Read back through a fresh instance: the floors must survive a restart.
            var floors = new AddressIndexStore(path).FloorsFor("w1", "BTC");

            Assert.Equal(3u, floors.LastSeenUsedExternalIndex);
            Assert.Null(floors.LastSeenUsedInternalIndex);
            Assert.Equal(7u, floors.TaprootLastSeenUsedExternalIndex);
            Assert.Equal(2u, floors.TaprootLastSeenUsedInternalIndex);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void A_reserved_Taproot_change_index_becomes_a_floor_the_scan_must_reach()
    {
        // The whole point of a separate counter: once change went out on Taproot internal #0, the
        // next scan has to walk at least that far on the Taproot internal chain even if the explorer
        // has not seen it yet.
        var path = TempPath();
        try
        {
            var store = new AddressIndexStore(path);
            store.ReserveNextChangeIndex("w1", AddressIndexStore.BranchKey("BTC", UtxoScriptKind.Taproot));

            var floors = store.FloorsFor("w1", "BTC");

            Assert.Equal(0u, floors.TaprootLastIssuedInternalIndex);
            Assert.Null(floors.LastIssuedInternalIndex);
        }
        finally { Cleanup(path); }
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"umbrella-addridx-tr-{Guid.NewGuid():N}.json");

    private static void Cleanup(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
