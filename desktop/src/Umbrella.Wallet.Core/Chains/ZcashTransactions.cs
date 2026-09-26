using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>One transparent coin being spent, and what it is worth.</summary>
public sealed record ZcashInput(byte[] TxId, uint Index, ulong Zatoshi, byte[] ScriptPubKey)
{
    public byte[] Sequence { get; init; } = [0xFF, 0xFF, 0xFF, 0xFF];
}

/// <summary>One transparent output.</summary>
public sealed record ZcashOutput(ulong Zatoshi, byte[] ScriptPubKey);

/// <summary>
/// Zcash transparent transactions (roadmap: the last chain in the catalogue that could receive but not
/// send): a v4 (Sapling) transaction with transparent inputs and outputs only, signed with the
/// signature hash ZIP-243 defines.
///
/// Zcash does not sign the way Bitcoin does. The digest is BLAKE2b-256 personalised with the network
/// upgrade's consensus branch id, over committed hashes of the prevouts, sequences and outputs — so a
/// signature is bound to one upgrade of one chain and cannot be replayed onto another. Getting it wrong
/// does not produce a wrong payment; it produces a transaction the network simply refuses, which is why
/// this is pinned to Zcash's own sighash vectors (<c>ZcashSendTests</c>).
///
/// Shielded (z-address) sending is NOT here: these are transparent transactions, and the wallet says so.
/// </summary>
public static class ZcashTransactions
{
    /// <summary>v4, with the "overwintered" flag set in the top bit.</summary>
    public const uint Version = 0x8000_0004;
    public const uint VersionGroupId = 0x892F_2085;

    public const int SigHashAll = 1;
    private const int SigHashNone = 2;
    private const int SigHashSingle = 3;
    private const int SigHashAnyoneCanPay = 0x80;

    /// <summary>Zcash counts in zatoshi: 10^-8 ZEC, like Bitcoin's satoshi.</summary>
    public const decimal ZatoshiPerZec = 100_000_000m;

    /// <summary>
    /// Every mainnet network upgrade, newest last: the height it activated at and the consensus branch
    /// id that signs transactions under it. Copied from Zcash's own <c>upgrades.cpp</c> and
    /// <c>chainparams.cpp</c> — these are consensus constants, not settings.
    /// </summary>
    private static readonly (uint Height, uint BranchId)[] Upgrades =
    [
        (0, 0x0000_0000),           // Sprout
        (347_500, 0x5BA8_1B19),     // Overwinter
        (419_200, 0x76B8_09BB),     // Sapling
        (653_600, 0x2BB4_0E60),     // Blossom
        (903_000, 0xF5B9_230B),     // Heartwood
        (1_046_400, 0xE9FF_75A6),   // Canopy
        (1_687_104, 0xC2D6_D0B4),   // NU5
        (2_726_400, 0xC8E7_1055),   // NU6
        (3_146_400, 0x4DEC_4DF0),   // NU6.1
        (3_364_600, 0x5437_F330),   // NU6.2
    ];

    /// <summary>
    /// The consensus branch id in force at a height. A signature is bound to this, so a stale table
    /// after a future upgrade does not misdirect a payment — the network simply refuses the
    /// transaction, and nothing is spent.
    /// </summary>
    public static uint ConsensusBranchId(uint height)
    {
        var branch = Upgrades[0].BranchId;
        foreach (var (activation, id) in Upgrades)
        {
            if (height < activation) break;
            branch = id;
        }

        return branch;
    }

    /// <summary>How far ahead of the tip a transaction is given to be mined, in blocks (~75s each).</summary>
    public const uint ExpiryDelta = 40;

    /// <summary>A node refuses a transaction that would expire within this many blocks of its tip.</summary>
    public const uint ExpiringSoonThreshold = 3;

    /// <summary>
    /// The expiry height for a transaction that will first be mineable at <paramref name="nextHeight"/>,
    /// or null when a network upgrade is too close to send one at all.
    ///
    /// A transaction is signed for ONE upgrade: the one in force at the next block. If the next upgrade
    /// activated before it expired, the transaction would become invalid mid-life and sit in wallets'
    /// view as pending until it lapsed. Zcash's own wallet caps the expiry just below the activation
    /// height for exactly this reason, and so does this one; when that leaves fewer blocks than nodes
    /// accept, the honest answer is "wait until the upgrade is through".
    /// </summary>
    public static uint? ExpiryHeight(uint nextHeight)
    {
        var expiry = nextHeight + ExpiryDelta;
        foreach (var (activation, _) in Upgrades)
        {
            if (activation > nextHeight && activation <= expiry)
            {
                expiry = activation - 1;
                break;
            }
        }

        return expiry < nextHeight + ExpiringSoonThreshold ? null : expiry;
    }

    // ZIP-317: the fee every Zcash node expects. Below it a transaction is not relayed; above it the
    // sender is simply overpaying.
    private const ulong MarginalFee = 5_000;
    private const ulong GraceActions = 2;

    /// <summary>
    /// The ZIP-317 conventional fee for a transparent transaction with this many inputs and outputs.
    /// Counting one logical action per standard-sized input or output is ZIP-317's own arithmetic for
    /// P2PKH — a real input serialises to slightly UNDER the 150-byte standard, so this never
    /// underpays. A 1-in 2-out spend costs 10,000 zatoshi (0.0001 ZEC), which is what the network's
    /// own median fee shows being paid.
    /// </summary>
    public static ulong ConventionalFee(int inputs, int outputs)
    {
        var logicalActions = (ulong)Math.Max(Math.Max(inputs, outputs), 0);
        return MarginalFee * Math.Max(GraceActions, logicalActions);
    }

    /// <summary>
    /// The ZIP-243 signature hash for one input. <paramref name="branchId"/> is the consensus branch id
    /// of the upgrade the transaction will be mined under — the network's own answer, not a guess.
    /// </summary>
    public static byte[] SigHash(
        IReadOnlyList<ZcashInput> inputs,
        IReadOnlyList<ZcashOutput> outputs,
        int index,
        byte[] scriptCode,
        int hashType,
        uint lockTime,
        uint expiryHeight,
        uint branchId,
        long valueBalance = 0,
        byte[]? hashShieldedSpends = null,
        byte[]? hashShieldedOutputs = null,
        byte[]? hashJoinSplits = null)
    {
        var anyoneCanPay = (hashType & SigHashAnyoneCanPay) != 0;
        var baseType = hashType & 0x1F;

        var prevouts = new MemoryStream();
        var sequences = new MemoryStream();
        foreach (var input in inputs)
        {
            prevouts.Write(input.TxId);
            prevouts.Write(LE32(input.Index));
            sequences.Write(input.Sequence);
        }

        var outputsStream = new MemoryStream();
        foreach (var output in outputs)
        {
            outputsStream.Write(LE64(output.Zatoshi));
            WriteVarBytes(outputsStream, output.ScriptPubKey);
        }

        var hashPrevouts = anyoneCanPay ? new byte[32] : Blake2b("ZcashPrevoutHash", prevouts.ToArray());
        var hashSequence = anyoneCanPay || baseType is SigHashSingle or SigHashNone
            ? new byte[32]
            : Blake2b("ZcashSequencHash", sequences.ToArray());

        byte[] hashOutputs;
        if (baseType is not (SigHashSingle or SigHashNone))
        {
            hashOutputs = Blake2b("ZcashOutputsHash", outputsStream.ToArray());
        }
        else if (baseType == SigHashSingle && index < outputs.Count)
        {
            var single = new MemoryStream();
            single.Write(LE64(outputs[index].Zatoshi));
            WriteVarBytes(single, outputs[index].ScriptPubKey);
            hashOutputs = Blake2b("ZcashOutputsHash", single.ToArray());
        }
        else
        {
            hashOutputs = new byte[32];
        }

        using var ms = new MemoryStream();
        ms.Write(LE32(Version));
        ms.Write(LE32(VersionGroupId));
        ms.Write(hashPrevouts);
        ms.Write(hashSequence);
        ms.Write(hashOutputs);
        // This wallet builds transparent transactions only, so these are empty — but they are part of
        // the digest, and Zcash's own vectors exercise them, so they can be supplied.
        ms.Write(hashJoinSplits ?? new byte[32]);
        ms.Write(hashShieldedSpends ?? new byte[32]);
        ms.Write(hashShieldedOutputs ?? new byte[32]);
        ms.Write(LE32(lockTime));
        ms.Write(LE32(expiryHeight));
        ms.Write(LE64((ulong)valueBalance));
        ms.Write(LE32((uint)hashType));

        // The input being signed, its script and its value.
        var signed = inputs[index];
        ms.Write(signed.TxId);
        ms.Write(LE32(signed.Index));
        WriteVarBytes(ms, scriptCode);
        ms.Write(LE64(signed.Zatoshi));
        ms.Write(signed.Sequence);

        return Blake2b("ZcashSigHash", ms.ToArray(), branchId);
    }

    /// <summary>The transaction as it goes on the wire, with each input's signature script in place.</summary>
    public static byte[] Serialize(
        IReadOnlyList<ZcashInput> inputs,
        IReadOnlyList<ZcashOutput> outputs,
        IReadOnlyList<byte[]> scriptSigs,
        uint lockTime,
        uint expiryHeight)
    {
        if (scriptSigs.Count != inputs.Count) throw new ArgumentException("One signature script per input.", nameof(scriptSigs));

        using var ms = new MemoryStream();
        ms.Write(LE32(Version));
        ms.Write(LE32(VersionGroupId));

        WriteVarInt(ms, (ulong)inputs.Count);
        for (var i = 0; i < inputs.Count; i++)
        {
            ms.Write(inputs[i].TxId);
            ms.Write(LE32(inputs[i].Index));
            WriteVarBytes(ms, scriptSigs[i]);
            ms.Write(inputs[i].Sequence);
        }

        WriteVarInt(ms, (ulong)outputs.Count);
        foreach (var output in outputs)
        {
            ms.Write(LE64(output.Zatoshi));
            WriteVarBytes(ms, output.ScriptPubKey);
        }

        ms.Write(LE32(lockTime));
        ms.Write(LE32(expiryHeight));
        ms.Write(LE64(0));        // valueBalance: nothing shielded
        WriteVarInt(ms, 0);       // no shielded spends
        WriteVarInt(ms, 0);       // no shielded outputs
        WriteVarInt(ms, 0);       // no JoinSplits
        return ms.ToArray();
    }

    /// <summary>The transaction id explorers show: double SHA-256, reversed, as hex.</summary>
    public static string TxId(byte[] transaction)
    {
        var hash = SHA256.HashData(SHA256.HashData(transaction));
        Array.Reverse(hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>A P2PKH signature script: the DER signature with its hash type, then the public key.</summary>
    public static byte[] SignatureScript(byte[] derSignature, byte[] publicKey, int hashType = SigHashAll)
    {
        using var ms = new MemoryStream();
        WritePush(ms, [.. derSignature, (byte)hashType]);
        WritePush(ms, publicKey);
        return ms.ToArray();
    }

    /// <summary>A positive ZEC amount with at most eight decimal places, as zatoshi.</summary>
    public static bool TryToZatoshi(decimal zec, out ulong zatoshi)
    {
        zatoshi = 0;
        if (zec <= 0) return false;
        var scaled = zec * ZatoshiPerZec;
        if (scaled != decimal.Truncate(scaled) || scaled > ulong.MaxValue) return false;
        zatoshi = (ulong)scaled;
        return true;
    }

    public static decimal ToZec(ulong zatoshi) => zatoshi / ZatoshiPerZec;

    /// <summary>BLAKE2b-256 with a 16-byte personalisation; the signature hash appends the branch id.</summary>
    private static byte[] Blake2b(string personalisation, byte[] data, uint? branchId = null)
    {
        var person = new byte[16];
        Encoding.ASCII.GetBytes(personalisation).CopyTo(person, 0);
        if (branchId is { } id) LE32(id).CopyTo(person, 12);

        var digest = new Blake2bDigest(null, 32, null, person);
        digest.BlockUpdate(data, 0, data.Length);
        var hash = new byte[32];
        digest.DoFinal(hash, 0);
        return hash;
    }

    private static byte[] LE32(uint value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        return b;
    }

    private static byte[] LE64(ulong value)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, value);
        return b;
    }

    internal static void WriteVarInt(Stream s, ulong value)
    {
        switch (value)
        {
            case < 0xFD:
                s.WriteByte((byte)value);
                break;
            case <= 0xFFFF:
                s.WriteByte(0xFD);
                s.Write(BitConverter.GetBytes((ushort)value));
                break;
            case <= 0xFFFFFFFF:
                s.WriteByte(0xFE);
                s.Write(BitConverter.GetBytes((uint)value));
                break;
            default:
                s.WriteByte(0xFF);
                s.Write(BitConverter.GetBytes(value));
                break;
        }
    }

    private static void WriteVarBytes(Stream s, byte[] data)
    {
        WriteVarInt(s, (ulong)data.Length);
        s.Write(data);
    }

    /// <summary>A minimal push of up to 520 bytes (all a signature script needs).</summary>
    private static void WritePush(Stream s, byte[] data)
    {
        switch (data.Length)
        {
            case < 0x4C:
                s.WriteByte((byte)data.Length);
                break;
            case <= 0xFF:
                s.WriteByte(0x4C);
                s.WriteByte((byte)data.Length);
                break;
            default:
                throw new ArgumentException("A signature script pushes at most 255 bytes here.", nameof(data));
        }

        s.Write(data);
    }
}
