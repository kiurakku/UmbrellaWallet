using System.Globalization;
using System.Text.Json;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>An XRP account as read before a send: its balance, next sequence and what it must keep.</summary>
public sealed record XrpAccountState(long BalanceDrops, uint Sequence, uint OwnerCount, uint Flags)
{
    public bool RequiresDestinationTag => (Flags & XrpSendRules.RequireDestTag) != 0;
    public bool DisallowsXrp => (Flags & XrpSendRules.DisallowXrp) != 0;
    public bool RequiresDepositAuth => (Flags & XrpSendRules.DepositAuth) != 0;
}

/// <summary>What an <c>account_info</c> answer said about an account.</summary>
public enum XrpAccountLookup
{
    /// <summary>The account exists; its state is attached.</summary>
    Found,
    /// <summary>The ledger has no such account: it has never received the reserve.</summary>
    NotFound,
    /// <summary>No usable answer. Never treated as either of the above.</summary>
    Unreadable,
}

/// <summary>The network's reserve and the last validated ledger, from <c>server_info</c>.</summary>
public sealed record XrpLedgerState(long ReserveBaseDrops, long ReserveIncrementDrops, long BaseFeeDrops, uint ValidatedLedger)
{
    /// <summary>What an account with <paramref name="ownerCount"/> objects must keep and can never send.</summary>
    public long LockedDrops(uint ownerCount) => ReserveBaseDrops + (ownerCount * ReserveIncrementDrops);
}

/// <summary>
/// Reading rippled's answers for a send (roadmap N.4). Kept apart from the network so every decision —
/// what the account can spend, whether the destination demands a tag, whether a submission is final —
/// is tested offline against answers in rippled's own shapes. Anything this cannot read is "unknown",
/// never a default: a guessed sequence or reserve is how a send fails on chain and still costs a fee.
/// </summary>
public static class XrpSendRules
{
    /// <summary>lsfRequireDestTag: the account refuses payments that carry no destination tag.</summary>
    public const uint RequireDestTag = 0x0002_0000;

    /// <summary>lsfDisallowXRP: the owner asked not to be sent XRP. The ledger does not enforce it; a
    /// wallet that respects it saves somebody an awkward refund.</summary>
    public const uint DisallowXrp = 0x0008_0000;

    /// <summary>lsfDepositAuth: only senders the account has approved can pay it.</summary>
    public const uint DepositAuth = 0x0100_0000;

    /// <summary>How many ledgers a signed payment stays valid for — about a minute and a half. After
    /// that it can never be included, which is what settles a submission whose answer never came.</summary>
    public const uint LedgerWindow = 20;

    /// <summary>The highest fee this wallet will pay without the network being plainly congested:
    /// 0.01 XRP, a thousand times the usual. The XRP Ledger burns the whole fee named, not what it needs.</summary>
    public const long MaxFeeDrops = 10_000;

    public static object AccountInfoRequest(string address, string ledger) => new
    {
        method = "account_info",
        @params = new[] { new { account = address, ledger_index = ledger } },
    };

    public static object ServerInfoRequest() => new { method = "server_info", @params = new[] { new { } } };

    public static object FeeRequest() => new { method = "fee", @params = new[] { new { } } };

    public static object SubmitRequest(string blobHex) => new
    {
        method = "submit",
        @params = new[] { new { tx_blob = blobHex, fail_hard = true } },
    };

    public static object TxRequest(string hash, uint minLedger, uint maxLedger) => new
    {
        method = "tx",
        @params = new[] { new { transaction = hash, binary = false, min_ledger = minLedger, max_ledger = maxLedger } },
    };

    public static object DepositAuthorizedRequest(string source, string destination) => new
    {
        method = "deposit_authorized",
        @params = new[] { new { source_account = source, destination_account = destination, ledger_index = "validated" } },
    };

    /// <summary>Whether an account that only takes approved senders has approved this one; null when
    /// the answer does not say.</summary>
    public static bool? ParseDepositAuthorized(JsonElement result) =>
        result.ValueKind == JsonValueKind.Object && Str(result, "status") == "success" &&
        result.TryGetProperty("deposit_authorized", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? a.GetBoolean()
            : null;

    /// <summary>
    /// An <c>account_info</c> result: the account, a clean "no such account", or unreadable. A
    /// validated answer is required unless <paramref name="current"/> — the open ledger is what gives
    /// the next sequence number, and it is never validated yet.
    /// </summary>
    public static (XrpAccountLookup Lookup, XrpAccountState? State, uint CurrentLedger) ParseAccount(JsonElement result, bool current)
    {
        if (result.ValueKind != JsonValueKind.Object) return (XrpAccountLookup.Unreadable, null, 0);
        var status = Str(result, "status");

        if (status == "error")
            return (Str(result, "error") == "actNotFound" ? XrpAccountLookup.NotFound : XrpAccountLookup.Unreadable, null, 0);
        if (status != "success") return (XrpAccountLookup.Unreadable, null, 0);
        if (!current && !(result.TryGetProperty("validated", out var v) && v.ValueKind == JsonValueKind.True))
            return (XrpAccountLookup.Unreadable, null, 0);

        uint ledger = 0;
        if (current && !(result.TryGetProperty("ledger_current_index", out var li) && li.TryGetUInt32(out ledger)))
            return (XrpAccountLookup.Unreadable, null, 0);

        if (!result.TryGetProperty("account_data", out var data) || data.ValueKind != JsonValueKind.Object)
            return (XrpAccountLookup.Unreadable, null, 0);

        if (!long.TryParse(Str(data, "Balance"), NumberStyles.None, CultureInfo.InvariantCulture, out var balance) ||
            !Uint(data, "Sequence", out var sequence) ||
            !Uint(data, "OwnerCount", out var owners) ||
            !Uint(data, "Flags", out var flags))
            return (XrpAccountLookup.Unreadable, null, 0);

        return (XrpAccountLookup.Found, new XrpAccountState(balance, sequence, owners, flags), ledger);
    }

    /// <summary>The reserve and validated ledger from a <c>server_info</c> result, or null.</summary>
    public static XrpLedgerState? ParseServerInfo(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || Str(result, "status") != "success") return null;
        if (!result.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object) return null;
        if (!info.TryGetProperty("validated_ledger", out var vl) || vl.ValueKind != JsonValueKind.Object) return null;

        if (!XrpDrops(vl, "reserve_base_xrp", out var reserveBase) ||
            !XrpDrops(vl, "reserve_inc_xrp", out var reserveInc) ||
            !XrpDrops(vl, "base_fee_xrp", out var baseFee) ||
            !Uint(vl, "seq", out var seq))
            return null;

        // A reserve of nothing would let the review promise an amount the ledger will refuse.
        if (reserveBase <= 0 || reserveInc < 0 || baseFee <= 0) return null;
        return new XrpLedgerState(reserveBase, reserveInc, baseFee, seq);
    }

    /// <summary>The fee that gets a transaction into the open ledger now, in drops, from a <c>fee</c>
    /// result — never below the network's minimum. Null when unreadable.</summary>
    public static long? ParseFee(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || Str(result, "status") != "success") return null;
        if (!result.TryGetProperty("drops", out var drops) || drops.ValueKind != JsonValueKind.Object) return null;

        long fee = 0;
        foreach (var name in new[] { "open_ledger_fee", "minimum_fee", "base_fee" })
        {
            if (!long.TryParse(Str(drops, name), NumberStyles.None, CultureInfo.InvariantCulture, out var d)) return null;
            fee = Math.Max(fee, d);
        }

        return fee > 0 ? fee : null;
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool Uint(JsonElement o, string name, out uint value)
    {
        value = 0;
        return o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetUInt32(out value);
    }

    /// <summary>An XRP figure (a JSON number or string) as whole drops; false if it is not exact.</summary>
    private static bool XrpDrops(JsonElement o, string name, out long drops)
    {
        drops = 0;
        if (!o.TryGetProperty(name, out var p)) return false;
        decimal xrp;
        if (p.ValueKind == JsonValueKind.Number) { if (!p.TryGetDecimal(out xrp)) return false; }
        else if (p.ValueKind != JsonValueKind.String ||
                 !decimal.TryParse(p.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out xrp))
            return false;

        var scaled = xrp * XrpLedger.DropsPerXrp;
        if (scaled < 0 || scaled != decimal.Truncate(scaled) || scaled > XrpTransactions.MaxDrops) return false;
        drops = (long)scaled;
        return true;
    }
}

/// <summary>Where a submitted XRP payment stands.</summary>
public enum XrpSubmitOutcome
{
    /// <summary>In a validated ledger and successful: final.</summary>
    Included,
    /// <summary>Not applied — refused by the server, or expired unincluded. Nothing was sent.</summary>
    Rejected,
    /// <summary>In a validated ledger but failed: final, only the fee was charged.</summary>
    FailedFeeCharged,
    /// <summary>The server took it provisionally; only a validated ledger can say more.</summary>
    Provisional,
    /// <summary>No answer either way. It may still be included until its last ledger.</summary>
    Unknown,
}

/// <summary>Reading <c>submit</c> and <c>tx</c> answers. Only a validated ledger is final on the XRP
/// Ledger: "tesSUCCESS" from <c>submit</c> is a forecast, not a result.</summary>
public static class XrpSubmit
{
    public static (XrpSubmitOutcome Outcome, string? Reason) ParseSubmit(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) return (XrpSubmitOutcome.Unknown, null);

        if (Str(result, "status") == "error")
        {
            // The server refused the request itself, so it relayed nothing.
            var message = Str(result, "error_exception") ?? Str(result, "error_message") ?? Str(result, "error");
            return (XrpSubmitOutcome.Rejected, message is null ? "The server refused the transaction." : $"The server refused the transaction: {message}");
        }

        var code = Str(result, "engine_result");
        if (code is null || code.Length < 3) return (XrpSubmitOutcome.Unknown, null);
        var text = Str(result, "engine_result_message");

        return code[..3] switch
        {
            // Applied to the open ledger, queued, or held for a later sequence: may still become final.
            "tes" or "ter" => (XrpSubmitOutcome.Provisional, null),
            // Would fail if applied now. Submitted with fail_hard, so it is normally not relayed at all —
            // but only a validated ledger can say, and the code is kept to explain an expiry.
            "tec" => (XrpSubmitOutcome.Provisional, text is null ? code : $"{code}: {text}"),
            // Malformed, failed locally, or can never apply: not relayed, nothing charged.
            "tem" or "tef" or "tel" => (XrpSubmitOutcome.Rejected, text is null ? code : $"{code}: {text}"),
            _ => (XrpSubmitOutcome.Unknown, null),
        };
    }

    /// <summary>
    /// A <c>tx</c> lookup of THIS transaction within its possible ledgers. Validated and successful is
    /// Included; validated with a tec code is a failure that cost the fee; not found with the server
    /// holding every ledger up to its last one is Rejected — it can never be included now. Everything
    /// else is still open.
    /// </summary>
    public static (XrpSubmitOutcome Outcome, string? Reason) ParseLookup(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) return (XrpSubmitOutcome.Unknown, null);

        if (Str(result, "status") == "error")
        {
            return Str(result, "error") == "txnNotFound" &&
                   result.TryGetProperty("searched_all", out var all) && all.ValueKind == JsonValueKind.True
                ? (XrpSubmitOutcome.Rejected, "The network did not include the payment before it expired. Nothing was sent.")
                : (XrpSubmitOutcome.Unknown, null);
        }

        if (!(result.TryGetProperty("validated", out var v) && v.ValueKind == JsonValueKind.True))
            return (XrpSubmitOutcome.Provisional, null);

        var code = result.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object
            ? Str(meta, "TransactionResult")
            : null;

        return code switch
        {
            "tesSUCCESS" => (XrpSubmitOutcome.Included, null),
            not null when code.StartsWith("tec", StringComparison.Ordinal) =>
                (XrpSubmitOutcome.FailedFeeCharged, $"The payment was included but failed ({code}); only the fee was charged."),
            _ => (XrpSubmitOutcome.Unknown, null),
        };
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
