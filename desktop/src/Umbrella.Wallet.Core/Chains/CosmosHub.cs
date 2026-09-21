using System.Globalization;
using System.Text.Json;
using Umbrella.Wallet.Core.Codecs;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// Cosmos Hub addresses and balances (roadmap N.6).
///
/// An address is the bech32 of the account id — RIPEMD160(SHA256(compressed secp256k1 key)) — under the
/// prefix <c>cosmos</c>. The balance is the bank module's AVAILABLE ATOM: staked ATOM sits with the
/// validators it is delegated to and is not part of it, which the wallet says rather than lets the number
/// imply otherwise.
/// </summary>
public static class CosmosHub
{
    public const string Prefix = "cosmos";
    public const string Denom = "uatom";
    public const decimal MicroPerAtom = 1_000_000m;

    public static string AddressFromAccountId(ReadOnlySpan<byte> accountId)
    {
        if (accountId.Length != 20) throw new ArgumentException("A Cosmos account id is 20 bytes.", nameof(accountId));
        return Bech32.Encode(Prefix, accountId);
    }

    /// <summary>A checksum-verified <c>cosmos1…</c> address holding a 20-byte account id.</summary>
    public static bool IsValidAddress(string? address) =>
        Bech32.TryDecode(address?.Trim(), out var hrp, out var data) && hrp == Prefix && data.Length == 20;

    /// <summary>
    /// Reads <c>/cosmos/bank/v1beta1/balances/{address}/by_denom?denom=uatom</c>. The bank module answers
    /// "0" for an address it has never seen, so a real zero needs no special case; everything that is not
    /// a 200 carrying uatom as whole micro-units is unknown.
    /// </summary>
    public static decimal? ParseBalance(int statusCode, JsonElement? body)
    {
        if (statusCode != 200 || body is not { ValueKind: JsonValueKind.Object } root) return null;
        if (!root.TryGetProperty("balance", out var balance) || balance.ValueKind != JsonValueKind.Object) return null;
        if (!balance.TryGetProperty("denom", out var denom) || denom.GetString() != Denom) return null;
        if (!balance.TryGetProperty("amount", out var amount) || amount.ValueKind != JsonValueKind.String) return null;

        // Micro-ATOM is an integer in a string; the SDK uses arbitrary precision, so parse as decimal digits.
        var text = amount.GetString();
        if (string.IsNullOrEmpty(text) || !text.All(char.IsAsciiDigit)) return null;
        return decimal.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var micro)
            ? micro / MicroPerAtom
            : null;
    }
}
