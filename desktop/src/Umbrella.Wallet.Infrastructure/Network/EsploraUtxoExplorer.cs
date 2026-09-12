using System.Text.Json;
using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// The live <see cref="IUtxoExplorer"/> over an Esplora-style public explorer (Blockstream for BTC,
/// litecoinspace for LTC), read through the shared Tor/proxy-aware <see cref="PublicHttp"/>. All
/// failures surface as thrown exceptions so the scanner treats them as "unknown", never "empty".
/// </summary>
public sealed class EsploraUtxoExplorer : IUtxoExplorer
{
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);

    /// <summary>The servers to try, in order. More than one only when the user has NOT chosen.</summary>
    private readonly IReadOnlyList<string> _bases;

    public EsploraUtxoExplorer(string baseUrl) : this([baseUrl]) { }

    public EsploraUtxoExplorer(IEnumerable<string> baseUrls)
    {
        _bases = baseUrls.Select(b => b.TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (_bases.Count == 0) throw new ArgumentException("At least one Esplora base URL is required.", nameof(baseUrls));
    }

    /// <summary>
    /// The API root for a chain: the user's chosen server when they have picked one, otherwise the
    /// shipped default. Reading it through <see cref="ChainEndpoints"/> is what makes the choice real
    /// — the adapter is built from a dozen call sites through <see cref="For"/>, so there is nowhere
    /// else the override could be applied consistently.
    /// </summary>
    public static string BaseUrlFor(string symbol) =>
        ChainEndpoints.Resolve(symbol, DefaultBaseUrlFor(symbol));

    /// <summary>What this build ships with, before any choice of the user's.</summary>
    public static string DefaultBaseUrlFor(string symbol) => symbol.ToUpperInvariant() switch
    {
        "BTC" => "https://blockstream.info/api",
        "LTC" => "https://litecoinspace.org/api",
        _ => throw new NotSupportedException($"No Esplora explorer for {symbol}."),
    };

    /// <summary>
    /// The explorer for a chain.
    ///
    /// With no choice made, the shipped alternatives sit behind the default as fallbacks: one public
    /// Esplora rate-limiting should not leave the wallet with no Bitcoin balance at all, and that is
    /// not hypothetical - Blockstream was returning 429 while two other instances answered the same
    /// question identically.
    ///
    /// When the user HAS chosen a server, there is no fallback. Quietly rerouting their addresses to
    /// somebody else is the behaviour the whole endpoint picker exists to end; if their server is
    /// down, the scan reports "unknown" and they can decide, exactly as with a chosen Monero node.
    /// </summary>
    public static EsploraUtxoExplorer For(string symbol)
    {
        if (ChainEndpoints.OverrideFor(symbol) is { } chosen) return new EsploraUtxoExplorer(chosen);

        var bases = new List<string> { DefaultBaseUrlFor(symbol) };
        if (ChainEndpoints.Known.TryGetValue(symbol, out var known))
            bases.AddRange(known.Select(k => k.BaseUrl));

        return new EsploraUtxoExplorer(bases);
    }

    /// <summary>
    /// Runs a request against each server in turn, returning the first real answer.
    ///
    /// The last failure is rethrown rather than swallowed: the scanner treats a thrown exception as
    /// "unknown" and an empty result as "nothing there", and those are different facts. Reporting a
    /// rate-limited explorer as an empty wallet is how somebody is told their money is gone.
    /// </summary>
    /// <summary>Index of the server that last answered. A gap-limit walk is forty-odd requests, and
    /// re-probing a dead host before each one would triple the traffic and the wait — and push the
    /// working server towards its own rate limit.</summary>
    private int _preferred;

    private async Task<T> TryEachAsync<T>(Func<string, Task<T>> request, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < _bases.Count; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var index = (_preferred + attempt) % _bases.Count;
            try
            {
                var result = await request(_bases[index]);
                _preferred = index;   // stay here for the rest of this scan
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { last = ex; }
        }

        throw last ?? new HttpRequestException("No Esplora server answered.");
    }

    public Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct) =>
        TryEachAsync(async host =>
        {
            using var res = await Http.GetAsync($"{host}/address/{Uri.EscapeDataString(address)}", ct);
            res.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            var txCount = TxCount(root, "chain_stats") + TxCount(root, "mempool_stats");
            return new AddressActivity(txCount > 0, txCount);
        }, ct);

    public Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct) =>
        TryEachAsync<IReadOnlyList<ExplorerUtxo>>(async host =>
        {
            using var res = await Http.GetAsync($"{host}/address/{Uri.EscapeDataString(address)}/utxo", ct);
            res.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var list = new List<ExplorerUtxo>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var txid = item.GetProperty("txid").GetString();
                if (txid is null) continue;
                var vout = item.GetProperty("vout").GetInt32();
                var value = item.GetProperty("value").GetInt64();
                var confirmed = !item.TryGetProperty("status", out var status) ||
                                !status.TryGetProperty("confirmed", out var c) || c.GetBoolean();
                list.Add(new ExplorerUtxo(txid, vout, value, confirmed));
            }

            return list;
        }, ct);

    private static int TxCount(JsonElement root, string statsProperty) =>
        root.TryGetProperty(statsProperty, out var stats) && stats.TryGetProperty("tx_count", out var tc)
            ? tc.GetInt32()
            : 0;
}
