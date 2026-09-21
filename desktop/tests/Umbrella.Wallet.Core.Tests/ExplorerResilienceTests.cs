using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Why balances read "server did not respond" when a server had in fact answered, or would have.
///
/// Three defects, each pinned here: a request that TIMED OUT was treated as the user cancelling, which
/// aborted the whole scan and skipped the fallback servers; nothing ever waited and retried a 429; and
/// every refresh re-walked twenty addresses on every branch, which is what earned the 429s. Plus the
/// scan cache surviving a lock, so the next wallet could be planned from the previous wallet's coins.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class ExplorerResilienceTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static readonly HdAddressDeriver Deriver = new();

    /// <summary>An explorer where chosen addresses are used, and chosen requests fail a given way.</summary>
    private sealed class FakeExplorer : IUtxoExplorer
    {
        public HashSet<string> Used { get; } = [];
        public Func<string, Exception?> Fail { get; init; } = _ => null;
        public List<string> Asked { get; } = [];

        public Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct)
        {
            lock (Asked) Asked.Add(address);
            if (Fail(address) is { } ex) return Task.FromException<AddressActivity>(ex);
            return Task.FromResult(new AddressActivity(Used.Contains(address), Used.Contains(address) ? 1 : 0));
        }

        public Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExplorerUtxo>>([new ExplorerUtxo(new string('a', 64), 0, 1000, true)]);
    }

    private static string Ext(uint i) => Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Ltc, 0, i).Address;

    [Fact]
    public async Task A_request_that_timed_out_is_an_unread_address_not_a_cancelled_scan()
    {
        // HttpClient reports a timeout as TaskCanceledException — an OperationCanceledException. The
        // scan must come back partial ("unknown"), not throw as if the user had pressed cancel.
        var timeout = new TaskCanceledException("A connection could not be established within the configured ConnectTimeout.");
        var explorer = new FakeExplorer { Fail = a => a == Ext(0) ? timeout : null };

        var result = await new UtxoAccountScanner().ScanAsync(Phrase, ChainId.Ltc, explorer, UtxoScanFloors.None);

        Assert.True(result.Partial);
    }

    [Fact]
    public async Task The_callers_own_cancel_still_stops_the_scan()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new UtxoAccountScanner().ScanAsync(Phrase, ChainId.Ltc, new FakeExplorer(), UtxoScanFloors.None, ct: cts.Token));
    }

    [Fact]
    public async Task A_refresh_rereads_every_known_address_and_only_a_short_look_ahead()
    {
        var explorer = new FakeExplorer();
        explorer.Used.Add(Ext(5));
        var floors = new UtxoScanFloors(LastIssuedExternalIndex: 2, LastSeenUsedExternalIndex: 5, null, null);

        var result = await new UtxoAccountScanner().ScanAsync(
            Phrase, ChainId.Ltc, explorer, floors, gapLimit: UtxoAccountScanner.RefreshGapLimit);

        Assert.False(result.Partial);
        Assert.Single(result.Utxos);                               // the coin on #5 is still found
        var external = explorer.Asked.Where(a => Enumerable.Range(0, 40).Any(i => Ext((uint)i) == a)).ToList();
        Assert.Equal(6 + UtxoAccountScanner.RefreshGapLimit, external.Count);   // #0..#5, then three past it
    }

    [Fact]
    public async Task A_full_walk_still_finds_money_far_past_the_last_known_address()
    {
        // The limit of the short refresh, stated rather than hidden: a coin fifteen addresses out is
        // found by the full walk (on unlock, then every thirty minutes), not by a refresh.
        var explorer = new FakeExplorer();
        explorer.Used.Add(Ext(15));

        var refresh = await new UtxoAccountScanner().ScanAsync(
            Phrase, ChainId.Ltc, explorer, UtxoScanFloors.None, gapLimit: UtxoAccountScanner.RefreshGapLimit);
        var full = await new UtxoAccountScanner().ScanAsync(Phrase, ChainId.Ltc, explorer, UtxoScanFloors.None);

        Assert.Empty(refresh.Utxos);
        Assert.Single(full.Utxos);
    }

    // --- ExplorerHttp ------------------------------------------------------------------------------

    private sealed class Scripted(params HttpStatusCode[] answers) : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var status = answers[Math.Min(Calls, answers.Length - 1)];
            Calls++;
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task A_429_is_waited_out_and_asked_again()
    {
        var handler = new Scripted(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        using var http = new HttpClient(handler);

        using var response = await ExplorerHttp.GetAsync(http, "https://resilience-test.invalid/a", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task A_definite_answer_is_returned_at_once()
    {
        var handler = new Scripted(HttpStatusCode.BadRequest, HttpStatusCode.OK);
        using var http = new HttpClient(handler);

        using var response = await ExplorerHttp.GetAsync(http, "https://resilience-test.invalid/b", CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_server_that_keeps_refusing_is_reported_not_waited_on_forever()
    {
        var handler = new Scripted(HttpStatusCode.TooManyRequests);
        using var http = new HttpClient(handler);

        using var response = await ExplorerHttp.GetAsync(http, "https://resilience-test.invalid/c", CancellationToken.None, attempts: 3);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public void Waiting_follows_Retry_After_but_never_sits_through_a_long_ban()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), ExplorerHttp.Backoff(1, new RetryConditionHeaderValue(TimeSpan.FromSeconds(3))));
        Assert.Equal(ExplorerHttp.MaxWait, ExplorerHttp.Backoff(1, new RetryConditionHeaderValue(TimeSpan.FromHours(1))));
        Assert.InRange(ExplorerHttp.Backoff(2, null), TimeSpan.FromMilliseconds(1600), TimeSpan.FromMilliseconds(1850));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public void Only_not_now_answers_are_retried(HttpStatusCode status, bool transient)
    {
        Assert.Equal(transient, ExplorerHttp.IsTransient(status));
        Assert.Equal(transient, ExplorerHttp.IsTransient(new HttpRequestException("x", null, status)));
    }

    [Fact]
    public void A_timeout_or_a_dropped_connection_counts_as_not_now_but_a_refusal_does_not()
    {
        Assert.True(ExplorerHttp.IsTransient(new TaskCanceledException("timeout")));
        Assert.True(ExplorerHttp.IsTransient(new HttpRequestException("reset", new System.Net.Sockets.SocketException(
            (int)System.Net.Sockets.SocketError.ConnectionReset))));

        // Refused, or a host that does not exist: asking again a second later changes nothing.
        Assert.False(ExplorerHttp.IsTransient(new HttpRequestException("refused", new System.Net.Sockets.SocketException(
            (int)System.Net.Sockets.SocketError.ConnectionRefused))));
        Assert.False(ExplorerHttp.IsTransient(new HttpRequestException("no such host", new System.Net.Sockets.SocketException(
            (int)System.Net.Sockets.SocketError.HostNotFound))));
        // The kill-switch refusing a clearnet request is a decision, not a hiccup.
        Assert.False(ExplorerHttp.IsTransient(new HttpRequestException("Tor-only mode is on but Tor is not connected")));
    }

    // --- the scan cache belongs to one wallet --------------------------------------------------------

    [Fact]
    public void Locking_forgets_what_the_last_wallets_scans_found()
    {
        TestDataIsolation.RestoreBaselineSettings();
        var dir = Path.Combine(Path.GetTempPath(), $"umbrella-lock-{Guid.NewGuid():N}");
        try
        {
            var vm = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(dir, "vault.json")));
            var scans = (Dictionary<string, UtxoScanResult>)typeof(MainViewModel)
                .GetField("_utxoScans", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;
            scans["BTC"] = new UtxoScanResult(ChainId.Btc, [], 5000, 0, 0, null, [], false);

            vm.LockVault();

            Assert.Empty(scans);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
