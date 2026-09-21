using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap P0.8 — network isolation, proved rather than asserted in prose.
///
/// "The kill-switch fails closed" has two possible meanings, and only one of them is worth anything.
/// The weak one is that the request errors out, which a firewall would also produce. The strong one
/// is that the wallet never opens the socket at all — no connect, no DNS lookup, nothing on the wire
/// that says a machine with this wallet on it just woke up.
///
/// So these tests put a listener on loopback and count what reaches it. With the kill-switch armed,
/// the answer has to be nothing; with it off, the same client reaches the same listener, which is
/// what stops this from being a test that passes because everything fails.
///
/// The last test covers the hole neither of those can see: code that never goes through
/// <see cref="PublicHttp"/> in the first place is code the kill-switch does not govern.
/// </summary>
[Trait("Category", "Isolation")]
public sealed class NetworkIsolationTests
{
    [Fact]
    public async Task With_the_kill_switch_armed_nothing_reaches_the_wire()
    {
        using var canary = new LoopbackCanary();
        using var client = PublicHttp.CreateProbeClient(proxy: null, requireProxy: true);

        var error = await Assert.ThrowsAnyAsync<HttpRequestException>(
            () => client.GetAsync(canary.Url));

        // It is OUR refusal, not an incidental failure...
        var message = error.Message + " " + (error.InnerException?.Message ?? string.Empty);
        Assert.Contains("blocked", message, StringComparison.OrdinalIgnoreCase);

        // ...and the socket was never opened. This is the claim that matters: a blocked request that
        // still connected would have already told an observer that this wallet is running.
        Assert.Equal(0, await canary.AcceptedWithin(TimeSpan.FromMilliseconds(400)));
    }

    /// <summary>
    /// The counter-proof. Without the kill-switch the very same wiring does reach the listener, so
    /// the test above is measuring the kill-switch and not a client that never works.
    /// </summary>
    [Fact]
    public async Task Without_the_kill_switch_the_same_client_does_reach_the_wire()
    {
        using var canary = new LoopbackCanary();
        using var client = PublicHttp.CreateProbeClient(proxy: null, requireProxy: false);

        // The canary answers nothing, so the request itself fails — the point is that it connected.
        try { await client.GetAsync(canary.Url); } catch { /* expected: no HTTP response */ }

        Assert.True(await canary.AcceptedWithin(TimeSpan.FromSeconds(3)) > 0);
    }

    /// <summary>
    /// Every request the wallet makes is supposed to be built by <see cref="PublicHttp"/>, because
    /// that is the single place the proxy and the kill-switch are wired in. An <c>HttpClient</c>
    /// constructed anywhere else inherits none of it and would keep talking after Tor dropped — a
    /// hole the user cannot see and the kill-switch cannot close.
    ///
    /// One exception is allowed and named: the Monero daemon client, which talks to a process on
    /// loopback with proxying explicitly off (routing a local RPC call through Tor would be absurd,
    /// and MoneroRpcService refuses to launch at all when Tor-only is on without a proxy).
    /// </summary>
    [Fact]
    public void No_http_client_is_built_outside_the_one_place_that_wires_the_kill_switch()
    {
        var root = RepoRoot();
        var allowed = new[]
        {
            Path.Combine("Umbrella.Wallet.Infrastructure", "Network", "PublicChainClients.cs"),
            Path.Combine("Umbrella.Wallet.Infrastructure", "Network", "MoneroRpcService.cs"),
        };

        var offenders = new List<string>();
        foreach (var file in Directory
                     .GetFiles(Path.Combine(root, "desktop", "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            if (allowed.Any(a => file.EndsWith(a, StringComparison.OrdinalIgnoreCase))) continue;

            var text = File.ReadAllText(file);
            if (Regex.IsMatch(text, @"new\s+(System\.Net\.Http\.)?HttpClient\s*\(") ||
                Regex.IsMatch(text, @"new\s+(System\.Net\.)?WebClient\s*\("))
            {
                offenders.Add(Path.GetRelativePath(root, file));
            }
        }

        Assert.Empty(offenders);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "desktop", "src"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root not found from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// A listener on loopback that counts connections. Nothing leaves the machine, so this stays an
    /// offline test — what is being measured is whether a socket was opened at all.
    /// </summary>
    private sealed class LoopbackCanary : IDisposable
    {
        private readonly TcpListener _listener;
        private int _accepted;

        public LoopbackCanary()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _ = AcceptLoopAsync();
        }

        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (true)
                {
                    var socket = await _listener.AcceptSocketAsync();
                    Interlocked.Increment(ref _accepted);
                    socket.Dispose();   // answer nothing: the connection itself is the signal
                }
            }
            catch
            {
                // listener stopped
            }
        }

        /// <summary>Connections seen, giving a connect attempt time to arrive before reporting zero.</summary>
        public async Task<int> AcceptedWithin(TimeSpan window)
        {
            var deadline = DateTime.UtcNow + window;
            while (DateTime.UtcNow < deadline)
            {
                if (Volatile.Read(ref _accepted) > 0) break;
                await Task.Delay(25);
            }

            return Volatile.Read(ref _accepted);
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* best effort */ }
        }
    }
}
