using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Umbrella.Wallet.Core.Polkadot;

/// <summary>
/// Merlin transcripts (merlin.cool) — what sr25519 hashes its signatures with: STROBE-128 over
/// Keccak-f[1600], with the label and length of every message absorbed alongside it. A line-by-line
/// port of the Rust crate's <c>strobe.rs</c> and <c>transcript.rs</c>, pinned to the fixed challenges in
/// the Go port's tests (<c>MerlinTests</c>).
/// </summary>
public sealed class MerlinTranscript
{
    private readonly Strobe128 _strobe;

    private MerlinTranscript(Strobe128 strobe) => _strobe = strobe;

    public MerlinTranscript(string label) : this(Encoding.ASCII.GetBytes(label)) { }

    public MerlinTranscript(byte[] label)
    {
        _strobe = new Strobe128(Encoding.ASCII.GetBytes("Merlin v1.0"));
        AppendMessage("dom-sep"u8, label);
    }

    public MerlinTranscript Clone() => new(_strobe.Clone());

    public void AppendMessage(ReadOnlySpan<byte> label, ReadOnlySpan<byte> message)
    {
        _strobe.MetaAd(label, false);
        _strobe.MetaAd(Le32(message.Length), true);
        _strobe.Ad(message, false);
    }

    public byte[] ChallengeBytes(ReadOnlySpan<byte> label, int length)
    {
        _strobe.MetaAd(label, false);
        _strobe.MetaAd(Le32(length), true);
        var dest = new byte[length];
        _strobe.Prf(dest, false);
        return dest;
    }

    /// <summary>
    /// The transcript-bound RNG: a copy of the transcript rekeyed with the secret witnesses, then with
    /// 32 fresh random bytes. A signature nonce drawn from it depends on the message, the secret and
    /// real randomness at once, so neither a bad RNG nor a repeated message can leak the key.
    /// </summary>
    public byte[] WitnessBytes(ReadOnlySpan<byte> label, IReadOnlyList<byte[]> witnesses, int length, byte[]? randomness = null)
    {
        var rng = _strobe.Clone();
        foreach (var witness in witnesses)
        {
            rng.MetaAd(label, false);
            rng.MetaAd(Le32(witness.Length), true);
            rng.Key(witness, false);
        }

        var random = randomness ?? RandomNumberGenerator.GetBytes(32);
        rng.MetaAd("rng"u8, false);
        rng.Key(random, false);

        var dest = new byte[length];
        rng.MetaAd(Le32(length), false);
        rng.Prf(dest, false);
        return dest;
    }

    private static byte[] Le32(int n)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)n);
        return b;
    }
}

/// <summary>STROBE-128, the subset Merlin uses: meta-AD, AD, KEY and PRF.</summary>
internal sealed class Strobe128
{
    private const int R = 166;
    private const byte FlagI = 1, FlagA = 1 << 1, FlagC = 1 << 2, FlagM = 1 << 4;

    private readonly byte[] _state;
    private int _pos;
    private int _posBegin;
    private byte _curFlags;

    public Strobe128(byte[] protocolLabel)
    {
        _state = new byte[200];
        _state[0] = 1; _state[1] = R + 2; _state[2] = 1; _state[3] = 0; _state[4] = 1; _state[5] = 96;
        Encoding.ASCII.GetBytes("STROBEv1.0.2").CopyTo(_state, 6);
        Keccak.F1600(_state);
        MetaAd(protocolLabel, false);
    }

    private Strobe128(byte[] state, int pos, int posBegin, byte curFlags)
    {
        _state = state;
        _pos = pos;
        _posBegin = posBegin;
        _curFlags = curFlags;
    }

    public Strobe128 Clone() => new((byte[])_state.Clone(), _pos, _posBegin, _curFlags);

    public void MetaAd(ReadOnlySpan<byte> data, bool more) { BeginOp(FlagM | FlagA, more); Absorb(data); }

    public void Ad(ReadOnlySpan<byte> data, bool more) { BeginOp(FlagA, more); Absorb(data); }

    public void Prf(Span<byte> data, bool more) { BeginOp(FlagI | FlagA | FlagC, more); Squeeze(data); }

    public void Key(ReadOnlySpan<byte> data, bool more) { BeginOp(FlagA | FlagC, more); Overwrite(data); }

    private void RunF()
    {
        _state[_pos] ^= (byte)_posBegin;
        _state[_pos + 1] ^= 0x04;
        _state[R + 1] ^= 0x80;
        Keccak.F1600(_state);
        _pos = 0;
        _posBegin = 0;
    }

    private void Absorb(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            _state[_pos] ^= b;
            if (++_pos == R) RunF();
        }
    }

    private void Overwrite(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            _state[_pos] = b;
            if (++_pos == R) RunF();
        }
    }

    private void Squeeze(Span<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = _state[_pos];
            _state[_pos] = 0;
            if (++_pos == R) RunF();
        }
    }

    private void BeginOp(byte flags, bool more)
    {
        if (more)
        {
            if (_curFlags != flags) throw new InvalidOperationException("A STROBE operation was continued with different flags.");
            return;
        }

        var oldBegin = (byte)_posBegin;
        _posBegin = _pos + 1;
        _curFlags = flags;
        Absorb([oldBegin, flags]);

        // C or K forces the permutation before the operation's data.
        if ((flags & FlagC) != 0 && _pos != 0) RunF();
    }
}

/// <summary>Keccak-f[1600] on a 200-byte little-endian state (FIPS 202).</summary>
internal static class Keccak
{
    private static readonly ulong[] RoundConstants =
    [
        0x0000000000000001UL, 0x0000000000008082UL, 0x800000000000808AUL, 0x8000000080008000UL,
        0x000000000000808BUL, 0x0000000080000001UL, 0x8000000080008081UL, 0x8000000000008009UL,
        0x000000000000008AUL, 0x0000000000000088UL, 0x0000000080008009UL, 0x000000008000000AUL,
        0x000000008000808BUL, 0x800000000000008BUL, 0x8000000000008089UL, 0x8000000000008003UL,
        0x8000000000008002UL, 0x8000000000000080UL, 0x000000000000800AUL, 0x800000008000000AUL,
        0x8000000080008081UL, 0x8000000000008080UL, 0x0000000080000001UL, 0x8000000080008008UL,
    ];

    private static readonly int[] Rotations =
        [0, 1, 62, 28, 27, 36, 44, 6, 55, 20, 3, 10, 43, 25, 39, 41, 45, 15, 21, 8, 18, 2, 61, 56, 14];

    public static void F1600(byte[] state)
    {
        Span<ulong> a = stackalloc ulong[25];
        for (var i = 0; i < 25; i++) a[i] = BinaryPrimitives.ReadUInt64LittleEndian(state.AsSpan(i * 8));

        Span<ulong> c = stackalloc ulong[5];
        Span<ulong> b = stackalloc ulong[25];
        for (var round = 0; round < 24; round++)
        {
            // θ
            for (var x = 0; x < 5; x++) c[x] = a[x] ^ a[x + 5] ^ a[x + 10] ^ a[x + 15] ^ a[x + 20];
            for (var x = 0; x < 5; x++)
            {
                var d = c[(x + 4) % 5] ^ BitOperations.RotateLeft(c[(x + 1) % 5], 1);
                for (var y = 0; y < 25; y += 5) a[y + x] ^= d;
            }

            // ρ and π
            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
                b[y + (5 * ((2 * x + 3 * y) % 5))] = BitOperations.RotateLeft(a[x + 5 * y], Rotations[x + 5 * y]);

            // χ
            for (var y = 0; y < 25; y += 5)
            for (var x = 0; x < 5; x++)
                a[y + x] = b[y + x] ^ (~b[y + ((x + 1) % 5)] & b[y + ((x + 2) % 5)]);

            // ι
            a[0] ^= RoundConstants[round];
        }

        for (var i = 0; i < 25; i++) BinaryPrimitives.WriteUInt64LittleEndian(state.AsSpan(i * 8), a[i]);
    }
}
