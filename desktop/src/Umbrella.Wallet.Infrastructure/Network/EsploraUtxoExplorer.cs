using System.Text.Json;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// The live <see cref="IUtxoExplorer"/> over an Esplora-style public explorer (Blockstream for BTC,
/// litecoinspace for LTC), read through the shared Tor/proxy-aware <see cref="PublicHttp"/>. All
/// failures surface as thrown exceptions so the scanner treats them as "unknown", never "empty".
/// </summary>
public sealed class EsploraUtxoExplorer : IUtxoExplorer
{
    private static HttpClient Http => PublicHttp.Shared;

    private readonly string _base;

    public EsploraUtxoExplorer(string baseUrl) => _base = baseUrl.TrimEnd('/');

    public static string BaseUrlFor(string symbol) => symbol.ToUpperInvariant() switch
    {
        "BTC" => "https://blockstream.info/api",
        "LTC" => "https://litecoinspace.org/api",
        _ => throw new NotSupportedException($"No Esplora explorer for {symbol}."),
    };

    public static EsploraUtxoExplorer For(string symbol) => new(BaseUrlFor(symbol));

    public async Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct)
    {
        using var res = await Http.GetAsync($"{_base}/address/{Uri.EscapeDataString(address)}", ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        var txCount = TxCount(root, "chain_stats") + TxCount(root, "mempool_stats");
        return new AddressActivity(txCount > 0, txCount);
    }

    public async Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct)
    {
        using var res = await Http.GetAsync($"{_base}/address/{Uri.EscapeDataString(address)}/utxo", ct);
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
    }

    private static int TxCount(JsonElement root, string statsProperty) =>
        root.TryGetProperty(statsProperty, out var stats) && stats.TryGetProperty("tx_count", out var tc)
            ? tc.GetInt32()
            : 0;
}
