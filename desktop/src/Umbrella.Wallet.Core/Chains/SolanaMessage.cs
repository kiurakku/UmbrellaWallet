namespace Umbrella.Wallet.Core.Chains;

/// <summary>An account an instruction touches, and how.</summary>
public sealed record SolanaAccountMeta(byte[] Key, bool IsSigner, bool IsWritable);

/// <summary>One instruction: the program that runs it, the accounts it touches, its data.</summary>
public sealed record SolanaInstruction(byte[] ProgramId, IReadOnlyList<SolanaAccountMeta> Accounts, byte[] Data);

/// <summary>
/// Solana legacy messages: every account once, in the order the runtime requires — signers that can
/// be written, read-only signers, writable accounts, read-only accounts (programs among them) — with
/// the fee payer first, then the recent blockhash and the instructions by index.
///
/// Within each group accounts are ordered by their bytes, as the Solana SDK's own compiler does.
/// Which order is chosen does not change what a transaction does; that the header counts and every
/// index agree with it is what matters, and a live validator's simulation checks that
/// (<c>SolanaSendLiveTests</c>).
/// </summary>
public static class SolanaMessage
{
    public static byte[] Compile(byte[] feePayer, IReadOnlyList<SolanaInstruction> instructions, byte[] recentBlockhash)
    {
        if (feePayer.Length != SolanaKeys.Length || recentBlockhash.Length != SolanaKeys.Length)
            throw new ArgumentException("Keys and blockhashes are 32 bytes.");

        // Every account once, its flags merged across the instructions that name it.
        var metas = new Dictionary<string, (byte[] Key, bool Signer, bool Writable)>(StringComparer.Ordinal)
        {
            [Convert.ToHexString(feePayer)] = (feePayer, true, true),
        };

        void Add(byte[] key, bool signer, bool writable)
        {
            if (key.Length != SolanaKeys.Length) throw new ArgumentException("An account key is 32 bytes.");
            var id = Convert.ToHexString(key);
            metas[id] = metas.TryGetValue(id, out var m) ? (m.Key, m.Signer || signer, m.Writable || writable) : (key, signer, writable);
        }

        foreach (var ix in instructions)
        {
            foreach (var a in ix.Accounts) Add(a.Key, a.IsSigner, a.IsWritable);
            Add(ix.ProgramId, false, false);
        }

        var payerId = Convert.ToHexString(feePayer);
        static int Group((byte[] Key, bool Signer, bool Writable) m) => (m.Signer, m.Writable) switch
        {
            (true, true) => 0,
            (true, false) => 1,
            (false, true) => 2,
            _ => 3,
        };

        var ordered = new List<(byte[] Key, bool Signer, bool Writable)> { metas[payerId] };
        ordered.AddRange(metas
            .Where(kv => kv.Key != payerId)
            .Select(kv => kv.Value)
            .OrderBy(Group)
            .ThenBy(m => Convert.ToHexString(m.Key), StringComparer.Ordinal));   // byte order

        var index = ordered.Select((m, i) => (Convert.ToHexString(m.Key), i)).ToDictionary(x => x.Item1, x => x.i);

        using var ms = new MemoryStream();
        ms.WriteByte((byte)ordered.Count(m => m.Signer));
        ms.WriteByte((byte)ordered.Count(m => m.Signer && !m.Writable));
        ms.WriteByte((byte)ordered.Count(m => !m.Signer && !m.Writable));

        WriteCompactU16(ms, ordered.Count);
        foreach (var m in ordered) ms.Write(m.Key);
        ms.Write(recentBlockhash);

        WriteCompactU16(ms, instructions.Count);
        foreach (var ix in instructions)
        {
            ms.WriteByte((byte)index[Convert.ToHexString(ix.ProgramId)]);
            WriteCompactU16(ms, ix.Accounts.Count);
            foreach (var a in ix.Accounts) ms.WriteByte((byte)index[Convert.ToHexString(a.Key)]);
            WriteCompactU16(ms, ix.Data.Length);
            ms.Write(ix.Data);
        }

        return ms.ToArray();
    }

    /// <summary>The wire form of a transaction with one signer: signature count, signature, message.</summary>
    public static byte[] Transaction(byte[] signature, byte[] message)
    {
        if (signature.Length != 64) throw new ArgumentException("An Ed25519 signature is 64 bytes.", nameof(signature));
        using var ms = new MemoryStream();
        WriteCompactU16(ms, 1);
        ms.Write(signature);
        ms.Write(message);
        return ms.ToArray();
    }

    /// <summary>Solana's "shortvec" length: seven bits a byte, low bits first.</summary>
    public static void WriteCompactU16(Stream s, int value)
    {
        if (value is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
        var v = value;
        while (v >= 0x80)
        {
            s.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }

        s.WriteByte((byte)v);
    }
}
