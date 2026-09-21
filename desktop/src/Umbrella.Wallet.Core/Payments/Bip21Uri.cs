using System.Globalization;

namespace Umbrella.Wallet.Core.Payments;

/// <summary>
/// A parsed <c>bitcoin:</c> payment request (BIP-21), with the BIP-78 PayJoin fields.
///
/// <see cref="PayjoinEndpoint"/> is set only when the request carried a <c>pj=</c> the wallet is
/// willing to talk to. When it carried one the wallet refused, <see cref="PayjoinIgnoredReason"/>
/// says why, so the UI can tell the user instead of quietly sending a plain payment.
/// </summary>
public sealed record Bip21Payment(
    string Address,
    decimal? Amount,
    string? Label,
    string? Message,
    Uri? PayjoinEndpoint,
    string? PayjoinIgnoredReason,
    bool OutputSubstitutionDisabled);

/// <summary>
/// Parses a BIP-21 URI strictly. A payment request is an instruction to move money, so anything
/// ambiguous is refused rather than guessed at:
///
/// <list type="bullet">
/// <item>The amount is BIP-21's own format — digits and one dot, in BTC. <c>0,5</c> is refused,
/// not read as five (the trap <c>AmountInput</c> exists for) and not as a half.</item>
/// <item>A key given twice is refused. Two <c>amount=</c> values let two programs disagree about
/// what the same link asks for.</item>
/// <item>An unknown <c>req-</c> key is refused, as BIP-21 requires: the sender is being told there
/// is a condition it does not understand.</item>
/// <item>A <c>pj=</c> endpoint must be HTTPS, or HTTP to a <c>.onion</c> — BIP-78's own rule. The
/// PSBT sent there reveals every input of the payment; a plaintext endpoint shows it to the path.</item>
/// </list>
/// </summary>
public static class Bip21Uri
{
    public const string Scheme = "bitcoin:";

    /// <summary>True when the text is plausibly a payment URI, before any validation — used to
    /// decide whether a pasted destination should be parsed at all.</summary>
    public static bool LooksLikeOne(string? text) =>
        text is not null && text.TrimStart().StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string? text, out Bip21Payment? payment, out string? error)
    {
        payment = null;
        error = null;

        var raw = text?.Trim() ?? "";
        if (!raw.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            error = "Not a bitcoin: payment link.";
            return false;
        }

        var rest = raw[Scheme.Length..];
        var q = rest.IndexOf('?');
        var address = (q < 0 ? rest : rest[..q]).Trim();
        if (address.Length == 0)
        {
            error = "The payment link has no address.";
            return false;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (q >= 0)
        {
            foreach (var pair in rest[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var key = Unescape(eq < 0 ? pair : pair[..eq]);
                var value = eq < 0 ? "" : Unescape(pair[(eq + 1)..]);

                if (key is null || value is null)
                {
                    error = "The payment link is not correctly encoded.";
                    return false;
                }

                if (!fields.TryAdd(key, value))
                {
                    error = $"The payment link gives '{key}' more than once.";
                    return false;
                }
            }
        }

        foreach (var key in fields.Keys)
        {
            if (key.StartsWith("req-", StringComparison.OrdinalIgnoreCase))
            {
                error = $"The payment link requires '{key}', which this wallet does not support.";
                return false;
            }
        }

        decimal? amount = null;
        if (fields.TryGetValue("amount", out var amountText))
        {
            if (!TryParseAmount(amountText, out var parsed))
            {
                error = "The payment link's amount is not a valid BTC amount.";
                return false;
            }

            amount = parsed;
        }

        Uri? endpoint = null;
        string? ignored = null;
        if (fields.TryGetValue("pj", out var pjText))
        {
            if (!Uri.TryCreate(pjText, UriKind.Absolute, out var pj))
                ignored = "The receiver's PayJoin address is not a valid URL.";
            else if (!IsAcceptableEndpoint(pj))
                ignored = "The receiver's PayJoin address is neither HTTPS nor a .onion, so the payment's " +
                          "inputs would travel in the clear. It was not used.";
            else
                endpoint = pj;
        }

        // pjos=0: the receiver asks us not to let it substitute the payment output. This wallet never
        // allows substitution anyway (see PayjoinParameters), so it is recorded, not relied on.
        var noSubstitution = fields.TryGetValue("pjos", out var pjos) && pjos == "0";

        payment = new Bip21Payment(
            address,
            amount,
            fields.GetValueOrDefault("label"),
            fields.GetValueOrDefault("message"),
            endpoint,
            ignored,
            noSubstitution);
        return true;
    }

    /// <summary>BIP-78: https, or plain http only to a Tor onion service (which is already
    /// end-to-end encrypted and authenticated by its address).</summary>
    public static bool IsAcceptableEndpoint(Uri endpoint)
    {
        if (endpoint.Scheme == Uri.UriSchemeHttps) return true;
        return endpoint.Scheme == Uri.UriSchemeHttp &&
               endpoint.Host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>BIP-21 amounts: decimal BTC with an optional single dot, no signs, no exponent, no
    /// group separators, at most 8 decimal places, and more than zero.</summary>
    private static bool TryParseAmount(string text, out decimal amount)
    {
        amount = 0;
        if (text.Length == 0 || text.Any(c => c != '.' && !char.IsAsciiDigit(c))) return false;
        if (text.Count(c => c == '.') > 1) return false;

        var dot = text.IndexOf('.');
        if (dot >= 0 && text.Length - dot - 1 > 8) return false;

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)
               && amount > 0
               && amount <= 21_000_000m;
    }

    private static string? Unescape(string s)
    {
        // RFC 3986, not HTML forms: '+' is a plus sign here. Turning it into a space would
        // rewrite a pj= URL that happens to contain one.
        try { return Uri.UnescapeDataString(s); }
        catch { return null; }
    }
}
