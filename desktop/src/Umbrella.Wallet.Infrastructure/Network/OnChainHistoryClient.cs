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

    /// <summary>Any Esplora-style explorer (Blockstream for BTC, litecoinspace for LTC…).</summary>
    public async Task<IReadOnlyList<ChainTx>> GetEsploraAsync(
        string apiBase, string address, string symbol, string explorerTxBase, CancellationToken ct = default)
    {
        try
        {
            var url = $"{apiBase}/address/{Uri.EscapeDataString(address)}/txs";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return [];
            var json = await res.Content.ReadAsStringAsync(ct);
            return ParseEsplora(json, address, symbol, explorerTxBase);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Native TRX transfers for a TRON base58 (T…) address, via Trongrid.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetTronNativeAsync(
        string address, int limit = 30, CancellationToken ct = default)
    {
        string meHex;
        try { meHex = TronBase58ToHex(address); } catch { return []; }
        try
        {
            var url = $"https://api.trongrid.io/v1/accounts/{Uri.EscapeDataString(address)}" +
                      $"/transactions?limit={limit}&only_confirmed=true";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return [];
            var json = await res.Content.ReadAsStringAsync(ct);
            return ParseTronNative(json, meHex);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Confirmed Bitcoin transactions, via Blockstream's keyless API.</summary>
    public Task<IReadOnlyList<ChainTx>> GetBitcoinAsync(string address, CancellationToken ct = default) =>
        GetEsploraAsync("https://blockstream.info/api", address, "BTC", "https://blockstream.info/tx/", ct);

    /// <summary>Confirmed Litecoin transactions, via litecoinspace (same Esplora API).</summary>
    public Task<IReadOnlyList<ChainTx>> GetLitecoinAsync(string address, CancellationToken ct = default) =>
        GetEsploraAsync("https://litecoinspace.org/api", address, "LTC", "https://litecoinspace.org/tx/", ct);

    /// <summary>
    /// Bitcoin Cash transactions, via Haskoin's keyless <c>transactions/full</c> API — the same explorer
    /// the wallet already uses for BCH balances and UTXOs (Blockchair's free tier IP-blacklists a busy
    /// caller). Haskoin takes the bare CashAddr, so the "bitcoincash:" scheme is stripped from the query;
    /// addresses in the response carry the scheme, and the parser compares on the scheme-stripped form.
    /// </summary>
    public async Task<IReadOnlyList<ChainTx>> GetBitcoinCashAsync(
        string address, int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var bare = StripCashScheme(address);
            var url = $"https://api.haskoin.com/bch/address/{Uri.EscapeDataString(bare)}" +
                      $"/transactions/full?limit={limit}";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return [];
            var json = await res.Content.ReadAsStringAsync(ct);
            return ParseHaskoinFull(json, address, "BCH", "https://blockchair.com/bitcoin-cash/transaction/");
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Native ETH transfers, via Blockscout's keyless API (Etherscan-compatible shape).</summary>
    public async Task<IReadOnlyList<ChainTx>> GetEthereumAsync(string address, CancellationToken ct = default)
    {
        try
        {
            var url = "https://eth.blockscout.com/api?module=account&action=txlist" +
                      $"&address={Uri.EscapeDataString(address)}&sort=desc&page=1&offset=25";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return [];
            var json = await res.Content.ReadAsStringAsync(ct);
            return ParseEvmTxlist(json, address, "ETH", "https://etherscan.io/tx/", 18);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Native TON transfers for a user-friendly (UQ/EQ) address, via toncenter's keyless API.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetTonAsync(string address, int limit = 30, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://toncenter.com/api/v2/getTransactions?address={Uri.EscapeDataString(address)}" +
                      $"&limit={limit}&archival=true";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return [];
            var json = await res.Content.ReadAsStringAsync(ct);
            return ParseTon(json, address);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Native ADA transfers for a bech32 (addr1…) address, via Koios's keyless API (two calls:
    /// recent tx hashes, then their full input/output sets).</summary>
    public async Task<IReadOnlyList<ChainTx>> GetCardanoAsync(string address, int limit = 25, CancellationToken ct = default)
    {
        try
        {
            using var body1 = new System.Net.Http.StringContent(
                $"{{\"_addresses\":[\"{address}\"]}}", System.Text.Encoding.UTF8, "application/json");
            using var res1 = await Http.PostAsync("https://api.koios.rest/api/v1/address_txs", body1, ct);
            if (!res1.IsSuccessStatusCode) return [];
            var j1 = await res1.Content.ReadAsStringAsync(ct);

            var hashes = new List<string>();
            using (var d1 = JsonDocument.Parse(j1))
            {
                if (d1.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var t in d1.RootElement.EnumerateArray())
                    {
                        var h = Str(t, "tx_hash");
                        if (h.Length > 0) hashes.Add(h);
                        if (hashes.Count >= limit) break;
                    }
            }
            if (hashes.Count == 0) return [];

            var hashList = string.Join(",", hashes.Select(h => $"\"{h}\""));
            using var body2 = new System.Net.Http.StringContent(
                $"{{\"_tx_hashes\":[{hashList}]}}", System.Text.Encoding.UTF8, "application/json");
            using var res2 = await Http.PostAsync("https://api.koios.rest/api/v1/tx_info", body2, ct);
            if (!res2.IsSuccessStatusCode) return [];
            var j2 = await res2.Content.ReadAsStringAsync(ct);
            return ParseCardanoTxInfo(j2, address);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Native SOL transfers for an address, via the public Solana RPC. Two calls: recent
    /// signatures, then a single batched <c>getTransaction</c> for them. Best-effort — the free RPC
    /// rate-limits, so it falls back to an empty list rather than blocking the feed.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetSolanaAsync(string address, int limit = 12, CancellationToken ct = default)
    {
        try
        {
            var sigReq = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"getSignaturesForAddress\",\"params\":[\"" +
                         address + "\",{\"limit\":" + limit + "}]}";
            using var body1 = new System.Net.Http.StringContent(sigReq, System.Text.Encoding.UTF8, "application/json");
            using var res1 = await Http.PostAsync("https://api.mainnet-beta.solana.com", body1, ct);
            if (!res1.IsSuccessStatusCode) return [];
            var j1 = await res1.Content.ReadAsStringAsync(ct);

            var sigs = new List<string>();
            using (var d1 = JsonDocument.Parse(j1))
                if (d1.RootElement.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.Array)
                    foreach (var s in r.EnumerateArray())
                    {
                        var sg = Str(s, "signature");
                        if (sg.Length > 0) sigs.Add(sg);
                    }
            if (sigs.Count == 0) return [];

            var reqs = string.Join(",", sigs.Select((sg, i) =>
                "{\"jsonrpc\":\"2.0\",\"id\":" + i + ",\"method\":\"getTransaction\",\"params\":[\"" + sg +
                "\",{\"encoding\":\"json\",\"maxSupportedTransactionVersion\":0}]}"));
            using var body2 = new System.Net.Http.StringContent("[" + reqs + "]", System.Text.Encoding.UTF8, "application/json");
            using var res2 = await Http.PostAsync("https://api.mainnet-beta.solana.com", body2, ct);
            if (!res2.IsSuccessStatusCode) return [];
            var j2 = await res2.Content.ReadAsStringAsync(ct);
            return ParseSolanaTransactions(j2, address);
        }
        catch
        {
            return [];
        }
    }

    // ---- Parsers (static + string-in, so they can be unit-tested without the network) ----

    /// <summary>
    /// Parses a batched Solana <c>getTransaction</c> response into normalized SOL rows by the net change
    /// to the address's own lamport balance (pre → post). For the fee-payer (index 0) the network fee is
    /// backed out of a send so the shown amount is what actually left. Lamports are 1e9.
    /// </summary>
    public static List<ChainTx> ParseSolanaTransactions(string json, string me)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : new[] { root };

        foreach (var item in items)
        {
            if (!item.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object) continue;
            if (!result.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object) continue;
            if (meta.TryGetProperty("err", out var errEl) && errEl.ValueKind != JsonValueKind.Null) continue; // failed tx

            if (!result.TryGetProperty("transaction", out var txn) ||
                !txn.TryGetProperty("message", out var msg) ||
                !msg.TryGetProperty("accountKeys", out var keys) || keys.ValueKind != JsonValueKind.Array) continue;

            var idx = -1;
            var k = 0;
            foreach (var key in keys.EnumerateArray())
            {
                if (key.ValueKind == JsonValueKind.String && key.GetString() == me) { idx = k; break; }
                k++;
            }
            if (idx < 0) continue;

            var pre = meta.TryGetProperty("preBalances", out var pb) ? LamportsAt(pb, idx) : 0;
            var post = meta.TryGetProperty("postBalances", out var pob) ? LamportsAt(pob, idx) : 0;
            var fee = Long(meta, "fee");
            var delta = post - pre;
            if (delta == 0) continue;

            var incoming = delta > 0;
            var lamports = incoming ? delta : (-delta - (idx == 0 ? fee : 0));
            if (lamports <= 0) continue;

            var amount = ScaleDown(lamports.ToString(CultureInfo.InvariantCulture), 9);
            var ts = Long(result, "blockTime") * 1000;
            var sig = txn.TryGetProperty("signatures", out var sgs) && sgs.ValueKind == JsonValueKind.Array &&
                      sgs.GetArrayLength() > 0 ? sgs[0].GetString() ?? "" : "";
            outList.Add(new ChainTx(incoming ? "Received" : "Sent", "SOL", amount, "", ts,
                $"https://solscan.io/tx/{sig}", sig));
        }
        return outList;
    }

    private static long LamportsAt(JsonElement arr, int idx)
    {
        if (arr.ValueKind != JsonValueKind.Array) return 0;
        var i = 0;
        foreach (var e in arr.EnumerateArray())
        {
            if (i == idx) return e.TryGetInt64(out var v) ? v : 0;
            i++;
        }
        return 0;
    }

    /// <summary>
    /// Parses a Koios <c>tx_info</c> payload into normalized ADA rows, using the same net-effect logic as
    /// Bitcoin: sum the address's own inputs vs outputs (lovelace, 1e6) to tell a send from a receive.
    /// </summary>
    public static List<ChainTx> ParseCardanoTxInfo(string json, string me)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return outList;

        foreach (var tx in doc.RootElement.EnumerateArray())
        {
            var hash = Str(tx, "tx_hash");
            long inMine = 0, outMine = 0, outToOthers = 0;
            string firstOther = "";

            if (tx.TryGetProperty("inputs", out var ins) && ins.ValueKind == JsonValueKind.Array)
                foreach (var i in ins.EnumerateArray())
                    if (AdaAddr(i) == me) inMine += Long(i, "value");

            if (tx.TryGetProperty("outputs", out var outs) && outs.ValueKind == JsonValueKind.Array)
                foreach (var o in outs.EnumerateArray())
                {
                    var addr = AdaAddr(o);
                    var val = Long(o, "value");
                    if (addr == me) outMine += val;
                    else { outToOthers += val; if (firstOther.Length == 0) firstOther = addr; }
                }

            var ts = Long(tx, "tx_timestamp") * 1000;
            var incoming = outMine - inMine >= 0;
            var shown = incoming ? outMine : outToOthers;
            var amount = ScaleDown(shown.ToString(CultureInfo.InvariantCulture), 6);
            if (amount == "0") continue;
            var counter = incoming ? "" : firstOther;
            outList.Add(new ChainTx(incoming ? "Received" : "Sent", "ADA", amount, counter, ts,
                $"https://cardanoscan.io/transaction/{hash}", hash));
        }
        return outList;
    }

    private static string AdaAddr(JsonElement e) =>
        e.TryGetProperty("payment_addr", out var pa) && pa.TryGetProperty("bech32", out var b) &&
        b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";

    /// <summary>
    /// Parses a toncenter <c>getTransactions</c> payload into normalized rows. TON semantics: an
    /// incoming transfer carries the value on <c>in_msg</c> (with a real source); an outgoing one has an
    /// empty in_msg source and the transfers sit in <c>out_msgs</c>. Amounts are nanoTON (1e9).
    /// </summary>
    public static List<ChainTx> ParseTon(string json, string me)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return outList;

        foreach (var tx in result.EnumerateArray())
        {
            var ts = Long(tx, "utime") * 1000;
            var hash = tx.TryGetProperty("transaction_id", out var tid) ? Str(tid, "hash") : "";
            var explorer = $"https://tonviewer.com/transaction/{Uri.EscapeDataString(hash)}";

            // Incoming: in_msg has a non-empty source and a positive value.
            if (tx.TryGetProperty("in_msg", out var inMsg))
            {
                var src = Str(inMsg, "source");
                var val = Long(inMsg, "value");
                if (src.Length > 0 && val > 0)
                {
                    outList.Add(new ChainTx("Received", "TON", ScaleDown(val.ToString(CultureInfo.InvariantCulture), 9),
                        src, ts, explorer, hash));
                    continue; // a plain incoming tx doesn't also count as a send
                }
            }

            // Outgoing: the wallet's own external message fans out to out_msgs.
            if (tx.TryGetProperty("out_msgs", out var outs) && outs.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in outs.EnumerateArray())
                {
                    var dst = Str(m, "destination");
                    var val = Long(m, "value");
                    if (dst.Length == 0 || val <= 0) continue;
                    outList.Add(new ChainTx("Sent", "TON", ScaleDown(val.ToString(CultureInfo.InvariantCulture), 9),
                        dst, ts, explorer, hash));
                }
            }
        }
        return outList;
    }

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

    public static List<ChainTx> ParseBitcoin(string json, string me) =>
        ParseEsplora(json, me, "BTC", "https://blockstream.info/tx/");

    /// <summary>Parses any Esplora /address/{a}/txs payload (BTC, LTC…) into normalized rows.</summary>
    public static List<ChainTx> ParseEsplora(string json, string me, string symbol, string explorerTxBase)
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
                incoming ? "Received" : "Sent", symbol, amount, counter, ts,
                explorerTxBase + hash, hash));
        }
        return outList;
    }

    /// <summary>
    /// Parses a Haskoin <c>transactions/full</c> payload into normalized rows, using the same net-effect
    /// logic as Bitcoin: sum the address's own inputs vs outputs (satoshis, 1e8) to tell a send from a
    /// receive, and for a send show the amount that actually left to others (change back to us and the fee
    /// excluded). CashAddr comparison ignores the "bitcoincash:" scheme so a scheme-carrying "me" — the
    /// form the wallet derives — still matches Haskoin's scheme-carrying addresses. A coinbase input has
    /// no address, which reads as empty and simply never matches "me".
    /// </summary>
    public static List<ChainTx> ParseHaskoinFull(string json, string me, string symbol, string explorerTxBase)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return outList;

        var meBare = StripCashScheme(me);
        foreach (var tx in doc.RootElement.EnumerateArray())
        {
            var hash = Str(tx, "txid");

            long inMine = 0, outMine = 0, outTotalToOthers = 0;
            string firstOtherOut = "";
            if (tx.TryGetProperty("inputs", out var ins) && ins.ValueKind == JsonValueKind.Array)
                foreach (var i in ins.EnumerateArray())
                    if (string.Equals(StripCashScheme(Str(i, "address")), meBare, StringComparison.Ordinal))
                        inMine += Long(i, "value");

            if (tx.TryGetProperty("outputs", out var outs) && outs.ValueKind == JsonValueKind.Array)
                foreach (var o in outs.EnumerateArray())
                {
                    var addr = Str(o, "address");
                    var val = Long(o, "value");
                    if (string.Equals(StripCashScheme(addr), meBare, StringComparison.Ordinal)) outMine += val;
                    else { outTotalToOthers += val; if (firstOtherOut.Length == 0) firstOtherOut = addr; }
                }

            var ts = Long(tx, "time") * 1000; // Haskoin's "time" is unix seconds (mempool: first-seen)
            var net = outMine - inMine; // sats
            bool incoming = net >= 0;
            long shownSats = incoming ? outMine : (inMine - outMine); // received-to-us, or sent-out incl. fee
            if (!incoming && outTotalToOthers > 0) shownSats = outTotalToOthers; // prefer the recipient amount
            var amount = ScaleDown(shownSats.ToString(CultureInfo.InvariantCulture), 8);
            if (amount == "0") continue;
            var counter = incoming ? "" : firstOtherOut;
            outList.Add(new ChainTx(
                incoming ? "Received" : "Sent", symbol, amount, counter, ts,
                explorerTxBase + hash, hash));
        }
        return outList;
    }

    /// <summary>Drops the "bitcoincash:" scheme from a CashAddr; leaves other strings unchanged.</summary>
    private static string StripCashScheme(string a) =>
        a.StartsWith("bitcoincash:", StringComparison.OrdinalIgnoreCase) ? a["bitcoincash:".Length..] : a;

    /// <summary>Decodes a TRON base58check (T…) address to its 21-byte hex form (41 + 20 bytes),
    /// as used inside raw transaction data. Throws on an invalid address.</summary>
    public static string TronBase58ToHex(string base58) =>
        Convert.ToHexString(NBitcoin.DataEncoders.Encoders.Base58Check.DecodeData(base58));

    /// <summary>Parses a Trongrid native /transactions payload into TRX transfer rows. <paramref name="meHex"/>
    /// is the user's own address in hex (41…), used to tell sends from receives.</summary>
    public static List<ChainTx> ParseTronNative(string json, string meHex)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return outList;

        foreach (var row in data.EnumerateArray())
        {
            if (!row.TryGetProperty("raw_data", out var raw) ||
                !raw.TryGetProperty("contract", out var contracts) ||
                contracts.ValueKind != JsonValueKind.Array) continue;

            foreach (var c in contracts.EnumerateArray())
            {
                if (Str(c, "type") != "TransferContract") continue;             // native TRX only
                if (!c.TryGetProperty("parameter", out var param) ||
                    !param.TryGetProperty("value", out var val)) continue;

                var owner = Str(val, "owner_address");
                var to = Str(val, "to_address");
                if (owner.Length == 0 && to.Length == 0) continue;

                var incoming = string.Equals(to, meHex, StringComparison.OrdinalIgnoreCase);
                var amount = ScaleDown(Long(val, "amount").ToString(CultureInfo.InvariantCulture), 6); // SUN→TRX
                if (amount == "0") continue;
                var ts = Long(row, "block_timestamp");
                var hash = Str(row, "txID");
                var counterHex = incoming ? owner : to;
                var counter = TryHexToTronBase58(counterHex);
                outList.Add(new ChainTx(
                    incoming ? "Received" : "Sent", "TRX", amount, counter, ts,
                    $"https://tronscan.org/#/transaction/{hash}", hash));
            }
        }
        return outList;
    }

    /// <summary>Best-effort hex(41…) → base58 (T…) for display; returns the hex unchanged on failure.</summary>
    private static string TryHexToTronBase58(string hex)
    {
        try
        {
            if (hex.Length == 0) return "";
            var bytes = Convert.FromHexString(hex);
            return NBitcoin.DataEncoders.Encoders.Base58Check.EncodeData(bytes);
        }
        catch
        {
            return hex;
        }
    }

    /// <summary>Parses an Etherscan/Blockscout <c>txlist</c> payload (native EVM transfers).</summary>
    public static List<ChainTx> ParseEvmTxlist(
        string json, string me, string symbol, string explorerTxBase, int decimals)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return outList;

        foreach (var tx in result.EnumerateArray())
        {
            if (Str(tx, "isError") == "1") continue;              // failed tx
            var from = Str(tx, "from");
            var to = Str(tx, "to");
            if (from.Length == 0 && to.Length == 0) continue;

            var amount = ScaleDown(Str(tx, "value"), decimals);
            if (amount == "0") continue;                          // contract call, no value moved

            var incoming = string.Equals(to, me, StringComparison.OrdinalIgnoreCase);
            var ts = Long(tx, "timeStamp") * 1000;                // seconds → ms
            var hash = Str(tx, "hash");
            var counter = incoming ? from : to;
            outList.Add(new ChainTx(
                incoming ? "Received" : "Sent", symbol, amount, counter, ts, explorerTxBase + hash, hash));
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
