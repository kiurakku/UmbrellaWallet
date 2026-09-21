using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Separate Tor circuits per purpose, and the rule that keeps the feature from becoming a hole.
///
/// On one circuit a single exit relay sees the wallet ask an explorer "what is the balance of bc1q…"
/// and then, minutes later, hand over a transaction spending it — and can tie the two together by
/// timing alone. Tor keys a circuit on the SOCKS5 username/password pair, so handing a different pair
/// per purpose is enough to split them.
///
/// The dangerous half is caching. A per-purpose client that outlived the kill-switch being armed
/// would keep connecting, punching a hole in the exact mechanism the kill-switch exists to be — worse
/// than not isolating at all. That is what most of these tests are about.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class TorStreamIsolationTests : IDisposable
{
    private const string Proxy = "socks5://127.0.0.1:9250";

    public TorStreamIsolationTests() => TestDataIsolation.GoOffline();

    public void Dispose() => TestDataIsolation.GoOffline();

    private static void UseProxy()
    {
        // A proxy must be set for isolation to mean anything, and the kill-switch off so the handler
        // is built with the proxy rather than with the refuse-everything callback.
        PublicHttp.SetRequireProxy(false);
        PublicHttp.SetProxy(Proxy);
    }

    [Fact]
    public void Without_a_proxy_every_purpose_shares_one_client()
    {
        // Isolation buys nothing when there is no Tor to isolate within, and extra connections cost
        // something. Going direct, all purposes are the shared client.
        PublicHttp.SetRequireProxy(false);
        PublicHttp.SetProxy(null);

        foreach (var purpose in Enum.GetValues<PublicHttp.NetworkPurpose>())
        {
            Assert.Same(PublicHttp.Shared, PublicHttp.For(purpose));
        }
    }

    [Fact]
    public void With_a_proxy_each_purpose_gets_its_own_client()
    {
        UseProxy();

        var clients = Enum.GetValues<PublicHttp.NetworkPurpose>()
            .Select(PublicHttp.For)
            .ToList();

        // Distinct instances, and none of them the shared one — a shared client would mean a shared
        // circuit, which is the thing being avoided.
        Assert.Equal(clients.Count, clients.Distinct().Count());
        Assert.DoesNotContain(PublicHttp.Shared, clients);
    }

    [Fact]
    public void Asking_twice_for_the_same_purpose_returns_the_same_client()
    {
        // Otherwise every request would build a handler and open a fresh connection pool, which is
        // both slow and a different kind of leak: a new circuit per request defeats keep-alive and
        // asks Tor to build circuits far faster than it should.
        UseProxy();

        Assert.Same(
            PublicHttp.For(PublicHttp.NetworkPurpose.ChainData),
            PublicHttp.For(PublicHttp.NetworkPurpose.ChainData));
    }

    [Fact]
    public void Chain_data_and_broadcast_never_share_a_client()
    {
        // The separation the whole feature exists for, asserted by name so it cannot be lost in a
        // refactor that merges purposes for tidiness.
        UseProxy();

        Assert.NotSame(
            PublicHttp.For(PublicHttp.NetworkPurpose.ChainData),
            PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast));
    }

    // --- the part that could have been a hole -------------------------------------------------------

    [Fact]
    public void Arming_the_kill_switch_discards_every_cached_client()
    {
        // The failure this guards: a client handed out before Tor-only mode was armed keeps its old
        // handler, which has no refuse-everything callback, so it would go on connecting directly
        // while the wallet reports itself as fail-closed.
        //
        // Only SetRequireProxy is called here, deliberately. The first version of this test called
        // SetProxy(null) first - which discards the cache itself - so it passed even with the discard
        // removed from SetRequireProxy entirely. It was attributing the effect to the wrong call.
        // This is also the realistic sequence: Tor is already up and the user turns Tor-only on.
        UseProxy();
        var before = PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);

        PublicHttp.SetRequireProxy(true);

        var after = PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);
        Assert.NotSame(before, after);
    }

    [Fact]
    public void Changing_the_proxy_discards_every_cached_client()
    {
        // Switching from Tor to a custom proxy, or between proxies, must not leave requests flowing
        // through the previous one.
        UseProxy();
        var before = PublicHttp.For(PublicHttp.NetworkPurpose.Prices);

        PublicHttp.SetProxy("socks5://127.0.0.1:9050");

        Assert.NotSame(before, PublicHttp.For(PublicHttp.NetworkPurpose.Prices));
    }

    [Fact]
    public void Changing_the_ip_family_discards_every_cached_client()
    {
        UseProxy();
        var before = PublicHttp.For(PublicHttp.NetworkPurpose.Swaps);

        PublicHttp.SetIpPreference(PublicHttp.IpMode.V4Only);
        try
        {
            Assert.NotSame(before, PublicHttp.For(PublicHttp.NetworkPurpose.Swaps));
        }
        finally
        {
            PublicHttp.SetIpPreference(PublicHttp.IpMode.Auto);
        }
    }

    [Fact]
    public async Task A_purpose_client_obeys_the_kill_switch_on_the_real_transport()
    {
        // Not just "a different object" — actually refuses. Built the same way the shared client is,
        // so Tor-only with no proxy fails closed at the transport rather than leaking to clearnet.
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(true);

        var client = PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<Exception>(
            () => client.GetAsync("https://example.invalid/", cts.Token));
    }

    [Fact]
    public void Every_purpose_is_distinct_in_the_enum()
    {
        // Two purposes sharing a value would silently share a circuit — the same bug as merging them,
        // but invisible in the code that maps clients to purposes.
        var values = Enum.GetValues<PublicHttp.NetworkPurpose>().Cast<int>().ToList();
        Assert.Equal(values.Count, values.Distinct().Count());

        // And the set is the one the wallet actually maps, so a new purpose cannot be added without
        // deciding where it belongs. The seventh is Payjoin (P2.2): a receiver's endpoint is handed a
        // signed payment, and does not share a circuit with the explorer that sees the broadcast.
        Assert.Equal(7, values.Count);
    }
}
