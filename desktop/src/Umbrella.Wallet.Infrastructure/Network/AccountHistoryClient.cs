using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// Transaction history for two account-based chains that had none: XRP and Stellar.
///
/// Each is read from the SAME server the balance comes from — the one the user chose, or the default —
/// so showing history adds no new party that learns the address. Each is best-effort: a server that
/// does not answer yields an empty list, and the Activity screen's coverage note keeps saying which
/// coins' history could not be read, rather than an empty list passing for "no transactions".
///
/// The explorer links are exactly the ones the Send screen stores for a transaction (scheme included),
/// so a payment made in this wallet and the same payment read back from the chain collapse into one row.
///
/// Cosmos Hub is NOT here, on purpose. Its public REST servers prune their transaction index: the same
/// search for an account with known sends answered with them once in nine tries across three servers,
/// and "0" the other eight — indistinguishable from an account that never moved anything. A history
/// that is usually empty for a reason nobody can see is worse than none, which the coverage note says.
/// </summary>
public sealed class AccountHistoryClient
{
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);

    /// <summary>Seconds between the Unix epoch and the XRP Ledger's (2000-01-01T00:00:00Z).</summary>
    private const long RippleEpoch = 946_684_800;

    // --- XRP -----------------------------------------------------------------------------------------

    /// <summary>Recent XRP payments to and from an account, newest first.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetXrpAsync(string address, int limit = 25, CancellationToken ct = default)
    {
        try
        {
            var root = ChainEndpoints.Resolve("XRP", "https://xrplcluster.com");
            var request = new
            {
                method = "account_tx",
                @params = new object[]
                {
                    new { account = address, limit, ledger_index_min = -1, ledger_index_max = -1, forward = false },
                },
            };
            using var res = await Http.PostAsJsonAsync(root, request, ct);
            if (!res.IsSuccessStatusCode) return [];
            return ParseXrp(await res.Content.ReadAsStringAsync(ct), address);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// The XRP payments in an <c>account_tx</c> answer.
    ///
    /// The amount shown is <c>delivered_amount</c> from the metadata, never the transaction's
    /// <c>Amount</c>. A "partial payment" can name a large Amount and deliver almost nothing; reading
    /// Amount is the classic way a wallet or exchange is fooled into crediting money that never arrived.
    /// Only validated, successful payments in XRP itself are listed; issued-currency payments and every
    /// other transaction type are left out rather than shown with an XRP figure they do not have.
    /// </summary>
    public static List<ChainTx> ParseXrp(string json, string me)
    {
        var list = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("transactions", out var txs) || txs.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var entry in txs.EnumerateArray())
        {
            if (entry.TryGetProperty("validated", out var v) && v.ValueKind == JsonValueKind.False) continue;

            // Newer servers call it tx_json (with the hash beside it); older ones call it tx.
            var tx = entry.TryGetProperty("tx_json", out var tj) ? tj : entry.TryGetProperty("tx", out var t) ? t : default;
            if (tx.ValueKind != JsonValueKind.Object || Str(tx, "TransactionType") != "Payment") continue;
            if (!entry.TryGetProperty("meta", out var meta) || Str(meta, "TransactionResult") != "tesSUCCESS") continue;

            // XRP is a string of drops; an issued currency is an object and is not this coin.
            if (!meta.TryGetProperty("delivered_amount", out var delivered) || delivered.ValueKind != JsonValueKind.String)
                continue;

            var from = Str(tx, "Account");
            var to = Str(tx, "Destination");
            var incoming = to == me;
            if (!incoming && from != me) continue;

            var hash = Str(entry, "hash");
            if (hash.Length == 0) hash = Str(tx, "hash");

            var ts = XrpCloseTime(entry, tx);

            list.Add(new ChainTx(
                incoming ? "Received" : "Sent", "XRP", OnChainHistoryClient.ScaleDown(delivered.GetString()!, 6),
                incoming ? from : to, ts, $"https://livenet.xrpl.org/transactions/{hash}", hash));
        }

        return list;
    }

    /// <summary>
    /// When the ledger holding the transaction closed, in Unix milliseconds, or 0 when the answer does not
    /// say. API v1 puts <c>date</c> (seconds since 2000) inside <c>tx</c>; API v2 and Clio put it — or
    /// <c>close_time_iso</c> — on the entry beside <c>tx_json</c>. All three are read, so a row never
    /// loses its time because the chosen server speaks the newer API.
    /// </summary>
    private static long XrpCloseTime(JsonElement entry, JsonElement tx)
    {
        var seconds = Long(tx, "date");
        if (seconds <= 0) seconds = Long(entry, "date");
        if (seconds > 0) return (seconds + RippleEpoch) * 1000;

        return DateTimeOffset.TryParse(Str(entry, "close_time_iso"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var at) ? at.ToUnixTimeMilliseconds() : 0;
    }

    // --- Stellar -------------------------------------------------------------------------------------

    /// <summary>Recent native XLM payments (and the account's creation) for a G… address, newest first.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetStellarAsync(string address, int limit = 25, CancellationToken ct = default)
    {
        try
        {
            var root = ChainEndpoints.Resolve("XLM", "https://horizon.stellar.org").TrimEnd('/');
            using var res = await Http.GetAsync(
                $"{root}/accounts/{Uri.EscapeDataString(address)}/payments?order=desc&limit={limit}", ct);
            // 404: an address the ledger does not know yet — no account, so no history.
            if (!res.IsSuccessStatusCode) return [];
            return ParseStellar(await res.Content.ReadAsStringAsync(ct), address);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// The native XLM movements in a Horizon <c>/payments</c> page: payments and account creations.
    /// Other assets, path payments and failed transactions are left out.
    /// </summary>
    public static List<ChainTx> ParseStellar(string json, string me)
    {
        var list = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("_embedded", out var embedded) ||
            !embedded.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var r in records.EnumerateArray())
        {
            if (r.TryGetProperty("transaction_successful", out var ok) && ok.ValueKind == JsonValueKind.False) continue;

            string from, to, amount;
            switch (Str(r, "type"))
            {
                case "payment" when Str(r, "asset_type") == "native":
                    from = Str(r, "from");
                    to = Str(r, "to");
                    amount = Str(r, "amount");
                    break;
                case "create_account":
                    from = Str(r, "funder");
                    to = Str(r, "account");
                    amount = Str(r, "starting_balance");
                    break;
                default:
                    continue;
            }

            var incoming = to == me;
            if (!incoming && from != me) continue;

            var hash = Str(r, "transaction_hash");
            var ts = DateTimeOffset.TryParse(Str(r, "created_at"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var at) ? at.ToUnixTimeMilliseconds() : 0;

            list.Add(new ChainTx(
                incoming ? "Received" : "Sent", "XLM", TrimAmount(amount),
                incoming ? from : to, ts, $"https://stellar.expert/explorer/public/tx/{hash}", hash));
        }

        return list;
    }

    // --- helpers -------------------------------------------------------------------------------------

    /// <summary>"12.5000000" → "12.5": Horizon prints seven decimals whatever the amount.</summary>
    private static string TrimAmount(string amount) =>
        amount.Contains('.') ? amount.TrimEnd('0').TrimEnd('.') : amount;

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? ""
            : "";

    private static long Long(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String &&
            long.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) return s;
        return 0;
    }
}
