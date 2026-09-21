using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// NEAR implicit accounts and balances (roadmap N.7).
///
/// An implicit account's id is simply the lower-case hex of its ed25519 public key — 64 characters, no
/// registration. It exists on-chain from the moment it first receives NEAR; before that the RPC answers
/// <c>UNKNOWN_ACCOUNT</c>, which is a real zero. Named accounts (<c>you.near</c>) are a separate thing the
/// wallet does not create or look up.
/// </summary>
public static class NearAccounts
{
    /// <summary>10^24 yoctoNEAR in one NEAR.</summary>
    private static readonly BigInteger YoctoPerNear = BigInteger.Pow(10, 24);

    public static string ImplicitAccountId(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length != 32) throw new ArgumentException("An ed25519 public key is 32 bytes.", nameof(publicKey));
        return Convert.ToHexString(publicKey).ToLowerInvariant();
    }

    public static bool IsImplicitAccountId(string? id) =>
        id is { Length: 64 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>The JSON-RPC body for <c>view_account</c> at FINAL finality — never an optimistic block.</summary>
    public static object ViewAccountRequest(string accountId) => new
    {
        jsonrpc = "2.0",
        id = "umbrella",
        method = "query",
        @params = new { request_type = "view_account", finality = "final", account_id = accountId },
    };

    /// <summary>
    /// NEAR from a <c>view_account</c> response. <c>amount</c> is yoctoNEAR as a decimal string that can
    /// exceed what <see cref="decimal"/> holds (about 79 000 NEAR), so it is split into whole NEAR and a
    /// fraction with <see cref="BigInteger"/> before converting. <c>UNKNOWN_ACCOUNT</c> is zero; anything
    /// else is unknown.
    /// </summary>
    public static decimal? ParseViewAccount(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        if (root.TryGetProperty("error", out var error))
        {
            // A server can put anything in "error" — a string from a gateway, an object from the RPC.
            // Only the RPC's own UNKNOWN_ACCOUNT is an answer.
            var cause = error.ValueKind == JsonValueKind.Object &&
                        error.TryGetProperty("cause", out var c) && c.ValueKind == JsonValueKind.Object &&
                        c.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()
                : null;
            return cause == "UNKNOWN_ACCOUNT" ? 0m : null;
        }

        if (!root.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("amount", out var amount) ||
            amount.ValueKind != JsonValueKind.String)
            return null;

        var text = amount.GetString();
        if (string.IsNullOrEmpty(text) || !text.All(char.IsAsciiDigit)) return null;
        if (!BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var yocto)) return null;

        var whole = BigInteger.DivRem(yocto, YoctoPerNear, out var rest);
        if (whole > new BigInteger(decimal.MaxValue)) return null;

        // The remainder is below 10^24, well inside decimal's range; scale it down in two exact steps.
        return (decimal)whole + (decimal)rest / 1_000_000_000_000m / 1_000_000_000_000m;
    }
}
