using System.Text.Json;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>A reviewed transparent Zcash send: the coins, the fee, and the height it expires at.</summary>
public sealed record ZecSendQuote(
    string From,
    string To,
    decimal Amount,
    ulong AmountZat,
    ulong FeeZat,
    decimal FeeZec,
    ulong ChangeZat,
    uint ExpiryHeight,
    uint BranchId,
    IReadOnlyList<ZcashUtxo> Inputs,
    byte[] FromScript,
    byte[] ToScript,
    bool ChangeSweptToFee)
{
    public int InputCount => Inputs.Count;
}

/// <summary>
/// How a Zcash broadcast ended. <see cref="Unclear"/> is its own answer: the transaction is signed
/// and may already be relayed, so it is never offered as a retry.
/// </summary>
public sealed record ZecSendResult(bool Ok, string? TxId, string? Error, bool Unclear = false);

/// <summary>
/// Real transparent (t-addr) Zcash sending — the last chain in the catalogue that could receive but
/// not send (roadmap N.8).
///
/// Everything consensus-critical happens on this device: the coins are read from a public explorer,
/// but the transaction is built and signed here, with the ZIP-243 signature hash bound to the
/// consensus branch id in force at the height it will be mined under. That binding is what stops a
/// signature being replayed onto another Zcash fork, and getting it wrong costs nothing — the network
/// simply refuses the transaction.
///
/// This is Zcash's PUBLIC side. Nothing here is shielded, and the wallet says so rather than letting
/// the coin's reputation imply privacy it is not providing.
/// </summary>
public sealed class ZcashTransactionSender
{
    private const string Api = "https://api.blockchair.com/zcash";

    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    /// <summary>Where a finished transaction can be looked at.</summary>
    public static string ExplorerFor(string txId) => $"blockchair.com/zcash/transaction/{txId}";

    /// <summary>
    /// Reads the address's confirmed coins and the chain tip, then plans the spend. Nothing is signed
    /// here, and nothing is guessed: an explorer that cannot be read is an error, never an empty
    /// wallet — "you have no coins" and "I could not ask" must not look the same to the user.
    /// </summary>
    public async Task<(ZecSendQuote? Quote, string? Error)> PrepareAsync(
        string fromAddress, string toAddress, decimal amount, CancellationToken ct = default)
    {
        var (from, fromError) = ZcashAddress.TryDecode(fromAddress);
        if (from is null) return (null, fromError);
        if (from.Kind != ZcashAddressKind.PublicKeyHash)
            return (null, "This wallet spends from t1 addresses only.");

        var (to, toError) = ZcashAddress.TryDecode(toAddress);
        if (to is null) return (null, toError);

        if (string.Equals(fromAddress.Trim(), toAddress.Trim(), StringComparison.Ordinal))
            return (null, "That is this wallet's own address — the payment would only pay the network fee.");

        if (!ZcashTransactions.TryToZatoshi(amount, out var amountZat))
            return (null, "Enter a positive ZEC amount with at most 8 decimal places.");

        var (utxos, utxoError) = await FetchUtxosAsync(fromAddress.Trim(), ct);
        if (utxos is null) return (null, utxoError);

        var height = await FetchHeightAsync(ct);
        if (height is not { } tip)
            return (null, "Could not read the Zcash chain tip, and a transaction cannot be dated without it.");

        var (plan, planError) = ZcashSendRules.Plan(utxos, amountZat, to.ScriptPubKey, from.ScriptPubKey);
        if (plan is null) return (null, planError);

        // Signed for the upgrade in force at the NEXT block — the earliest it can be mined — and set to
        // expire before any upgrade that follows, so it can never outlive the rules it was signed under.
        var next = (uint)tip + 1;
        var branch = ZcashTransactions.ConsensusBranchId(next);
        if (ZcashTransactions.ExpiryHeight(next) is not { } expiry)
            return (null, "A Zcash network upgrade activates within the next few blocks. Send once it is through.");

        return (new ZecSendQuote(
            From: fromAddress.Trim(),
            To: toAddress.Trim(),
            Amount: amount,
            AmountZat: plan.AmountZat,
            FeeZat: plan.FeeZat,
            FeeZec: ZcashTransactions.ToZec(plan.FeeZat),
            ChangeZat: plan.ChangeZat,
            ExpiryHeight: expiry,
            BranchId: branch,
            Inputs: plan.Inputs,
            FromScript: from.ScriptPubKey,
            ToScript: plan.ToScript,
            ChangeSweptToFee: plan.ChangeSweptToFee), null);
    }

    /// <summary>
    /// Signs the reviewed spend and broadcasts it. The transaction id is computed BEFORE the broadcast,
    /// so an answer that never arrives can still be settled against the chain instead of being called
    /// a failure the user would retry — and pay twice for.
    /// </summary>
    public async Task<ZecSendResult> SignAndBroadcastAsync(ZecSendQuote quote, Key key, CancellationToken ct = default)
    {
        // The key must be the one behind the address the user reviewed. A mismatch would sign for coins
        // this transaction does not spend; building that silently is worse than refusing to build it.
        var derivedAddress = ZcashAddress.EncodePublicKeyHash(key.PubKey.Hash.ToBytes());
        if (!string.Equals(derivedAddress, quote.From, StringComparison.Ordinal))
            return new ZecSendResult(false, null, "This transaction does not belong to the unlocked wallet.");

        byte[] raw;
        string txId;
        try
        {
            (raw, txId) = Build(quote, key);
        }
        catch (Exception ex)
        {
            return new ZecSendResult(false, null, $"Could not build the Zcash transaction: {ex.Message}");
        }

        var hex = Convert.ToHexString(raw).ToLowerInvariant();

        bool httpOk;
        string body;
        try
        {
            using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", hex) });
            using var res = await Http.PostAsync($"{Api}/push/transaction", content, ct);
            httpOk = res.IsSuccessStatusCode;
            body = await res.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            // The request left this device and the answer did not come back. That is not a failure.
            return await SettleAsync(txId, $"Zcash did not answer: {ex.Message}", ct);
        }

        return UtxoBroadcast.Classify(httpOk, body) switch
        {
            UtxoBroadcastAnswer.Accepted => new ZecSendResult(true, txId, null),
            UtxoBroadcastAnswer.Rejected => new ZecSendResult(false, null, Explain(body)),
            _ => await SettleAsync(txId, Explain(body), ct),
        };
    }

    /// <summary>
    /// Builds and signs the reviewed spend, returning the wire bytes and the id they hash to. Pure:
    /// no network, so the exact bytes a send would publish can be checked in a test.
    /// </summary>
    public static (byte[] Raw, string TxId) Build(ZecSendQuote quote, Key key)
    {
        var inputs = quote.Inputs
            .Select(u => new ZcashInput(ReverseHex(u.TxId), u.Index, u.Zatoshi, quote.FromScript))
            .ToList();

        var outputs = new List<ZcashOutput> { new(quote.AmountZat, quote.ToScript) };
        if (quote.ChangeZat > 0) outputs.Add(new ZcashOutput(quote.ChangeZat, quote.FromScript));

        var scriptSigs = new List<byte[]>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var sigHash = ZcashTransactions.SigHash(
                inputs, outputs, i, quote.FromScript, ZcashTransactions.SigHashAll,
                lockTime: 0, expiryHeight: quote.ExpiryHeight, branchId: quote.BranchId);

            // Zcash verifies transparent signatures exactly as Bitcoin does: a low-S DER signature over
            // the digest, with the hash type appended.
            var der = key.Sign(new uint256(sigHash), useLowR: false).ToDER();
            scriptSigs.Add(ZcashTransactions.SignatureScript(der, key.PubKey.ToBytes()));
        }

        var raw = ZcashTransactions.Serialize(inputs, outputs, scriptSigs, lockTime: 0, expiryHeight: quote.ExpiryHeight);
        return (raw, ZcashTransactions.TxId(raw));
    }

    /// <summary>
    /// Asks the chain whether the transaction is there after an answer that settled nothing. Found
    /// means sent; not found means unclear — never failed, because it may still be in a mempool.
    /// </summary>
    private static async Task<ZecSendResult> SettleAsync(string txId, string? reason, CancellationToken ct)
    {
        try
        {
            using var res = await Http.GetAsync($"{Api}/dashboards/transaction/{txId}", ct);
            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty(txId, out var entry) &&
                    entry.ValueKind == JsonValueKind.Object &&
                    entry.TryGetProperty("transaction", out var tx) &&
                    tx.ValueKind == JsonValueKind.Object)
                {
                    return new ZecSendResult(true, txId, null);
                }
            }
        }
        catch (Exception)
        {
            // Still unclear — fall through.
        }

        var because = reason is null ? string.Empty : $": {reason}";
        return new ZecSendResult(false, txId,
            $"Zcash gave no clear answer{because}. The payment {txId} may still be on its way — check the explorer before sending again.",
            Unclear: true);
    }

    /// <summary>The address's unspent coins, or why they could not be read.</summary>
    private static async Task<(IReadOnlyList<ZcashUtxo>? Utxos, string? Error)> FetchUtxosAsync(
        string address, CancellationToken ct)
    {
        try
        {
            using var res = await Http.GetAsync(
                $"{Api}/dashboards/address/{Uri.EscapeDataString(address)}?limit=0", ct);
            if ((int)res.StatusCode is 429 or 430)
                return (null, "Zcash's public explorer is rate-limiting this device. Wait a minute and try again.");
            if (!res.IsSuccessStatusCode)
                return (null, $"Could not read this address's coins (the explorer answered {(int)res.StatusCode}).");

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return (ParseUtxos(doc.RootElement), null);
        }
        catch (Exception ex)
        {
            return (null, $"Could not reach the Zcash explorer: {ex.Message}");
        }
    }

    /// <summary>
    /// The coins in an explorer answer. Separate from the request so the shape the wallet depends on
    /// is checked in a test rather than only in production.
    /// </summary>
    public static IReadOnlyList<ZcashUtxo> ParseUtxos(JsonElement root)
    {
        var utxos = new List<ZcashUtxo>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return utxos;

        foreach (var entry in data.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object) continue;
            if (!entry.Value.TryGetProperty("utxo", out var list) || list.ValueKind != JsonValueKind.Array) continue;

            foreach (var u in list.EnumerateArray())
            {
                if (!u.TryGetProperty("transaction_hash", out var h) || h.GetString() is not { Length: 64 } hash) continue;
                if (!u.TryGetProperty("index", out var idx) || !idx.TryGetUInt32(out var index)) continue;
                if (!u.TryGetProperty("value", out var val) || !val.TryGetUInt64(out var value)) continue;

                // Blockchair dates an unconfirmed coin -1; only confirmed coins are spendable here.
                long? height = u.TryGetProperty("block_id", out var b) && b.TryGetInt64(out var bh) && bh > 0
                    ? bh
                    : null;
                utxos.Add(new ZcashUtxo(hash, index, value, height));
            }
        }

        return utxos;
    }

    /// <summary>The chain tip, or null when it could not be read.</summary>
    private static async Task<long?> FetchHeightAsync(CancellationToken ct)
    {
        try
        {
            using var res = await Http.GetAsync($"{Api}/stats", ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            if (data.TryGetProperty("best_block_height", out var best) && best.TryGetInt64(out var h)) return h;
            if (data.TryGetProperty("blocks", out var blocks) && blocks.TryGetInt64(out var b)) return b - 1;
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The explorer's own words, trimmed to something a person can read.</summary>
    public static string Explain(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("context", out var context) &&
                context.TryGetProperty("error", out var error) &&
                error.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (Exception)
        {
            // Not JSON — fall through to the raw text.
        }

        var trimmed = body.Trim();
        return trimmed.Length == 0 ? "the explorer said nothing"
            : trimmed.Length > 300 ? trimmed[..300] : trimmed;
    }

    /// <summary>
    /// A transaction id as an explorer prints it is a uint256 in reverse; a transaction input carries
    /// the bytes the other way round.
    /// </summary>
    private static byte[] ReverseHex(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        Array.Reverse(bytes);
        return bytes;
    }
}
