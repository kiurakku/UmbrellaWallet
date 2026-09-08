using System.Globalization;

// NOTE: deliberately NOT namespace ...Core.Money — that name would shadow NBitcoin's Money type
// for everything under Umbrella.Wallet.Core (the UTXO spender uses Money.Satoshis).
namespace Umbrella.Wallet.Core.Amounts;

/// <summary>
/// Parses an amount the user typed into a send/swap/request field.
///
/// This exists because the obvious version is dangerously wrong. The wallet is used in six languages
/// and formats its own figures per locale, so people type <c>0,5</c> as readily as <c>0.5</c> — and
/// <c>decimal.TryParse("0,5", NumberStyles.Number, InvariantCulture)</c> succeeds with the value
/// <b>5</b>, because .NET reads the comma as a group separator and does not check group sizes. A field
/// meant to send 0.5 would have prepared a transfer of 5.
///
/// The rules here are chosen so that any ambiguity errs towards a SMALLER amount, never a larger one:
/// <list type="bullet">
/// <item>a single <c>.</c> or <c>,</c> is always the decimal separator, so "1,234" is 1.234, not 1234;</item>
/// <item>if both appear, the LAST one is the decimal separator and the other is grouping;</item>
/// <item>a separator that appears more than once can only be grouping;</item>
/// <item>grouping must actually be grouping — 1–3 digits, then groups of exactly 3;</item>
/// <item>whitespace (including the non-breaking spaces locale formatting uses) is stripped;</item>
/// <item>anything else — empty, letters, a sign, a dangling separator — is refused outright.</item>
/// </list>
/// Nothing is rounded or "corrected": a value this cannot read confidently is rejected so the caller
/// can ask again, rather than guessing at somebody's money.
/// </summary>
public static class AmountInput
{
    /// <summary>
    /// True when <paramref name="text"/> is an unambiguous, non-negative amount, with the value in
    /// <paramref name="amount"/>. Zero parses (callers decide whether zero is allowed); a negative
    /// value is refused, since no field in this wallet takes one.
    /// </summary>
    public static bool TryParse(string? text, out decimal amount)
    {
        amount = 0m;

        // Locale formatting groups with U+00A0 / U+202F, and pasted figures often carry plain spaces.
        var s = Strip((text ?? string.Empty).Trim());
        if (s.Length == 0) return false;
        if (s.Contains('-') || s.Contains('+')) return false; // no signed amounts anywhere in the app

        var lastDot = s.LastIndexOf('.');
        var lastComma = s.LastIndexOf(',');
        var dots = Count(s, '.');
        var commas = Count(s, ',');

        // Which separator (if any) is the decimal point, and where.
        int decimalAt;
        if (lastDot >= 0 && lastComma >= 0)
        {
            // Both present: the rightmost is the decimal point — and it may only appear once.
            if (lastDot > lastComma)
            {
                if (dots != 1) return false;
                decimalAt = lastDot;
            }
            else
            {
                if (commas != 1) return false;
                decimalAt = lastComma;
            }
        }
        else if (dots > 1 || commas > 1)
        {
            decimalAt = -1; // a repeated single separator can only be grouping
        }
        else if (dots == 1)
        {
            decimalAt = lastDot;
        }
        else if (commas == 1)
        {
            decimalAt = lastComma;
        }
        else
        {
            decimalAt = -1;
        }

        var integerPart = decimalAt >= 0 ? s[..decimalAt] : s;
        var fractionPart = decimalAt >= 0 ? s[(decimalAt + 1)..] : string.Empty;

        // A decimal separator with no digits after it is a half-typed number, not a value.
        if (decimalAt >= 0 && !AllDigits(fractionPart)) return false;
        if (!IsGroupedInteger(integerPart)) return false;

        var normalised = Ungroup(integerPart) + (decimalAt >= 0 ? "." + fractionPart : string.Empty);

        return decimal.TryParse(
            normalised,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out amount) && amount >= 0m;
    }

    /// <summary>True when the text is an unambiguous amount strictly greater than zero.</summary>
    public static bool TryParsePositive(string? text, out decimal amount) =>
        TryParse(text, out amount) && amount > 0m;

    private static string Strip(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (!char.IsWhiteSpace(ch)) sb.Append(ch);
        }

        return sb.ToString();
    }

    private static int Count(string s, char c)
    {
        var n = 0;
        foreach (var ch in s)
        {
            if (ch == c) n++;
        }

        return n;
    }

    private static bool AllDigits(string s)
    {
        if (s.Length == 0) return false;
        foreach (var ch in s)
        {
            if (!char.IsAsciiDigit(ch)) return false;
        }

        return true;
    }

    /// <summary>
    /// The digits before the decimal point: either plain digits, or digits grouped the way every
    /// locale groups them (1–3 digits, then groups of exactly 3, by one consistent separator).
    /// "1.2.3" is not a grouped number and must not be read as 123.
    /// </summary>
    private static bool IsGroupedInteger(string s)
    {
        if (s.Length == 0) return false;
        if (AllDigits(s)) return true;

        var separator = s.Contains(',') ? ',' : '.';
        if (s.Contains(separator == ',' ? '.' : ',')) return false; // two grouping separators: nonsense

        var groups = s.Split(separator);
        if (groups.Length < 2) return false;
        if (groups[0].Length is < 1 or > 3 || !AllDigits(groups[0])) return false;

        for (var i = 1; i < groups.Length; i++)
        {
            if (groups[i].Length != 3 || !AllDigits(groups[i])) return false;
        }

        return true;
    }

    private static string Ungroup(string s) => s.Replace(",", string.Empty).Replace(".", string.Empty);
}
