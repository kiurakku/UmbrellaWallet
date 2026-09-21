using System.Text.Json;
using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// The live <see cref="IUtxoExplorer"/> over BlockCypher, for chains that have no Esplora instance
/// (Dogecoin). Read through the shared Tor/proxy-aware <see cref="PublicHttp"/>. Every failure throws
/// so the scanner treats it as "unknown", never "empty" — critical, or a flaky API could make the
/// wallet believe an address holds nothing and quietly skip its coins.
///
/// Only txid/vout/value/confirmed are needed: the spender re-derives each input's scriptPubKey from its
/// own HD path, so the explorer never has to supply scripts.
/// </summary>
public sealed class BlockCypherUtxoExplorer : IUtxoExplorer
{
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);

    private readonly string _coin; // BlockCypher coin slug, e.g. "doge"

    public BlockCypherUtxoExplorer(string coin) => _coin = coin;

    public static string CoinFor(string symbol) => symbol.ToUpperInvariant() switch
    {
        "DOGE" => "doge",
        _ => throw new NotSupportedException($"No BlockCypher explorer for {symbol}."),
    };

    public static BlockCypherUtxoExplorer For(string symbol) => new(CoinFor(symbol));

    /// <summary>What this build ships with, before any choice of the user's.</summary>
    public const string DefaultRoot = "https://api.blockcypher.com";

    private string Base => $"{ChainEndpoints.Resolve(_coin.ToUpperInvariant(), DefaultRoot)}/v1/{_coin}/main";

    public async Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct)
    {
        using var res = await ExplorerHttp.GetAsync(Http, $"{Base}/addrs/{Uri.EscapeDataString(address)}/balance", ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var n = doc.RootElement.TryGetProperty("final_n_tx", out var t) ? t.GetInt32() : 0;
        return new AddressActivity(n > 0, n);
    }

    public async Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct)
    {
        // unspentOnly keeps the list to spendable outputs; limit is generous so a busy address isn't
        // silently truncated (which would under-report the balance and could strand coins).
        using var res = await ExplorerHttp.GetAsync(Http,
            $"{Base}/addrs/{Uri.EscapeDataString(address)}?unspentOnly=true&limit=2000", ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;

        var list = new List<ExplorerUtxo>();
        // Confirmed outputs live in "txrefs"; mempool ones in "unconfirmed_txrefs". The spender only
        // ever selects confirmed inputs, but we surface both with the right flag and let it decide.
        foreach (var key in new[] { "txrefs", "unconfirmed_txrefs" })
        {
            if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in arr.EnumerateArray())
            {
                if (!item.TryGetProperty("tx_hash", out var h) || h.GetString() is not { } txid) continue;
                if (!item.TryGetProperty("tx_output_n", out var vo)) continue;
                var vout = vo.GetInt32();
                if (vout < 0) continue; // -1 marks an input reference, not a spendable output
                // A spent output must never be offered as an input, even if it slipped into the list.
                if (item.TryGetProperty("spent", out var sp) && sp.ValueKind == JsonValueKind.True) continue;
                var value = item.TryGetProperty("value", out var v) ? v.GetInt64() : 0L;
                if (value <= 0) continue;
                var confirmations = item.TryGetProperty("confirmations", out var c) ? c.GetInt32() : 0;
                list.Add(new ExplorerUtxo(txid, vout, value, confirmations > 0));
            }
        }

        return list;
    }
}
