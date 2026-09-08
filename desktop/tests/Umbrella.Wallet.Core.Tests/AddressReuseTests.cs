using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Secure &amp; anonymous roadmap 3.3 — the Receive screen must warn before handing out an address that
/// already has on-chain history, and must never claim an address is fresh unless something proved it.
/// </summary>
public sealed class AddressReuseTests
{
    [Fact]
    public async Task Address_with_history_is_reported_used()
    {
        var explorer = new StubExplorer { Used = { "addr-used" } };

        var verdict = await new AddressReuseInspector().InspectAsync(explorer, "addr-used");

        Assert.Equal(AddressUseState.Used, verdict);
    }

    [Fact]
    public async Task Untouched_address_is_reported_fresh()
    {
        var verdict = await new AddressReuseInspector().InspectAsync(new StubExplorer(), "addr-new");

        Assert.Equal(AddressUseState.Fresh, verdict);
    }

    /// <summary>A broken explorer must not silence the warning by answering "fresh".</summary>
    [Fact]
    public async Task Explorer_failure_is_unknown_never_fresh()
    {
        var explorer = new StubExplorer { Throw = true };

        var verdict = await new AddressReuseInspector().InspectAsync(explorer, "addr-any");

        Assert.Equal(AddressUseState.Unknown, verdict);
    }

    /// <summary>An index above the highest index discovery ever saw used is provably untouched offline.</summary>
    [Fact]
    public void Index_above_the_seen_used_floor_is_fresh_offline()
    {
        Assert.Equal(AddressUseState.Fresh, AddressReuseInspector.FromLocalState(4, lastSeenUsedExternalIndex: 3));
    }

    /// <summary>At or below the floor the local state proves nothing — it must defer, not guess.</summary>
    [Theory]
    [InlineData(0u, 3u)]
    [InlineData(3u, 3u)]
    public void Index_at_or_below_the_floor_is_unknown_offline(uint shown, uint floor)
    {
        Assert.Equal(AddressUseState.Unknown, AddressReuseInspector.FromLocalState(shown, floor));
    }

    /// <summary>With no scan state at all, nothing is known — the explorer has to answer.</summary>
    [Fact]
    public void No_local_state_is_unknown()
    {
        Assert.Equal(AddressUseState.Unknown, AddressReuseInspector.FromLocalState(0, null));
    }

    /// <summary>The offline floor short-circuits the network call when it can prove freshness.</summary>
    [Fact]
    public async Task Local_proof_of_freshness_skips_the_explorer()
    {
        var explorer = new StubExplorer { Throw = true };

        var verdict = await new AddressReuseInspector()
            .InspectAsync(explorer, "addr-9", shownIndex: 9, lastSeenUsedExternalIndex: 3);

        Assert.Equal(AddressUseState.Fresh, verdict);
        Assert.Equal(0, explorer.Calls);
    }

    /// <summary>Below the floor the explorer decides, so a reused #0 is still caught.</summary>
    [Fact]
    public async Task Below_the_floor_the_explorer_decides()
    {
        var explorer = new StubExplorer { Used = { "addr-0" } };

        var verdict = await new AddressReuseInspector()
            .InspectAsync(explorer, "addr-0", shownIndex: 0, lastSeenUsedExternalIndex: 3);

        Assert.Equal(AddressUseState.Used, verdict);
        Assert.Equal(1, explorer.Calls);
    }

    private sealed class StubExplorer : IUtxoExplorer
    {
        public HashSet<string> Used { get; } = new();
        public bool Throw { get; set; }
        public int Calls { get; private set; }

        public Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct)
        {
            Calls++;
            if (Throw) throw new HttpRequestException("simulated explorer failure");
            var used = Used.Contains(address);
            return Task.FromResult(new AddressActivity(used, used ? 2 : 0));
        }

        public Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ExplorerUtxo>>(Array.Empty<ExplorerUtxo>());
    }
}
