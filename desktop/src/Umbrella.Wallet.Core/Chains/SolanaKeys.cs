using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using NBitcoin.DataEncoders;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// Solana addresses: 32 bytes in base58, and the program-derived addresses programs own — a token
/// account, most importantly, lives at one.
///
/// A program-derived address is SHA-256 of the seeds, a bump byte, the program id and the marker
/// "ProgramDerivedAddress", chosen so that it is NOT a point on the Ed25519 curve: no private key can
/// exist for it, so only the program can sign for it. Pinned to the Solana SDK's own
/// create_program_address vectors (<c>SolanaKeysTests</c>).
/// </summary>
public static class SolanaKeys
{
    public const int Length = 32;

    /// <summary>The longest one seed may be, and the most seeds (the bump included).</summary>
    public const int MaxSeedLength = 32;
    public const int MaxSeeds = 16;

    private static readonly byte[] PdaMarker = Encoding.ASCII.GetBytes("ProgramDerivedAddress");

    // Curve25519 in Edwards form: p = 2^255 - 19, d = -121665/121666.
    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    private static readonly BigInteger D = Mod(-121665 * ModInverse(121666));

    public static bool TryDecode(string? text, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var decoded = Encoders.Base58.DecodeData(text.Trim());
            if (decoded.Length != Length) return false;
            key = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string Encode(ReadOnlySpan<byte> key)
    {
        if (key.Length != Length) throw new ArgumentException("A Solana address is 32 bytes.", nameof(key));
        return Encoders.Base58.EncodeData(key.ToArray());
    }

    /// <summary>
    /// Whether 32 bytes decompress to a point on the Ed25519 curve — what curve25519-dalek's
    /// <c>decompress().is_some()</c> decides. The top bit is the sign of x and y is taken mod p, so the
    /// question is only whether (y² − 1) / (d·y² + 1) has a square root.
    /// </summary>
    public static bool IsOnCurve(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length) return false;
        var y = new byte[Length];
        bytes.CopyTo(y);
        y[31] &= 0x7F;
        var yy = Mod(BigInteger.Pow(new BigInteger(y, isUnsigned: true, isBigEndian: false), 2));

        var u = Mod(yy - 1);
        var v = Mod((D * yy) + 1);
        if (v.IsZero) return u.IsZero;

        var w = Mod(u * ModInverse(v));
        return w.IsZero || BigInteger.ModPow(w, (P - 1) / 2, P).IsOne;   // Euler's criterion
    }

    /// <summary>The program-derived address for exactly these seeds, or null when the hash lands on the
    /// curve (then it is no PDA) or a seed is too long.</summary>
    public static byte[]? CreateProgramAddress(IReadOnlyList<byte[]> seeds, ReadOnlySpan<byte> programId)
    {
        if (seeds.Count > MaxSeeds || seeds.Any(s => s.Length > MaxSeedLength)) return null;
        if (programId.Length != Length) throw new ArgumentException("A program id is 32 bytes.", nameof(programId));

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var seed in seeds) sha.AppendData(seed);
        sha.AppendData(programId);
        sha.AppendData(PdaMarker);
        var hash = sha.GetHashAndReset();

        return IsOnCurve(hash) ? null : hash;
    }

    /// <summary>The canonical program-derived address: the highest bump (255 down) whose address is
    /// off the curve — the one every wallet and program derives.</summary>
    public static (byte[] Address, byte Bump) FindProgramAddress(IReadOnlyList<byte[]> seeds, ReadOnlySpan<byte> programId)
    {
        if (seeds.Count >= MaxSeeds) throw new ArgumentException("Too many seeds for a bump to be added.", nameof(seeds));

        var withBump = new List<byte[]>(seeds) { Array.Empty<byte>() };
        for (var bump = 255; bump >= 0; bump--)
        {
            withBump[^1] = [(byte)bump];
            if (CreateProgramAddress(withBump, programId) is { } address) return (address, (byte)bump);
        }

        throw new InvalidOperationException("No bump gives an address off the curve.");
    }

    private static BigInteger Mod(BigInteger x)
    {
        var r = BigInteger.Remainder(x, P);
        return r.Sign < 0 ? r + P : r;
    }

    private static BigInteger ModInverse(BigInteger x) => BigInteger.ModPow(Mod(x), P - 2, P);
}
