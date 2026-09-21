using System.Globalization;
using System.Text.Json;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// Stellar account ids — the 56-character "G…" StrKey (roadmap N.5).
///
/// A version byte (6 &lt;&lt; 3 for an account id, which is what makes the first character G), the
/// 32-byte ed25519 public key, and a CRC16-XModem checksum stored little-endian, all in RFC 4648
/// base32 without padding. The checksum is what turns a mistyped character into a refused address
/// rather than a payment to nobody.
/// </summary>
public static class StellarKeys
{
    private const byte AccountIdVersion = 6 << 3;   // 'G'
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string EncodeAccountId(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length != 32) throw new ArgumentException("An ed25519 public key is 32 bytes.", nameof(publicKey));

        var data = new byte[35];
        data[0] = AccountIdVersion;
        publicKey.CopyTo(data.AsSpan(1));
        var crc = Crc16XModem(data.AsSpan(0, 33));
        data[33] = (byte)(crc & 0xff);          // little-endian, as StrKey specifies
        data[34] = (byte)(crc >> 8);
        return Base32(data);
    }

    /// <summary>True for a well-formed account id: right length, alphabet, version and checksum.</summary>
    public static bool IsValidAccountId(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        var a = address.Trim();
        if (a.Length != 56 || a[0] != 'G') return false;

        var data = FromBase32(a);
        if (data is null || data.Length != 35 || data[0] != AccountIdVersion) return false;

        var crc = Crc16XModem(data.AsSpan(0, 33));
        return data[33] == (byte)(crc & 0xff) && data[34] == (byte)(crc >> 8);
    }

    private static ushort Crc16XModem(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0;
        foreach (var b in bytes)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }

        return crc;
    }

    private static string Base32(ReadOnlySpan<byte> data)
    {
        var chars = new char[(data.Length * 8 + 4) / 5];
        int buffer = 0, bits = 0, n = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                chars[n++] = Base32Alphabet[(buffer >> (bits - 5)) & 31];
                bits -= 5;
            }
        }

        if (bits > 0) chars[n++] = Base32Alphabet[(buffer << (5 - bits)) & 31];
        return new string(chars, 0, n);
    }

    private static byte[]? FromBase32(string text)
    {
        var bytes = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in text)
        {
            var v = Base32Alphabet.IndexOf(c);
            if (v < 0) return null;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }

        // Leftover bits must be zero padding, or the string is not a canonical encoding.
        return bits > 0 && (buffer & ((1 << bits) - 1)) != 0 ? null : bytes.ToArray();
    }
}

/// <summary>
/// Reading a Horizon <c>/accounts/{id}</c> answer (roadmap N.5). As with XRP, the decision that
/// matters is a real zero versus an unknown: a Stellar address is not an account until somebody funds
/// it with the minimum balance, and Horizon answers 404 for it — which IS zero. Everything else that is
/// not a clean 200 with a native balance is unknown.
/// </summary>
public static class StellarHorizon
{
    public static decimal? ParseAccount(int statusCode, JsonElement? body)
    {
        if (statusCode == 404) return 0m;
        if (statusCode != 200 || body is not { ValueKind: JsonValueKind.Object } root) return null;
        if (!root.TryGetProperty("balances", out var balances) || balances.ValueKind != JsonValueKind.Array) return null;

        foreach (var b in balances.EnumerateArray())
        {
            if (!b.TryGetProperty("asset_type", out var type) || type.GetString() != "native") continue;
            if (!b.TryGetProperty("balance", out var value) || value.ValueKind != JsonValueKind.String) return null;

            // Horizon writes seven decimal places with a dot. No sign, no grouping, no exponent.
            return decimal.TryParse(value.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var xlm)
                ? xlm
                : null;
        }

        // An account always holds its native reserve; a list without XLM is not an answer we understand.
        return null;
    }
}
