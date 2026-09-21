using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NBitcoin.DataEncoders;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>One NEAR transfer, as it will be signed. Amounts are in yoctoNEAR (10^-24 NEAR).</summary>
public sealed record NearTransfer(
    string SignerId,
    byte[] PublicKey,
    ulong Nonce,
    string ReceiverId,
    byte[] BlockHash,
    BigInteger Yocto);

/// <summary>
/// NEAR transactions (roadmap N.7, send): the Borsh encoding of a Transaction carrying one Transfer,
/// the ed25519 signature over its SHA-256, and the SignedTransaction the RPC accepts — byte-for-byte
/// what near-api-js builds, pinned to its own "serialize and sign transfer tx" test
/// (<c>NearSendTests</c>).
///
/// Only a Transfer can be expressed. No function call, no access-key change, no deploy: nothing a
/// server answers can be turned into any of those.
/// </summary>
public static class NearTransactions
{
    public static readonly BigInteger YoctoPerNear = BigInteger.Pow(10, 24);

    private const byte KeyTypeEd25519 = 0;
    private const byte ActionTransfer = 3;

    /// <summary>The Borsh bytes of the Transaction — what is hashed and signed.</summary>
    public static byte[] TransactionBytes(NearTransfer t)
    {
        if (t.PublicKey.Length != 32) throw new ArgumentException("An ed25519 public key is 32 bytes.");
        if (t.BlockHash.Length != 32) throw new ArgumentException("A block hash is 32 bytes.");
        if (!IsValidAccountId(t.SignerId)) throw new ArgumentException("Not a NEAR account id.", nameof(t));
        if (!IsValidAccountId(t.ReceiverId)) throw new ArgumentException("Not a NEAR account id.", nameof(t));
        if (t.Yocto <= 0 || t.Yocto > MaxU128) throw new ArgumentException("A transfer moves a positive amount.");

        using var ms = new MemoryStream();
        WriteString(ms, t.SignerId);
        ms.WriteByte(KeyTypeEd25519);
        ms.Write(t.PublicKey);
        WriteU64(ms, t.Nonce);
        WriteString(ms, t.ReceiverId);
        ms.Write(t.BlockHash);
        WriteU32(ms, 1);                   // actions: exactly one
        ms.WriteByte(ActionTransfer);
        WriteU128(ms, t.Yocto);
        return ms.ToArray();
    }

    /// <summary>The transaction hash explorers show: base58 of SHA-256 of the Borsh bytes.</summary>
    public static string Hash(NearTransfer t) => Encoders.Base58.EncodeData(SHA256.HashData(TransactionBytes(t)));

    /// <summary>
    /// Signs with the 32-byte ed25519 seed and returns the SignedTransaction, base64, and its hash.
    /// Refuses a seed whose public key is not the one in the transaction.
    /// </summary>
    public static (string SignedBase64, string Hash, byte[] Signature) Sign(NearTransfer t, ReadOnlySpan<byte> seed)
    {
        if (seed.Length != Ed25519.SecretKeySize) throw new ArgumentException("An ed25519 seed is 32 bytes.");

        var sk = seed.ToArray();
        try
        {
            var pub = new byte[Ed25519.PublicKeySize];
            Ed25519.GeneratePublicKey(sk, 0, pub, 0);
            if (!pub.AsSpan().SequenceEqual(t.PublicKey))
                throw new InvalidOperationException("The key does not belong to the sending account.");

            var tx = TransactionBytes(t);
            var digest = SHA256.HashData(tx);
            var signature = new byte[Ed25519.SignatureSize];
            Ed25519.Sign(sk, 0, digest, 0, digest.Length, signature, 0);

            using var ms = new MemoryStream();
            ms.Write(tx);
            ms.WriteByte(KeyTypeEd25519);
            ms.Write(signature);
            return (Convert.ToBase64String(ms.ToArray()), Encoders.Base58.EncodeData(digest), signature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sk);
        }
    }

    /// <summary>"ed25519:&lt;base58&gt;", the form the RPC takes a public key in.</summary>
    public static string PublicKeyString(ReadOnlySpan<byte> publicKey) => "ed25519:" + Encoders.Base58.EncodeData(publicKey.ToArray());

    // --- account ids ---------------------------------------------------------------------------------

    private static readonly Regex AccountIdPattern =
        new(@"^(([a-z\d]+[\-_])*[a-z\d]+\.)*([a-z\d]+[\-_])*[a-z\d]+$", RegexOptions.CultureInvariant);

    /// <summary>
    /// NEAR's own rule for an account id: 2 to 64 characters, lowercase letters, digits and the
    /// separators "-", "_" and "." — never at an edge and never two in a row. Implicit accounts (64 hex
    /// characters) and Ethereum-style ones ("0x" + 40 hex) are account ids under the same rule.
    /// </summary>
    public static bool IsValidAccountId(string? id) =>
        id is { Length: >= 2 and <= 64 } && AccountIdPattern.IsMatch(id);

    /// <summary>An implicit account: the 64-hex public key itself. Paying one that does not exist yet creates it.</summary>
    public static bool IsImplicit(string id) => id.Length == 64 && id.All(Uri.IsHexDigit) && id == id.ToLowerInvariant();

    // --- amounts ---------------------------------------------------------------------------------------

    /// <summary>NEAR to yoctoNEAR, exactly: more than 24 decimals is refused rather than rounded.</summary>
    public static bool TryToYocto(decimal near, out BigInteger yocto)
    {
        yocto = BigInteger.Zero;
        if (near <= 0) return false;
        var text = near.ToString(CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        var whole = dot < 0 ? text : text[..dot];
        var frac = dot < 0 ? "" : text[(dot + 1)..].TrimEnd('0');
        if (frac.Length > 24) return false;
        yocto = BigInteger.Parse(whole, CultureInfo.InvariantCulture) * YoctoPerNear +
                (frac.Length == 0 ? BigInteger.Zero : BigInteger.Parse(frac.PadRight(24, '0'), CultureInfo.InvariantCulture));
        return yocto > 0;
    }

    public static decimal ToNear(BigInteger yocto) => (decimal)yocto / 1_000_000_000_000_000_000_000_000m;

    // --- Borsh ------------------------------------------------------------------------------------------

    private static readonly BigInteger MaxU128 = (BigInteger.One << 128) - 1;

    private static void WriteU32(Stream s, uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); s.Write(b); }
    private static void WriteU64(Stream s, ulong v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, v); s.Write(b); }

    private static void WriteU128(Stream s, BigInteger v)
    {
        var bytes = new byte[16];
        var raw = v.ToByteArray(isUnsigned: true, isBigEndian: false);
        raw.CopyTo(bytes, 0);
        s.Write(bytes);
    }

    private static void WriteString(Stream s, string v)
    {
        var bytes = Encoding.UTF8.GetBytes(v);
        WriteU32(s, (uint)bytes.Length);
        s.Write(bytes);
    }
}

/// <summary>What the RPC says about the sending account, for a transfer.</summary>
public sealed record NearAccountState(BigInteger Amount, BigInteger Locked, ulong StorageUsage)
{
    /// <summary>NEAR's storage price: 10^19 yoctoNEAR per byte (0.00001 NEAR). An account must keep
    /// enough to pay for the storage it uses, or the network refuses the transfer.</summary>
    public static readonly BigInteger StoragePricePerByte = BigInteger.Pow(10, 19);

    /// <summary>The liquid balance above the storage the account must keep paid for. Staked
    /// (locked) NEAR is not in <see cref="Amount"/> and is never spendable here.</summary>
    public BigInteger Spendable => BigInteger.Max(BigInteger.Zero, Amount - (StorageUsage * StoragePricePerByte));
}

/// <summary>Reading the NEAR RPC for a send (roadmap N.7). Anything not understood is null.</summary>
public static class NearRpc
{
    /// <summary>
    /// A generous cap for one transfer's fee: a transfer burns well under 0.0001 NEAR of gas at
    /// today's prices. The balance must cover the amount plus this, so a busy network never leaves a
    /// signed transfer that cannot pay for itself. Only the gas actually used is charged.
    /// </summary>
    public static readonly BigInteger FeeReserveYocto = BigInteger.Pow(10, 21);   // 0.001 NEAR

    /// <summary><c>query view_account</c>: the balance, stake and storage. Null when not understood;
    /// an account that does not exist is reported by <see cref="IsUnknownAccount"/>.</summary>
    public static NearAccountState? ParseViewAccount(JsonElement root)
    {
        if (!Result(root, out var r)) return null;
        if (!TryBig(r, "amount", out var amount) || !TryBig(r, "locked", out var locked)) return null;
        if (!r.TryGetProperty("storage_usage", out var su) || su.ValueKind != JsonValueKind.Number || !su.TryGetUInt64(out var usage)) return null;
        return new NearAccountState(amount, locked, usage);
    }

    /// <summary><c>query view_access_key</c>: the key's current nonce and the block it was read at,
    /// whose hash the transaction must carry.</summary>
    public static (ulong Nonce, byte[] BlockHash)? ParseAccessKey(JsonElement root)
    {
        if (!Result(root, out var r)) return null;
        if (!r.TryGetProperty("nonce", out var n) || n.ValueKind != JsonValueKind.Number || !n.TryGetUInt64(out var nonce)) return null;
        if (!r.TryGetProperty("block_hash", out var h) || h.ValueKind != JsonValueKind.String) return null;
        try
        {
            var hash = Encoders.Base58.DecodeData(h.GetString()!);
            return hash.Length == 32 ? (nonce, hash) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when the RPC says the account (or key) does not exist — a definite answer, not a failure.</summary>
    public static bool IsUnknownAccount(JsonElement root)
    {
        var text = root.ValueKind == JsonValueKind.Object ? root.GetRawText() : "";
        return text.Contains("UNKNOWN_ACCOUNT", StringComparison.Ordinal) ||
               text.Contains("UNKNOWN_ACCESS_KEY", StringComparison.Ordinal) ||
               text.Contains("does not exist while viewing", StringComparison.Ordinal);
    }

    private static bool Result(JsonElement root, out JsonElement result)
    {
        result = default;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out result) &&
               result.ValueKind == JsonValueKind.Object && !result.TryGetProperty("error", out _);
    }

    private static bool TryBig(JsonElement obj, string name, out BigInteger value)
    {
        value = BigInteger.Zero;
        return obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String &&
               BigInteger.TryParse(p.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>What became of a NEAR transaction handed to the RPC.</summary>
public enum NearSubmitOutcome
{
    /// <summary>Executed and the transfer succeeded.</summary>
    Included,

    /// <summary>Refused or failed, with the reason. The transfer did not happen.</summary>
    Rejected,

    /// <summary>
    /// No answer that says either way. A NEAR transaction stays valid for about a day (its block
    /// hash's validity period), so this is never offered as a retry.
    /// </summary>
    Unknown,
}

public sealed record NearSubmitResult(NearSubmitOutcome Outcome, string? Reason);

public static class NearSubmit
{
    /// <summary>Reads the answer to <c>send_tx</c> / <c>tx</c> (waited until executed).</summary>
    public static NearSubmitResult Parse(string? body)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(body ?? "").RootElement; }
        catch (JsonException) { return new NearSubmitResult(NearSubmitOutcome.Unknown, null); }
        if (root.ValueKind != JsonValueKind.Object) return new NearSubmitResult(NearSubmitOutcome.Unknown, null);

        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object)
        {
            if (status.TryGetProperty("SuccessValue", out _)) return new NearSubmitResult(NearSubmitOutcome.Included, null);
            if (status.TryGetProperty("Failure", out var failure))
                return new NearSubmitResult(NearSubmitOutcome.Rejected, Explain(failure.GetRawText()));
        }

        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var raw = error.GetRawText();
            // A timeout says nothing about whether the transaction will execute.
            if (raw.Contains("TIMEOUT_ERROR", StringComparison.Ordinal)) return new NearSubmitResult(NearSubmitOutcome.Unknown, null);
            if (raw.Contains("INVALID_TRANSACTION", StringComparison.Ordinal) ||
                raw.Contains("REQUEST_VALIDATION_ERROR", StringComparison.Ordinal))
                return new NearSubmitResult(NearSubmitOutcome.Rejected, Explain(raw));
        }

        return new NearSubmitResult(NearSubmitOutcome.Unknown, null);
    }

    /// <summary>The network's reason, in words a person can act on.</summary>
    public static string Explain(string raw)
    {
        if (raw.Contains("NotEnoughBalance", StringComparison.Ordinal) || raw.Contains("LackBalanceForState", StringComparison.Ordinal))
            return "Not enough NEAR once the storage this account must keep paid for is left behind. Nothing was sent.";
        if (raw.Contains("AccountDoesNotExist", StringComparison.Ordinal))
            return "The destination account does not exist. The transfer failed and the NEAR came back, less the gas.";
        if (raw.Contains("InvalidNonce", StringComparison.Ordinal))
            return "Another transaction was sent from this account in the meantime. Review the send again.";
        if (raw.Contains("Expired", StringComparison.Ordinal))
            return "The transaction expired before it was included. Nothing was sent.";
        if (raw.Contains("InvalidSignature", StringComparison.Ordinal) || raw.Contains("InvalidAccessKeyError", StringComparison.Ordinal))
            return "The network did not accept this key's signature. Nothing was sent.";
        return "NEAR refused the transaction. Nothing was sent.";
    }
}
