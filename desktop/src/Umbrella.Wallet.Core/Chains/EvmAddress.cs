using System.Text;
using Nethereum.Util;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>Whether an EVM address carries a valid EIP-55 checksum.</summary>
public enum EvmChecksumState
{
    /// <summary>Not a 0x-prefixed 40-hex-character address at all — nothing to say here.</summary>
    NotEvm = 0,

    /// <summary>All-lowercase or all-uppercase: no case information, so the checksum can't be verified
    /// (and the address is still valid — checksums are optional).</summary>
    NoChecksum = 1,

    /// <summary>Mixed case, and the casing matches the EIP-55 checksum — the address is intact.</summary>
    Valid = 2,

    /// <summary>Mixed case, but the casing does NOT match the checksum. A single altered character
    /// breaks it, so this almost always means the address was mistyped or swapped in transit.</summary>
    Invalid = 3,
}

/// <summary>
/// EIP-55 checksum handling for Ethereum-family addresses.
///
/// The checksum encodes information in the letter casing of the hex address: flip one character and
/// the casing no longer matches. That makes it a cheap, offline guard against a corrupted or
/// maliciously-swapped destination — a wallet can refuse (or at least warn) before signing, which
/// matters because an on-chain send to a wrong address is irreversible. All-lowercase and
/// all-uppercase addresses carry no checksum and are accepted as-is.
/// </summary>
public static class EvmAddress
{
    public static EvmChecksumState Check(string? address)
    {
        var a = (address ?? string.Empty).Trim();
        if (!a.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return EvmChecksumState.NotEvm;

        var hex = a[2..];
        if (hex.Length != 40 || !IsHex(hex)) return EvmChecksumState.NotEvm;

        var hasLower = hex.Any(c => c is >= 'a' and <= 'f');
        var hasUpper = hex.Any(c => c is >= 'A' and <= 'F');
        if (!(hasLower && hasUpper)) return EvmChecksumState.NoChecksum;

        return string.Equals(ToChecksum(hex), a, StringComparison.Ordinal)
            ? EvmChecksumState.Valid
            : EvmChecksumState.Invalid;
    }

    /// <summary>The EIP-55 checksummed form (with 0x) of a 40-hex-char address, any input casing.</summary>
    public static string ToChecksum(string hexOrAddress)
    {
        var hex = hexOrAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? hexOrAddress[2..]
            : hexOrAddress;
        hex = hex.ToLowerInvariant();

        // EIP-55: keccak256 of the ASCII lowercase hex; a hex letter is uppercased when the matching
        // nibble of the hash is >= 8.
        var hash = Sha3Keccack.Current.CalculateHash(Encoding.ASCII.GetBytes(hex));
        var sb = new StringBuilder("0x", 42);
        for (var i = 0; i < hex.Length; i++)
        {
            var c = hex[i];
            if (c is >= 'a' and <= 'f')
            {
                var nibble = (hash[i / 2] >> (i % 2 == 0 ? 4 : 0)) & 0x0F;
                sb.Append(nibble >= 8 ? char.ToUpperInvariant(c) : c);
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }
}
