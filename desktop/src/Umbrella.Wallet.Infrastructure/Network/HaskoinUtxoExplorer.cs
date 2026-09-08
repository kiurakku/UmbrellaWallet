using System.Text.Json;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// The live <see cref="IUtxoExplorer"/> over Haskoin (api.haskoin.com), for Bitcoin Cash — which has no
/// Esplora instance and whose Blockchair free tier IP-blacklists a busy caller (HTTP 430). Keyless, read
/// through the shared Tor/proxy-aware <see cref="PublicHttp"/>. Every failure throws so the scanner treats
/// it as "unknown", never "empty" — a flaky API must never make the wallet believe an address is empty and
/// quietly skip its coins. Haskoin accepts a CashAddr with or without the "bitcoincash:" prefix; we strip it.
///
/// Only txid/index/value/confirmed are needed: the spender re-derives each input's scriptPubKey from its
/// own HD path, so the explorer never has to supply scripts.
/// </summary>
public sealed class HaskoinUtxoExplorer : IUtxoExplorer
{
    private static HttpClient Http => PublicHttp.Shared;

    private readonly string _coin; // Haskoin coin slug, e.g. "bch"

    public HaskoinUtxoExplorer(string coin) => _coin = coin;

    public static string CoinFor(string symbol) => symbol.ToUpperInvariant() switch
    {
        "BCH" => "bch",
        _ => throw new NotSupportedException($"No Haskoin explorer for {symbol}."),
    };

    public static HaskoinUtxoExplorer For(string symbol) => new(CoinFor(symbol));

    private string Base => $"https://api.haskoin.com/{_coin}";

    /// <summary>Haskoin takes the bare CashAddr; the wallet stores it with the "bitcoincash:" scheme.</summary>
    private static string Strip(string address) =>
        address.StartsWith("bitcoincash:", StringComparison.OrdinalIgnoreCase)
            ? address["bitcoincash:".Length..]
            : address;

    public async Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct)
    {
        using var res = await Http.GetAsync($"{Base}/address/{Uri.EscapeDataString(Strip(address))}/balance", ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var n = doc.RootElement.TryGetProperty("txs", out var t) && t.TryGetInt32(out var v) ? v : 0;
        return new AddressActivity(n > 0, n);
    }

    public async Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct)
    {
        // limit is generous so a busy address isn't silently truncated (which would under-report the
        // balance and could strand coins).
        using var res = await Http.GetAsync(
            $"{Base}/address/{Uri.EscapeDataString(Strip(address))}/unspent?limit=2000", ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return ParseUnspent(json);
    }

    /// <summary>
    /// Parses a Haskoin <c>/unspent</c> array into UTXOs (static + string-in, so it can be unit-tested
    /// without the network). A confirmed output carries a <c>block</c> object with a height; a mempool one
    /// has <c>block: null</c>, which is surfaced as unconfirmed so the spender never selects it.
    /// </summary>
    public static IReadOnlyList<ExplorerUtxo> ParseUnspent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<ExplorerUtxo>();

        var list = new List<ExplorerUtxo>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("txid", out var h) || h.GetString() is not { } txid) continue;
            if (!item.TryGetProperty("index", out var vo) || !vo.TryGetInt32(out var vout)) continue;
            var value = item.TryGetProperty("value", out var v) && v.TryGetInt64(out var val) ? val : 0L;
            if (value <= 0) continue;
            var confirmed = item.TryGetProperty("block", out var b) && b.ValueKind == JsonValueKind.Object
                            && b.TryGetProperty("height", out var hh) && hh.ValueKind == JsonValueKind.Number;
            list.Add(new ExplorerUtxo(txid, vout, value, confirmed));
        }
        return list;
    }
}
