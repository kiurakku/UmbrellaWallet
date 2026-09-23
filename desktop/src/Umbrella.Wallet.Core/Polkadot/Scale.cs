using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Umbrella.Wallet.Core.Polkadot;

/// <summary>SCALE, the encoding Substrate uses for everything: little-endian integers and compact
/// (variable-length) integers, length-prefixed vectors and strings.</summary>
public static class Scale
{
    public static byte[] Compact(BigInteger value)
    {
        if (value.Sign < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (value < 64) return [(byte)((int)value << 2)];
        if (value < 1 << 14)
        {
            var v = (ushort)(((int)value << 2) | 1);
            return [(byte)v, (byte)(v >> 8)];
        }

        if (value < 1 << 30)
        {
            var b = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, ((uint)value << 2) | 2);
            return b;
        }

        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        if (bytes.Length > 67) throw new ArgumentOutOfRangeException(nameof(value));
        return [(byte)(((bytes.Length - 4) << 2) | 3), .. bytes];
    }

    public static byte[] U32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }
}

/// <summary>A cursor over SCALE bytes. Every read throws on running out, so a truncated or malformed
/// answer is an error, never a silently short value.</summary>
public sealed class ScaleReader(byte[] data, int offset = 0)
{
    private int _pos = offset;

    public int Position => _pos;

    /// <summary>A second cursor at the same position, for looking ahead without moving this one.</summary>
    public ScaleReader Fork() => new(data, _pos);
    public bool AtEnd => _pos == data.Length;

    public byte U8() => Take(1)[0];
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public BigInteger U128() => new(Take(16), isUnsigned: true, isBigEndian: false);
    public byte[] Bytes(int n) => Take(n).ToArray();

    public BigInteger Compact()
    {
        var first = data.Length > _pos ? data[_pos] : throw new FormatException("SCALE data ended early.");
        switch (first & 3)
        {
            case 0: _pos++; return first >> 2;
            case 1: return U16() >> 2;
            case 2: return U32() >> 2;
            default:
                _pos++;
                var n = (first >> 2) + 4;
                return new BigInteger(Take(n), isUnsigned: true, isBigEndian: false);
        }
    }

    public int CompactInt()
    {
        var v = Compact();
        return v > int.MaxValue ? throw new FormatException("A SCALE length is too large.") : (int)v;
    }

    public string String() => Encoding.UTF8.GetString(Take(CompactInt()));

    public bool Option() => U8() switch
    {
        0 => false,
        1 => true,
        _ => throw new FormatException("A SCALE option tag was neither 0 nor 1."),
    };

    private ReadOnlySpan<byte> Take(int n)
    {
        if (n < 0 || _pos + n > data.Length) throw new FormatException("SCALE data ended early.");
        var span = data.AsSpan(_pos, n);
        _pos += n;
        return span;
    }
}
