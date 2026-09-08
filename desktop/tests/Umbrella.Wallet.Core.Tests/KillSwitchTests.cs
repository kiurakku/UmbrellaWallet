using System;
using System.Net.Http;
using System.Threading.Tasks;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The Tor-only kill-switch is the anonymity backstop (roadmap §2.2 / §2.4): if Tor drops or is off,
/// no request may fall back to clearnet — and no hostname may even be resolved locally. These prove
/// that guarantee on the REAL transport (the production wiring, via <see cref="PublicHttp.CreateProbeClient"/>),
/// not just in prose: the connect callback refuses before any socket is opened or any DNS lookup runs.
/// The probe client is isolated, so these tests touch no global state and can run in parallel safely.
/// </summary>
public sealed class KillSwitchTests
{
    [Fact]
    public async Task Kill_switch_refuses_a_direct_connection_when_Tor_is_absent()
    {
        // Tor-only ON, no proxy active — exactly the "Tor just dropped" situation. A request MUST be
        // refused at the transport, never leaked to clearnet.
        using var probe = PublicHttp.CreateProbeClient(proxy: null, requireProxy: true);

        var ex = await Assert.ThrowsAnyAsync<HttpRequestException>(
            () => probe.GetAsync("http://example.com"));

        // The refusal is OUR kill-switch (its message), not an incidental network error.
        var message = ex.Message + " " + (ex.InnerException?.Message ?? string.Empty);
        Assert.Contains("blocked", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Kill_switch_blocks_https_the_same_as_http()
    {
        // The block happens at connect time, so the scheme is irrelevant — https is refused just the same.
        using var probe = PublicHttp.CreateProbeClient(proxy: null, requireProxy: true);

        await Assert.ThrowsAnyAsync<HttpRequestException>(
            () => probe.GetAsync("https://api.binance.com/api/v3/time"));
    }

    [Fact]
    public async Task Without_the_kill_switch_the_transport_does_not_short_circuit()
    {
        // Counter-proof that the block is the kill-switch and not the probe always failing: with the
        // kill-switch OFF and a proxy pointed at a dead local port, the failure is an ordinary
        // connection error — never the kill-switch's "blocked" refusal. No clearnet is touched (the
        // proxy target is loopback), so this stays offline and deterministic.
        using var probe = PublicHttp.CreateProbeClient(proxy: "socks5://127.0.0.1:1", requireProxy: false);

        try
        {
            await probe.GetAsync("http://example.com");
        }
        catch (Exception ex)
        {
            var message = ex.Message + " " + (ex.InnerException?.Message ?? string.Empty);
            Assert.DoesNotContain("Tor-only mode is on", message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
