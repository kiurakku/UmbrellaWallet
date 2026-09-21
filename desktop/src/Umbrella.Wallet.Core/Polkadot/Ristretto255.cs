using System.Numerics;

namespace Umbrella.Wallet.Core.Polkadot;

/// <summary>
/// Just enough of ristretto255 (RFC 9496) to turn a scalar into a public key: multiply the base point
/// on Edwards25519 and encode the result the Ristretto way (roadmap N.8).
///
/// Polkadot's sr25519 public keys ARE Ristretto encodings, so getting this wrong would not give an
/// error — it would give an address that belongs to no key at all, and anything sent to it would be
/// gone. It is therefore checked against RFC 9496's own encodings of 0·B … 15·B, and the whole sr25519
/// pipeline against subkey's published output.
///
/// This computes PUBLIC values only. It is written for clarity over speed (BigInteger, not constant
/// time), which is acceptable because nothing secret depends on its timing beyond a one-off derivation
/// on the user's own machine, and it never signs.
/// </summary>
public static class Ristretto255
{
    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;

    // RFC 9496 §4.1, given there in decimal.
    private static readonly BigInteger D =
        BigInteger.Parse("37095705934669439343138083508754565189542113879843219016388785533085940283555");
    private static readonly BigInteger SqrtM1 =
        BigInteger.Parse("19681161376707505956807079304988542015446066515923890162744021073123829784752");
    private static readonly BigInteger InvSqrtAMinusD =
        BigInteger.Parse("54469307008909316920995813868745141605393597292927456921205312896311721017578");

    // The Edwards25519 base point (RFC 8032 §5.1).
    private static readonly BigInteger BaseX =
        BigInteger.Parse("15112221349535400772501151409588531511454012693041857206046113283949847762202");
    private static readonly BigInteger BaseY =
        BigInteger.Parse("46316835694926478169428394003475163141307993866256225615783033603165251855960");

    /// <summary>A point in extended coordinates: x = X/Z, y = Y/Z, xy = T/Z.</summary>
    private readonly record struct Point(BigInteger X, BigInteger Y, BigInteger Z, BigInteger T);

    private static readonly Point Identity = new(0, 1, 1, 0);
    private static readonly Point Base = new(BaseX, BaseY, 1, Mod(BaseX * BaseY));

    /// <summary>The ristretto255 encoding of <paramref name="scalar"/>·B, 32 bytes little-endian.</summary>
    public static byte[] EncodeBaseMultiple(BigInteger scalar)
    {
        if (scalar.Sign < 0) throw new ArgumentOutOfRangeException(nameof(scalar));
        return Encode(Multiply(Base, scalar));
    }

    private static Point Multiply(Point point, BigInteger scalar)
    {
        var result = Identity;
        var addend = point;
        while (!scalar.IsZero)
        {
            if (!scalar.IsEven) result = Add(result, addend);
            addend = Add(addend, addend);
            scalar >>= 1;
        }

        return result;
    }

    /// <summary>Unified addition for a = −1 twisted Edwards ("add-2008-hwcd-3"); complete on this curve,
    /// so it also serves as doubling.</summary>
    private static Point Add(Point p, Point q)
    {
        var a = Mod((p.Y - p.X) * (q.Y - q.X));
        var b = Mod((p.Y + p.X) * (q.Y + q.X));
        var c = Mod(p.T * 2 * D * q.T);
        var d = Mod(p.Z * 2 * q.Z);
        var e = b - a;
        var f = d - c;
        var g = d + c;
        var h = b + a;
        return new Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
    }

    /// <summary>RFC 9496 §4.3.2.</summary>
    private static byte[] Encode(Point p)
    {
        var u1 = Mod((p.Z + p.Y) * (p.Z - p.Y));
        var u2 = Mod(p.X * p.Y);
        var (_, invsqrt) = SqrtRatioM1(1, Mod(u1 * u2 * u2));   // always square for a valid point
        var den1 = Mod(invsqrt * u1);
        var den2 = Mod(invsqrt * u2);
        var zInv = Mod(den1 * den2 * p.T);
        var ix0 = Mod(p.X * SqrtM1);
        var iy0 = Mod(p.Y * SqrtM1);
        var enchantedDenominator = Mod(den1 * InvSqrtAMinusD);

        var rotate = IsNegative(Mod(p.T * zInv));
        var x = rotate ? iy0 : p.X;
        var y = rotate ? ix0 : p.Y;
        var denInv = rotate ? enchantedDenominator : den2;

        if (IsNegative(Mod(x * zInv))) y = Mod(-y);

        var s = Abs(Mod(denInv * (p.Z - y)));
        var bytes = s.ToByteArray(isUnsigned: true, isBigEndian: false);
        var out32 = new byte[32];
        bytes.AsSpan(0, Math.Min(bytes.Length, 32)).CopyTo(out32);
        return out32;
    }

    /// <summary>RFC 9496 §4.2: (was_square, the non-negative sqrt(u/v) or sqrt(i·u/v)).</summary>
    private static (bool WasSquare, BigInteger Root) SqrtRatioM1(BigInteger u, BigInteger v)
    {
        var v3 = Mod(v * v * v);
        var v7 = Mod(v3 * v3 * v);
        var r = Mod(u * v3 * BigInteger.ModPow(Mod(u * v7), (P - 5) / 8, P));
        var check = Mod(v * r * r);

        var correctSign = check == Mod(u);
        var flippedSign = check == Mod(-u);
        var flippedSignI = check == Mod(-u * SqrtM1);

        if (flippedSign || flippedSignI) r = Mod(r * SqrtM1);
        return (correctSign || flippedSign, Abs(r));
    }

    private static bool IsNegative(BigInteger x) => !Mod(x).IsEven;

    private static BigInteger Abs(BigInteger x) => IsNegative(x) ? Mod(-x) : Mod(x);

    private static BigInteger Mod(BigInteger x)
    {
        var r = x % P;
        return r.Sign < 0 ? r + P : r;
    }
}
