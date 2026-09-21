using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using NBitcoin;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// A field of the XRP Ledger's binary format: its type code and its field code, which together fix
/// where it sits in a serialized transaction. Only the fields a wallet payment needs are named; the
/// codes are rippled's own (<c>definitions.json</c> in ripple-binary-codec).
/// </summary>
public readonly record struct XrplField(int TypeCode, int FieldCode)
{
    public static readonly XrplField TransactionType = new(1, 2);     // UInt16
    public static readonly XrplField Flags = new(2, 2);               // UInt32
    public static readonly XrplField SourceTag = new(2, 3);
    public static readonly XrplField Sequence = new(2, 4);
    public static readonly XrplField DestinationTag = new(2, 14);
    public static readonly XrplField LastLedgerSequence = new(2, 27);
    public static readonly XrplField Amount = new(6, 1);              // Amount
    public static readonly XrplField Fee = new(6, 8);
    public static readonly XrplField SigningPubKey = new(7, 3);       // Blob
    public static readonly XrplField TxnSignature = new(7, 4);
    public static readonly XrplField Domain = new(7, 7);
    public static readonly XrplField Account = new(8, 1);             // AccountID
    public static readonly XrplField Destination = new(8, 3);
    public static readonly XrplField Memos = new(15, 9);              // STArray

    /// <summary>The field header: one byte when both codes are under 16, more otherwise.</summary>
    internal void WriteHeader(Stream s)
    {
        if (TypeCode < 16 && FieldCode < 16) s.WriteByte((byte)((TypeCode << 4) | FieldCode));
        else if (TypeCode < 16) { s.WriteByte((byte)(TypeCode << 4)); s.WriteByte((byte)FieldCode); }
        else if (FieldCode < 16) { s.WriteByte((byte)FieldCode); s.WriteByte((byte)TypeCode); }
        else { s.WriteByte(0); s.WriteByte((byte)TypeCode); s.WriteByte((byte)FieldCode); }
    }
}

/// <summary>
/// An XRP Ledger transaction being assembled: fields and their encoded values, written in the
/// canonical order (by type code, then field code) whatever order they were set in.
/// </summary>
public sealed class XrplObject
{
    private readonly SortedDictionary<(int, int), (XrplField Field, byte[] Value)> _fields = new();

    public XrplObject UInt16(XrplField field, ushort value)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, value);
        return Set(field, b);
    }

    public XrplObject UInt32(XrplField field, uint value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        return Set(field, b);
    }

    /// <summary>An amount of XRP in drops: eight bytes, the "not an issued currency" bit clear and the
    /// "positive" bit set.</summary>
    public XrplObject Drops(XrplField field, long drops)
    {
        if (drops < 0 || drops > XrpTransactions.MaxDrops) throw new ArgumentOutOfRangeException(nameof(drops));
        var b = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(b, 0x4000_0000_0000_0000UL | (ulong)drops);
        return Set(field, b);
    }

    /// <summary>A variable-length blob: its length prefix, then the bytes.</summary>
    public XrplObject Blob(XrplField field, ReadOnlySpan<byte> value) => Set(field, WithLength(value));

    /// <summary>A 20-byte account id, length-prefixed like a blob.</summary>
    public XrplObject AccountId(XrplField field, ReadOnlySpan<byte> accountId)
    {
        if (accountId.Length != 20) throw new ArgumentException("An account id is 20 bytes.", nameof(accountId));
        return Set(field, WithLength(accountId));
    }

    /// <summary>A value already in its binary form, written after the field header as it is.</summary>
    public XrplObject Raw(XrplField field, ReadOnlySpan<byte> encoded) => Set(field, encoded.ToArray());

    /// <summary>The serialized object. For signing, the signature field itself is left out.</summary>
    public byte[] Serialize(bool forSigning)
    {
        using var ms = new MemoryStream();
        foreach (var (field, value) in _fields.Values)
        {
            if (forSigning && field == XrplField.TxnSignature) continue;
            field.WriteHeader(ms);
            ms.Write(value);
        }

        return ms.ToArray();
    }

    private XrplObject Set(XrplField field, byte[] value)
    {
        _fields[(field.TypeCode, field.FieldCode)] = (field, value);
        return this;
    }

    private static byte[] WithLength(ReadOnlySpan<byte> value)
    {
        var n = value.Length;
        byte[] prefix = n switch
        {
            <= 192 => [(byte)n],
            <= 12_480 => [(byte)(193 + ((n - 193) >> 8)), (byte)((n - 193) & 0xFF)],
            _ => throw new ArgumentException("That blob is too long for this wallet to write.", nameof(value)),
        };

        var result = new byte[prefix.Length + n];
        prefix.CopyTo(result, 0);
        value.CopyTo(result.AsSpan(prefix.Length));
        return result;
    }
}

/// <summary>One XRP payment, as it will be signed. Amounts are in drops (10^-6 XRP).</summary>
public sealed record XrpPayment(
    string Account,
    string Destination,
    long Drops,
    long FeeDrops,
    uint Sequence,
    uint LastLedgerSequence,
    uint? DestinationTag);

/// <summary>
/// XRP payments (roadmap N.4, send): the binary encoding of a Payment of XRP, the secp256k1 signature
/// over its SHA-512Half, and the transaction hash — byte-for-byte what xrpl.js builds, pinned to its
/// own wallet signing tests (<c>XrpSendTests</c>).
///
/// Only a plain Payment of XRP can be expressed: no issued currencies, no paths, no partial-payment
/// flag, no account or trust-line changes. Nothing a server answers can be turned into any of those.
/// </summary>
public static class XrpTransactions
{
    /// <summary>All the XRP there will ever be, in drops (100 billion XRP).</summary>
    public const long MaxDrops = 100_000_000_000L * 1_000_000L;

    private const ushort PaymentType = 0;

    /// <summary>tfFullyCanonicalSig. Every signature is canonical now anyway; xrpl.js still sets it,
    /// and so the bytes here match what it signs.</summary>
    public const uint FullyCanonicalSig = 0x8000_0000;

    private static readonly byte[] SignPrefix = [0x53, 0x54, 0x58, 0x00];       // "STX\0"
    private static readonly byte[] TxHashPrefix = [0x54, 0x58, 0x4E, 0x00];     // "TXN\0"

    public static decimal ToXrp(long drops) => drops / XrpLedger.DropsPerXrp;

    /// <summary>A positive XRP amount with at most six decimal places, as drops.</summary>
    public static bool TryToDrops(decimal xrp, out long drops)
    {
        drops = 0;
        if (xrp <= 0) return false;
        var scaled = xrp * XrpLedger.DropsPerXrp;
        if (scaled != decimal.Truncate(scaled) || scaled > MaxDrops) return false;
        drops = (long)scaled;
        return true;
    }

    /// <summary>The Payment's fields, unsigned. The signing key is added by <see cref="Sign"/>.</summary>
    public static XrplObject Payment(XrpPayment p)
    {
        if (!XrpAddress.TryDecode(p.Account, out var account)) throw new ArgumentException("Not an XRP address.", nameof(p));
        if (!XrpAddress.TryDecode(p.Destination, out var destination)) throw new ArgumentException("Not an XRP address.", nameof(p));
        if (p.Drops <= 0) throw new ArgumentException("A payment moves a positive amount.", nameof(p));
        if (p.FeeDrops <= 0) throw new ArgumentException("A transaction pays a fee.", nameof(p));

        var tx = new XrplObject()
            .UInt16(XrplField.TransactionType, PaymentType)
            .UInt32(XrplField.Flags, FullyCanonicalSig)
            .UInt32(XrplField.Sequence, p.Sequence)
            .UInt32(XrplField.LastLedgerSequence, p.LastLedgerSequence)
            .Drops(XrplField.Amount, p.Drops)
            .Drops(XrplField.Fee, p.FeeDrops)
            .AccountId(XrplField.Account, account)
            .AccountId(XrplField.Destination, destination);

        if (p.DestinationTag is { } tag) tx.UInt32(XrplField.DestinationTag, tag);
        return tx;
    }

    /// <summary>
    /// Signs a payment. The key must be the one the sending address comes from: a mismatch would be
    /// signed and then refused by every server, so it is refused here, before anything leaves.
    /// </summary>
    public static (string BlobHex, string Hash) SignPayment(XrpPayment p, Key key)
    {
        var own = XrpAddress.Encode(XrpAddress.AccountIdFromPublicKey(key.PubKey.Compress().ToBytes()));
        if (!string.Equals(own, p.Account, StringComparison.Ordinal))
            throw new InvalidOperationException("The signing key does not belong to the sending address.");
        return Sign(Payment(p), key);
    }

    /// <summary>
    /// Signs a transaction with a secp256k1 key: the public key goes in, the signing bytes are
    /// "STX\0" + the object without its signature, and the signature is deterministic (RFC 6979) with
    /// a low S — what rippled requires and what xrpl.js produces. Returns the blob to submit and the
    /// transaction hash explorers show.
    /// </summary>
    public static (string BlobHex, string Hash) Sign(XrplObject tx, Key key)
    {
        tx.Blob(XrplField.SigningPubKey, key.PubKey.Compress().ToBytes());

        var digest = Sha512Half(SignPrefix, tx.Serialize(forSigning: true));
        // useLowR: false — grinding for a short R would change the nonce, and the signature would
        // then differ from the RFC 6979 one every other XRPL library makes.
        var signature = key.Sign(new uint256(digest), useLowR: false).ToDER();
        tx.Blob(XrplField.TxnSignature, signature);

        var blob = tx.Serialize(forSigning: false);
        return (Convert.ToHexString(blob), Convert.ToHexString(Sha512Half(TxHashPrefix, blob)));
    }

    /// <summary>The first 32 bytes of SHA-512 over a prefix and a body.</summary>
    private static byte[] Sha512Half(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> body)
    {
        var data = new byte[prefix.Length + body.Length];
        prefix.CopyTo(data);
        body.CopyTo(data.AsSpan(prefix.Length));
        return SHA512.HashData(data)[..32];
    }
}

/// <summary>
/// The destination tag: the number an exchange or custodian uses to tell whose deposit an incoming
/// payment is. Any whole number from 0 to 4,294,967,295, or none at all.
/// </summary>
public static class XrpDestinationTag
{
    public static bool TryParse(string? text, out uint? tag, out string? error)
    {
        tag = null;
        error = null;
        var t = text?.Trim() ?? "";
        if (t.Length == 0) return true;

        // Digits only: "1e3", "+5", "5.0" or a thousands separator are not what an exchange showed.
        foreach (var c in t)
        {
            if (c is < '0' or > '9')
            {
                error = "A destination tag is a whole number (digits only), exactly as the recipient gave it.";
                return false;
            }
        }

        if (!uint.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            error = "A destination tag is at most 4294967295.";
            return false;
        }

        tag = value;
        return true;
    }
}
