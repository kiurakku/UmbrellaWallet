namespace Umbrella.Wallet.Core.Codecs;

/// <summary>
/// Bech32 (BIP-173) — one implementation for every chain that uses it: Cardano's <c>addr1…</c>,
/// Cosmos' <c>cosmos1…</c>.
///
/// The part that matters is <see cref="TryDecode"/> refusing a string whose checksum does not match.
/// Bech32's checksum is guaranteed to catch any error in up to four characters; a decoder that skips
/// it turns a single mistyped character into a different, perfectly valid-looking address, and a
/// payment to it into a payment to nobody. (The Cardano send path had exactly that decoder until this
/// replaced it.)
///
/// Pinned to BIP-173's own valid and invalid test strings.
/// </summary>
public static class Bech32
{
    public const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";

    /// <summary>BIP-173's overall limit. Cardano addresses exceed it and pass a larger one explicitly.</summary>
    public const int DefaultMaxLength = 90;

    /// <summary>Encodes 8-bit <paramref name="data"/> under <paramref name="hrp"/>, lower case.</summary>
    public static string Encode(string hrp, ReadOnlySpan<byte> data)
    {
        var words = ConvertBits(data.ToArray().Select(b => (int)b).ToArray(), 8, 5, pad: true)
                    ?? throw new ArgumentException("Could not regroup the data.", nameof(data));
        var checksum = CreateChecksum(hrp, words);

        var sb = new System.Text.StringBuilder(hrp.Length + 1 + words.Length + 6);
        sb.Append(hrp).Append('1');
        foreach (var w in words) sb.Append(Charset[w]);
        foreach (var c in checksum) sb.Append(Charset[c]);
        return sb.ToString();
    }

    /// <summary>
    /// Decodes and VERIFIES a bech32 string: character ranges, no mixed case, a separator, a non-empty
    /// human-readable part, a six-character checksum that matches, and 5-bit data that regroups into
    /// whole bytes with zero padding. Anything else returns false.
    /// </summary>
    public static bool TryDecode(string? text, out string hrp, out byte[] data, int maxLength = DefaultMaxLength)
    {
        hrp = string.Empty;
        data = [];

        if (!TryDecodeWords(text, out var h, out var words, maxLength)) return false;

        var bytes = ConvertBits(words, 5, 8, pad: false);
        if (bytes is null) return false;

        hrp = h;
        data = bytes.Select(b => (byte)b).ToArray();
        return true;
    }

    /// <summary>The checksum-verified 5-bit words, before regrouping. BIP-173's test strings are
    /// checked at this level: several of them carry data that is not whole bytes.</summary>
    public static bool TryDecodeWords(string? text, out string hrp, out int[] words, int maxLength = DefaultMaxLength)
    {
        hrp = string.Empty;
        words = [];

        if (string.IsNullOrEmpty(text) || text.Length > maxLength) return false;

        bool hasLower = false, hasUpper = false;
        foreach (var c in text)
        {
            if (c < 33 || c > 126) return false;
            if (char.IsAsciiLetterLower(c)) hasLower = true;
            if (char.IsAsciiLetterUpper(c)) hasUpper = true;
        }

        if (hasLower && hasUpper) return false;

        var s = text.ToLowerInvariant();
        var sep = s.LastIndexOf('1');
        if (sep < 1 || sep + 7 > s.Length) return false;   // empty HRP, or fewer than 6 checksum chars

        var h = s[..sep];
        var values = new int[s.Length - sep - 1];
        for (var i = 0; i < values.Length; i++)
        {
            var v = Charset.IndexOf(s[sep + 1 + i]);
            if (v < 0) return false;
            values[i] = v;
        }

        if (Polymod(HrpExpand(h).Concat(values).ToArray()) != 1) return false;

        hrp = h;
        words = values[..^6];
        return true;
    }

    private static int[] CreateChecksum(string hrp, int[] words)
    {
        var values = HrpExpand(hrp).Concat(words).Concat(new int[6]).ToArray();
        var polymod = Polymod(values) ^ 1;
        var checksum = new int[6];
        for (var i = 0; i < 6; i++) checksum[i] = (polymod >> (5 * (5 - i))) & 31;
        return checksum;
    }

    private static int[] HrpExpand(string hrp)
    {
        var result = new int[hrp.Length * 2 + 1];
        for (var i = 0; i < hrp.Length; i++)
        {
            result[i] = hrp[i] >> 5;
            result[i + hrp.Length + 1] = hrp[i] & 31;
        }

        return result;
    }

    private static int Polymod(int[] values)
    {
        int[] gen = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];
        var chk = 1;
        foreach (var v in values)
        {
            var top = chk >> 25;
            chk = ((chk & 0x1ffffff) << 5) ^ v;
            for (var i = 0; i < 5; i++)
                if (((top >> i) & 1) == 1) chk ^= gen[i];
        }

        return chk;
    }

    /// <summary>Regroups bit widths. Without padding, leftover bits must be zero and fewer than a
    /// whole group — otherwise the data was not produced by an encoder, and is refused.</summary>
    private static int[]? ConvertBits(int[] data, int from, int to, bool pad)
    {
        var acc = 0;
        var bits = 0;
        var maxv = (1 << to) - 1;
        var result = new List<int>(data.Length * from / to + 1);
        foreach (var value in data)
        {
            if (value < 0 || value >> from != 0) return null;
            acc = (acc << from) | value;
            bits += from;
            while (bits >= to)
            {
                bits -= to;
                result.Add((acc >> bits) & maxv);
            }
        }

        if (pad)
        {
            if (bits > 0) result.Add((acc << (to - bits)) & maxv);
        }
        else if (bits >= from || ((acc << (to - bits)) & maxv) != 0)
        {
            return null;
        }

        return result.ToArray();
    }
}
