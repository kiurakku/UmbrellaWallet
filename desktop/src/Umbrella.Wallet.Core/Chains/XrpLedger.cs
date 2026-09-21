using System.Globalization;
using System.Text.Json;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// Reading an XRP Ledger answer (roadmap N.4).
///
/// Kept apart from the network call so the one decision that matters can be tested offline: telling a
/// real zero from an unknown. On the XRP Ledger an address that has never received the network's
/// reserve does not exist as an account, and the server says so with <c>actNotFound</c>. That is an
/// answer — the balance IS zero — and is reported as zero. Anything else that is not a clean success
/// is "could not read", never zero (MANIFESTO §4).
/// </summary>
public static class XrpLedger
{
    /// <summary>XRP has six decimal places; the ledger counts in "drops".</summary>
    public const decimal DropsPerXrp = 1_000_000m;

    /// <summary>The JSON-RPC body for <c>account_info</c> at the last validated ledger — never the
    /// open one, whose balance can still change.</summary>
    public static object AccountInfoRequest(string address) => new
    {
        method = "account_info",
        @params = new[] { new { account = address, ledger_index = "validated" } },
    };

    /// <summary>
    /// The XRP balance in a <c>result</c> object from <c>account_info</c>: the balance for a success,
    /// zero for an account the ledger does not have yet, null for anything else.
    /// </summary>
    public static decimal? ParseAccountInfo(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) return null;

        var status = result.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;

        if (status == "error")
        {
            var error = result.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            return error == "actNotFound" ? 0m : null;
        }

        if (status != "success") return null;

        // Only a VALIDATED answer counts: an unvalidated one can still be rolled back.
        if (!result.TryGetProperty("validated", out var v) || v.ValueKind != JsonValueKind.True) return null;

        if (!result.TryGetProperty("account_data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("Balance", out var balance) ||
            balance.ValueKind != JsonValueKind.String)
            return null;

        // Drops are an integer in a string. Integer parsing only: a decimal point or a sign here is not
        // a balance the ledger could have produced.
        return long.TryParse(balance.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var drops)
            ? drops / DropsPerXrp
            : null;
    }
}
