using System.Globalization;
using System.Text.Json;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>Where a sent Solana transaction stands.</summary>
public enum SolSubmitOutcome
{
    /// <summary>In a confirmed block and successful.</summary>
    Included,
    /// <summary>Refused before any block (the node's simulation failed), or its blockhash expired
    /// without it landing. Nothing was sent.</summary>
    Rejected,
    /// <summary>In a block but failed: only the fee was charged.</summary>
    FailedFeeCharged,
    /// <summary>Seen but not confirmed yet, or not seen.</summary>
    Pending,
    /// <summary>No answer either way.</summary>
    Unknown,
}

/// <summary>
/// Reading Solana JSON-RPC answers for a send. Kept apart from the network so each decision is tested
/// offline; anything unreadable is unknown, never a default.
/// </summary>
public static class SolanaRpc
{
    public const decimal LamportsPerSol = 1_000_000_000m;

    /// <summary>The fee for one signature and no priority fee: 5,000 lamports.</summary>
    public const ulong BaseFeeLamports = 5_000;

    /// <summary>A positive SOL amount with at most nine decimal places, as lamports.</summary>
    public static bool TryToLamports(decimal sol, out ulong lamports)
    {
        lamports = 0;
        if (sol <= 0) return false;
        var scaled = sol * LamportsPerSol;
        if (scaled != decimal.Truncate(scaled) || scaled > ulong.MaxValue) return false;
        lamports = (ulong)scaled;
        return true;
    }

    public static decimal ToSol(ulong lamports) => lamports / LamportsPerSol;

    public static object Request(string method, params object[] parameters) => new
    {
        jsonrpc = "2.0",
        id = 1,
        method,
        @params = parameters,
    };

    /// <summary>The <c>result</c> of an answer, or its error message. Neither when there is no answer.</summary>
    public static (JsonElement? Result, string? Error) Unwrap(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return (null, null);
        if (root.TryGetProperty("error", out var error))
        {
            var message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) &&
                          m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : "The node refused the request.";
            return (null, message);
        }

        return root.TryGetProperty("result", out var result) ? (result, null) : (null, null);
    }

    /// <summary>A lamport count from <c>getBalance</c> ({ context, value }) or a bare number
    /// (<c>getMinimumBalanceForRentExemption</c>, <c>getBlockHeight</c>).</summary>
    public static ulong? ParseLamports(JsonElement result)
    {
        var value = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("value", out var v) ? v : result;
        return value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var n) ? n : null;
    }

    /// <summary>The blockhash to sign with, and the last block height it is valid for.</summary>
    public static (byte[] Blockhash, ulong LastValidBlockHeight)? ParseBlockhash(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("blockhash", out var hash) || hash.ValueKind != JsonValueKind.String ||
            !value.TryGetProperty("lastValidBlockHeight", out var last) || !last.TryGetUInt64(out var height) ||
            !SolanaKeys.TryDecode(hash.GetString(), out var bytes))
            return null;
        return (bytes, height);
    }

    /// <summary>
    /// One entry of <c>getSignatureStatuses</c>. Null means the node has not seen it (yet); a status
    /// only counts once a supermajority has voted on its block ("confirmed" or "finalized").
    /// </summary>
    public static (SolSubmitOutcome Outcome, string? Reason) ParseSignatureStatus(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array ||
            value.GetArrayLength() != 1)
            return (SolSubmitOutcome.Unknown, null);

        var status = value[0];
        if (status.ValueKind == JsonValueKind.Null) return (SolSubmitOutcome.Pending, null);
        if (status.ValueKind != JsonValueKind.Object) return (SolSubmitOutcome.Unknown, null);

        var level = status.TryGetProperty("confirmationStatus", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()
            : null;
        if (level is not ("confirmed" or "finalized")) return (SolSubmitOutcome.Pending, null);

        return status.TryGetProperty("err", out var err) && err.ValueKind != JsonValueKind.Null
            ? (SolSubmitOutcome.FailedFeeCharged, $"The transaction was included but failed ({err.GetRawText()}); only the fee was charged.")
            : (SolSubmitOutcome.Included, null);
    }

    public static string Sol(ulong lamports) =>
        ToSol(lamports).ToString("0.#########", CultureInfo.InvariantCulture);
}
