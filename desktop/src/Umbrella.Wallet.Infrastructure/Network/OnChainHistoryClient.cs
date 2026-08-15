using System.Globalization;
using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>One normalized on-chain transaction, ready to show in the Transactions list.</summary>
public sealed record ChainTx(
    string Kind,          // "Sent" | "Received"
    string Asset,         // "BTC", "USDT", "TRX"…
    string Amount,        // human amount, already signed-friendly (no sign; Kind carries direction)
    string Counterparty,  // the other address (truncated by the caller if desired)
    long UnixMs,          // for sorting / relative time
    string Explorer,      // full explorer URL for the tx
    string Hash);         // tx id, for de-duplication

/// <summary>
/// Reads real transaction history straight from public explorers for the user's OWN addresses, so
/// transactions made before the wallet was ever opened still show up. Keyless endpoints only, and it
/// rides the shared HTTP client — so it goes through Tor / the custom proxy exactly like balances.
///
/// First chains: TRON (TRC-20, e.g. USDT) and Bitcoin. Others (ETH/SOL/…) follow the same shape.
/// </summary>
public sealed class OnChainHistoryClient
{
    private static HttpClient Http => PublicHttp.Shared;

    /// <summary>TRC-20 token transfers (USDT and friends) for a TRON base58 address.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetTronTrc20Async(
        string address, int limit = 30, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.trongrid.io/v1/accounts/{Uri.EscapeDataString(address)}" +
                      $"/transactions/trc20?limit={limit}&only_confirmed=true";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return [];
            var json = await res.Content.ReadAsStringAsync(ct);
            return ParseTronTrc20(json, address);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Confirmed Bitcoin transactions for an address, via Blockstream's keyless API.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetBitcoinAsync(
        string address, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://blockstream.info/api/address/{Uri.EscapeDataString(address)}/txs";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return [];
            var json = await res.Content.ReadAsStringAsync(ct);
            return ParseBitcoin(json, address);
        }
        catch
        {
            return [];
        }
    }

    // ---- Parsers (static + string-in, so they can be unit-tested without the network) ----

    public static List<ChainTx> ParseTronTrc20(string json, string me)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return outList;

        foreach (var row in data.EnumerateArray())
        {
            var from = Str(row, "from");
            var to = Str(row, "to");
            if (from.Length == 0 && to.Length == 0) continue;

            var incoming = string.Equals(to, me, StringComparison.OrdinalIgnoreCase);
            var symbol = "TOKEN";
            var decimals = 6;
            if (row.TryGetProperty("token_info", out var ti))
            {
                symbol = Str(ti, "symbol") is { Length: > 0 } s ? s : symbol;
                if (ti.TryGetProperty("decimals", out var d) && d.TryGetInt32(out var dec)) decimals = dec;
            }

            var raw = Str(row, "value");
            var amount = ScaleDown(raw, decimals);
            var ts = Long(row, "block_timestamp");
            var hash = Str(row, "transaction_id");
            var counter = incoming ? from : to;
            outList.Add(new ChainTx(
                incoming ? "Received" : "Sent", symbol, amount, counter, ts,
                $"https://tronscan.org/#/transaction/{hash}", hash));
        }
        return outList;
    }

    public static List<ChainTx> ParseBitcoin(string json, string me)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return outList;

        foreach (var tx in doc.RootElement.EnumerateArray())
        {
            var hash = Str(tx, "txid");

            // Sum of inputs that are mine, and outputs that are mine (in satoshis).
            long inMine = 0, outMine = 0, outTotalToOthers = 0;
            string firstOtherOut = "";
            if (tx.TryGetProperty("vin", out var vin) && vin.ValueKind == JsonValueKind.Array)
                foreach (var v in vin.EnumerateArray())
                    if (v.TryGetProperty("prevout", out var po) &&
                        string.Equals(Str(po, "scriptpubkey_address"), me, StringComparison.Ordinal))
                        inMine += Long(po, "value");

            if (tx.TryGetProperty("vout", out var vout) && vout.ValueKind == JsonValueKind.Array)
                foreach (var o in vout.EnumerateArray())
                {
                    var addr = Str(o, "scriptpubkey_address");
                    var val = Long(o, "value");
                    if (string.Equals(addr, me, StringComparison.Ordinal)) outMine += val;
                    else { outTotalToOthers += val; if (firstOtherOut.Length == 0) firstOtherOut = addr; }
                }

            var ts = 0L;
            if (tx.TryGetProperty("status", out var st) && st.TryGetProperty("block_time", out var bt) &&
                bt.TryGetInt64(out var secs)) ts = secs * 1000;

            // Net effect on us: inputs we funded are "spent"; outputs to us are "received".
            var net = outMine - inMine; // sats
            bool incoming = net >= 0;
            long shownSats = incoming ? outMine : (inMine - outMine); // received-to-us, or sent-to-others
            if (!incoming && outTotalToOthers > 0) shownSats = outTotalToOthers;
            var amount = ScaleDown(shownSats.ToString(CultureInfo.InvariantCulture), 8);
            var counter = incoming ? "" : firstOtherOut;
            outList.Add(new ChainTx(
                incoming ? "Received" : "Sent", "BTC", amount, counter, ts,
                $"https://blockstream.info/tx/{hash}", hash));
        }
        return outList;
    }

    // ---- helpers ----
    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static long Long(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String &&
            long.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) return s;
        return 0;
    }

    /// <summary>Divides an integer string by 10^decimals and trims trailing zeros, culture-invariant.</summary>
    public static string ScaleDown(string raw, int decimals)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "0";
        if (!System.Numerics.BigInteger.TryParse(raw, out var value)) return "0";
        if (decimals <= 0) return value.ToString(CultureInfo.InvariantCulture);

        var divisor = System.Numerics.BigInteger.Pow(10, decimals);
        var whole = System.Numerics.BigInteger.DivRem(value, divisor, out var frac);
        if (frac.IsZero) return whole.ToString(CultureInfo.InvariantCulture);

        var fracStr = frac.ToString(CultureInfo.InvariantCulture).PadLeft(decimals, '0').TrimEnd('0');
        return $"{whole.ToString(CultureInfo.InvariantCulture)}.{fracStr}";
    }
}
