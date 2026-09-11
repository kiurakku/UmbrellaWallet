using System.Linq;
using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Real requests to real explorers, to prove the endpoint choice moves actual traffic rather than
/// just a string in a settings file.
///
/// Named LiveExplorer so the normal run and CI skip it — the suite must never fail because somebody
/// else's server was down. Run it deliberately:
/// <code>dotnet test --filter FullyQualifiedName~LiveExplorerEndpoint</code>
///
/// The check that matters is AGREEMENT. Two independent Esplora instances answering the same question
/// about the same address must give the same answer; if they disagree, one of them is not reading the
/// chain this wallet thinks it is, and pointing a user at it would show them a balance that is not
/// theirs.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class LiveExplorerEndpointTests : IDisposable
{
    /// <summary>The genesis coinbase address — the most-examined address in Bitcoin, and one that will
    /// certainly still have history the next time anybody runs this.</summary>
    private const string Genesis = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa";

    /// <summary>Opens the registry so these tests reach the real internet — which is the entire point
    /// of them — and closes it again afterwards, so nothing scheduled later inherits live access.</summary>
    public LiveExplorerEndpointTests()
    {
        // The rest of the suite runs behind the wallet's own kill-switch, which refuses every
        // connection. These tests exist to make real ones, so they lift it - and put it straight back.
        PublicHttp.SetRequireProxy(false);
        ChainEndpoints.ClearAll();
    }

    public void Dispose() => TestDataIsolation.GoOffline();

    private static async Task<AddressActivityOrSkip> ActivityAsync(string baseUrl)
    {
        ChainEndpoints.SetOverride("BTC", baseUrl);
        Assert.Equal(baseUrl, EsploraUtxoExplorer.BaseUrlFor("BTC"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            var activity = await EsploraUtxoExplorer.For("BTC").GetActivityAsync(Genesis, cts.Token);
            return new AddressActivityOrSkip(activity.Used, activity.TxCount, Reachable: true);
        }
        catch
        {
            // Rate-limited or down. Reported, never failed: this test is about our wiring, not their
            // uptime, and Blockstream was already returning 429 while its alternatives answered.
            return new AddressActivityOrSkip(false, 0, Reachable: false);
        }
    }

    private readonly record struct AddressActivityOrSkip(bool Used, int TxCount, bool Reachable);

    [Fact]
    public async Task Independent_explorers_agree_about_the_same_address()
    {
        // Every instance this build offers, not a fixed pair: pinning two meant the whole check
        // quietly did nothing whenever one of them was rate-limited, which is most of the time for
        // Blockstream. Whichever answer must agree with each other.
        var answers = new List<AddressActivityOrSkip>();
        foreach (var option in ChainEndpoints.Known["BTC"])
            answers.Add(await ActivityAsync(option.BaseUrl));

        var reachable = answers.Where(a => a.Reachable).ToList();
        if (reachable.Count < 2) return;   // nothing proven, nothing broken

        Assert.All(reachable, a => Assert.True(a.Used));

        // Counts drift by a confirmation or two between instances; the address has tens of thousands,
        // so agreement to within a handful is agreement. Disagreement beyond that means one of them is
        // not reading the chain this wallet thinks it is.
        var spread = reachable.Max(a => a.TxCount) - reachable.Min(a => a.TxCount);
        Assert.InRange(spread, 0, 25);
    }

    [Fact]
    public async Task The_shipped_default_survives_one_instance_being_rate_limited()
    {
        // The bug this was written for: Esplora had a single base URL, so a 429 from Blockstream left
        // the wallet with no Bitcoin balance at all. With no override set, the other shipped instances
        // sit behind it, and one of them answers.
        ChainEndpoints.ClearAll();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var activity = await EsploraUtxoExplorer.For("BTC").GetActivityAsync(Genesis, cts.Token);

        Assert.True(activity.Used);
        Assert.True(activity.TxCount > 1000);
    }

    [Fact]
    public async Task A_chosen_server_is_never_quietly_replaced_by_a_fallback()
    {
        // The other half of the rule. Falling back is a kindness when the wallet picked the server;
        // it is a betrayal when the USER picked it, because the whole point of choosing was to keep
        // those addresses away from the default.
        ChainEndpoints.SetOverride("BTC", "https://endpoint-that-does-not-exist.invalid/api");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await Assert.ThrowsAnyAsync<Exception>(
            () => EsploraUtxoExplorer.For("BTC").GetActivityAsync(Genesis, cts.Token));
    }

    [Fact]
    public async Task A_chosen_explorer_is_the_one_actually_queried()
    {
        // The whole point: change the setting, and the bytes go somewhere else. Proven by pointing at
        // a host that does not exist and watching the call fail — if the override were ignored, the
        // default would answer and this would succeed.
        ChainEndpoints.SetOverride("BTC", "https://endpoint-that-does-not-exist.invalid/api");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await Assert.ThrowsAnyAsync<Exception>(
            () => EsploraUtxoExplorer.For("BTC").GetActivityAsync(Genesis, cts.Token));
    }

    [Fact]
    public async Task A_failure_reads_as_unknown_rather_than_as_an_empty_wallet()
    {
        // The rule the scanner depends on: an unreachable explorer must THROW, so "we could not ask"
        // is never silently recorded as "there is nothing there". A zero balance and an unanswered
        // question are not the same fact, and confusing them is how a wallet tells somebody their
        // money is gone.
        ChainEndpoints.SetOverride("BTC", "https://endpoint-that-does-not-exist.invalid/api");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var explorer = EsploraUtxoExplorer.For("BTC");

        await Assert.ThrowsAnyAsync<Exception>(() => explorer.GetUtxosAsync(Genesis, cts.Token));
    }
}
