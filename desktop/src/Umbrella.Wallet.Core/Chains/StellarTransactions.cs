using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Umbrella.Wallet.Core.Chains;

public enum StellarMemoType
{
    None = 0,
    Text = 1,
    Id = 2,
}

/// <summary>
/// The memo on a Stellar payment. Exchanges credit a deposit by its memo, so a missing or wrong one
/// sends the money to the exchange but not to the account behind it.
/// </summary>
public sealed record StellarMemo(StellarMemoType Type, string? Text = null, ulong Id = 0)
{
    public static readonly StellarMemo None = new(StellarMemoType.None);

    /// <summary>Stellar caps a text memo at 28 bytes of UTF-8, not 28 characters.</summary>
    public const int MaxTextBytes = 28;

    /// <summary>
    /// What the user typed, read the way LOBSTR and most exchanges' instructions expect: nothing is no
    /// memo, a whole number that fits in 64 bits is an ID memo, anything else is text. The type is
    /// shown on the review so a numeric text memo is never silently sent as an ID.
    /// </summary>
    public static bool TryParse(string? input, out StellarMemo memo, out string? error)
    {
        memo = None;
        error = null;
        var s = input?.Trim() ?? "";
        if (s.Length == 0) return true;

        if (s.All(char.IsAsciiDigit) && ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            memo = new StellarMemo(StellarMemoType.Id, Id: id);
            return true;
        }

        if (Encoding.UTF8.GetByteCount(s) > MaxTextBytes)
        {
            error = $"A Stellar memo holds at most {MaxTextBytes} bytes.";
            return false;
        }

        memo = new StellarMemo(StellarMemoType.Text, Text: s);
        return true;
    }

    public override string ToString() => Type switch
    {
        StellarMemoType.Id => $"ID {Id.ToString(CultureInfo.InvariantCulture)}",
        StellarMemoType.Text => $"text \"{Text}\"",
        _ => "none",
    };
}

/// <summary>
/// One native-XLM transfer, as it will be signed. <see cref="CreatesAccount"/> is set when the
/// destination has never been funded: Stellar refuses a Payment to an address that is not yet an
/// account, so the first transfer to it has to be a CreateAccount that brings the minimum balance.
/// </summary>
public sealed record StellarTransfer(
    string Source,
    string Destination,
    long Stroops,
    bool CreatesAccount,
    uint Fee,
    long Sequence,
    StellarMemo Memo,
    ulong MaxTime);

/// <summary>
/// Stellar transactions (roadmap N.5, send): the XDR a <c>TransactionV1Envelope</c> is made of, the
/// signature base, and the ed25519 signature — byte-for-byte what the Stellar SDKs build, pinned to
/// transactions copied from the Go SDK's own tests (<c>StellarSendTests</c>).
///
/// Only what this wallet sends is encoded: one CreateAccount or Payment of native XLM, an optional
/// text or ID memo, and a time limit. Nothing here can express a trustline, an offer or a change of
/// signers, so nothing a server returns can be turned into one.
/// </summary>
public static class StellarTransactions
{
    public const string PublicNetwork = "Public Global Stellar Network ; September 2015";
    public const string TestNetwork = "Test SDF Network ; September 2015";

    public const long StroopsPerXlm = 10_000_000;

    /// <summary>The protocol's minimum fee per operation, in stroops.</summary>
    public const uint MinBaseFee = 100;

    private const int EnvelopeTypeTx = 2;
    private const int KeyTypeEd25519 = 0;
    private const int PreconditionTime = 1;
    private const int OpCreateAccount = 0;
    private const int OpPayment = 1;
    private const int AssetNative = 0;

    /// <summary>The <c>Transaction</c> XDR — the part that is hashed and signed.</summary>
    public static byte[] TransactionXdr(StellarTransfer t)
    {
        var source = StellarKeys.DecodeAccountId(t.Source);
        var destination = StellarKeys.DecodeAccountId(t.Destination);
        if (t.Stroops <= 0) throw new ArgumentException("A transfer moves a positive amount.", nameof(t));

        var x = new XdrWriter();

        // sourceAccount: MuxedAccount, plain ed25519 arm.
        x.Int32(KeyTypeEd25519);
        x.Fixed(source);

        x.UInt32(t.Fee);
        x.Int64(t.Sequence);

        // cond: always a time window. Without an upper bound a transaction that did not make it in could
        // be submitted by anyone who saw it, at any later time; with one it is void once the window shuts.
        x.Int32(PreconditionTime);
        x.UInt64(0);
        x.UInt64(t.MaxTime);

        WriteMemo(x, t.Memo);

        // operations<100>: exactly one, with no per-operation source.
        x.UInt32(1);
        x.Int32(0);
        if (t.CreatesAccount)
        {
            x.Int32(OpCreateAccount);
            x.Int32(KeyTypeEd25519);     // AccountID: a PublicKey, never muxed
            x.Fixed(destination);
            x.Int64(t.Stroops);
        }
        else
        {
            x.Int32(OpPayment);
            x.Int32(KeyTypeEd25519);     // destination: MuxedAccount, plain ed25519 arm
            x.Fixed(destination);
            x.Int32(AssetNative);
            x.Int64(t.Stroops);
        }

        x.Int32(0);                      // ext
        return x.ToArray();
    }

    /// <summary>The memo on its own, exactly as it sits inside <see cref="TransactionXdr"/>.</summary>
    public static byte[] MemoXdr(StellarMemo memo)
    {
        var x = new XdrWriter();
        WriteMemo(x, memo);
        return x.ToArray();
    }

    private static void WriteMemo(XdrWriter x, StellarMemo memo)
    {
        x.Int32((int)memo.Type);
        switch (memo.Type)
        {
            case StellarMemoType.Text:
                var bytes = Encoding.UTF8.GetBytes(memo.Text ?? "");
                if (bytes.Length > StellarMemo.MaxTextBytes) throw new ArgumentException("Memo text is over 28 bytes.");
                x.VarOpaque(bytes);
                break;
            case StellarMemoType.Id:
                x.UInt64(memo.Id);
                break;
        }
    }

    /// <summary>
    /// What is signed: SHA-256 of the network passphrase, the envelope type, then the transaction. The
    /// network id is what keeps a testnet signature from being valid on the public network.
    /// </summary>
    public static byte[] SignatureBase(string networkPassphrase, byte[] transactionXdr)
    {
        var x = new XdrWriter();
        x.Fixed(SHA256.HashData(Encoding.UTF8.GetBytes(networkPassphrase)));
        x.Int32(EnvelopeTypeTx);
        x.Fixed(transactionXdr);
        return x.ToArray();
    }

    /// <summary>The transaction id explorers show: SHA-256 of the signature base, in hex.</summary>
    public static string Hash(string networkPassphrase, byte[] transactionXdr) =>
        Convert.ToHexString(SHA256.HashData(SignatureBase(networkPassphrase, transactionXdr))).ToLowerInvariant();

    /// <summary>
    /// Signs <paramref name="t"/> with the 32-byte ed25519 seed and returns the base64 envelope Horizon
    /// accepts plus its hash. Refuses when the seed is not the key of the source account — a transaction
    /// the network would reject for a bad signature is still one that should never be built.
    /// </summary>
    public static (string EnvelopeBase64, string Hash) Sign(
        StellarTransfer t, ReadOnlySpan<byte> seed, string networkPassphrase = PublicNetwork)
    {
        if (seed.Length != Ed25519.SecretKeySize) throw new ArgumentException("An ed25519 seed is 32 bytes.");

        var sk = seed.ToArray();
        try
        {
            var pub = new byte[Ed25519.PublicKeySize];
            Ed25519.GeneratePublicKey(sk, 0, pub, 0);
            if (!pub.AsSpan().SequenceEqual(StellarKeys.DecodeAccountId(t.Source)))
                throw new InvalidOperationException("The key does not belong to the sending account.");

            var tx = TransactionXdr(t);
            var digest = SHA256.HashData(SignatureBase(networkPassphrase, tx));
            var signature = new byte[Ed25519.SignatureSize];
            Ed25519.Sign(sk, 0, digest, 0, digest.Length, signature, 0);

            var x = new XdrWriter();
            x.Int32(EnvelopeTypeTx);
            x.Fixed(tx);
            x.UInt32(1);                 // signatures<20>: one
            x.Fixed(pub.AsSpan(28, 4));  // hint: the last four bytes of the public key
            x.VarOpaque(signature);

            return (Convert.ToBase64String(x.ToArray()), Convert.ToHexString(digest).ToLowerInvariant());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sk);
        }
    }

    /// <summary>XLM to stroops, exactly. More than seven decimals is refused rather than rounded: the
    /// amount on the review has to be the amount that is signed.</summary>
    public static bool TryToStroops(decimal xlm, out long stroops)
    {
        stroops = 0;
        var scaled = xlm * StroopsPerXlm;
        if (xlm <= 0 || scaled != decimal.Truncate(scaled) || scaled > long.MaxValue) return false;
        stroops = (long)scaled;
        return true;
    }

    public static decimal ToXlm(long stroops) => stroops / (decimal)StroopsPerXlm;

    /// <summary>The minimal big-endian XDR primitives the transfer needs.</summary>
    private sealed class XdrWriter
    {
        private readonly MemoryStream _ms = new();

        public void Int32(int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); _ms.Write(b); }
        public void UInt32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); _ms.Write(b); }
        public void Int64(long v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(b, v); _ms.Write(b); }
        public void UInt64(ulong v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, v); _ms.Write(b); }
        public void Fixed(ReadOnlySpan<byte> bytes) => _ms.Write(bytes);

        /// <summary>Length-prefixed, zero-padded to a multiple of four.</summary>
        public void VarOpaque(ReadOnlySpan<byte> bytes)
        {
            UInt32((uint)bytes.Length);
            _ms.Write(bytes);
            for (var pad = (4 - bytes.Length % 4) % 4; pad > 0; pad--) _ms.WriteByte(0);
        }

        public byte[] ToArray() => _ms.ToArray();
    }
}

/// <summary>What Horizon says about the sending account: enough to number the next transaction and to
/// know how much of the balance the network will actually let go.</summary>
public sealed record StellarAccountState(long Sequence, long BalanceStroops, long LockedStroops)
{
    /// <summary>The balance above the minimum the account must keep and above what open offers have
    /// promised away. Never negative.</summary>
    public long SpendableStroops => Math.Max(0, BalanceStroops - LockedStroops);
}

public static class StellarSendRules
{
    /// <summary>
    /// The network's base reserve, 0.5 XLM since protocol 11. An account must keep
    /// (2 + subentries + sponsoring − sponsored) of these; if the network ever raises it, the
    /// submission is refused (<c>op_underfunded</c>) rather than anything being lost.
    /// </summary>
    public const long BaseReserveStroops = 5_000_000;

    /// <summary>What a brand-new account has to be funded with: two base reserves, 1 XLM.</summary>
    public const long MinimumNewAccountStroops = 2 * BaseReserveStroops;

    /// <summary>
    /// Reads Horizon's <c>/accounts/{id}</c> for sending. Unlike the balance read, a 404 here is not
    /// "zero" but "cannot send at all", and anything not understood is null — an unknown sequence
    /// number or reserve is a reason to stop, not to guess.
    /// </summary>
    public static StellarAccountState? ParseAccount(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!TryLong(root, "sequence", out var sequence)) return null;
        if (!root.TryGetProperty("balances", out var balances) || balances.ValueKind != JsonValueKind.Array) return null;

        long? balance = null, selling = null;
        foreach (var b in balances.EnumerateArray())
        {
            if (b.ValueKind != JsonValueKind.Object) return null;
            if (!b.TryGetProperty("asset_type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() != "native") continue;

            if (!TryStroops(b, "balance", out var bal)) return null;
            balance = bal;
            // Older Horizons omit liabilities; absent means none.
            selling = b.TryGetProperty("selling_liabilities", out _)
                ? TryStroops(b, "selling_liabilities", out var s) ? s : null
                : 0;
        }

        if (balance is null || selling is null) return null;

        var subentries = OptionalInt(root, "num_subentries");
        var sponsoring = OptionalInt(root, "num_sponsoring");
        var sponsored = OptionalInt(root, "num_sponsored");
        if (subentries is null || sponsoring is null || sponsored is null) return null;

        var reserves = 2L + subentries.Value + sponsoring.Value - sponsored.Value;
        return new StellarAccountState(sequence, balance.Value, (reserves * BaseReserveStroops) + selling.Value);
    }

    /// <summary>
    /// The fee to bid per operation, from Horizon's <c>/fee_stats</c>: the 95th percentile of what
    /// recent ledgers charged, never below the protocol minimum and never above 0.001 XLM. Stellar charges
    /// what the ledger needs, not the bid, so bidding high costs nothing when the network is quiet.
    /// Null when the answer is not understood.
    /// </summary>
    public static uint? ParseFeeBid(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("fee_charged", out var charged) || charged.ValueKind != JsonValueKind.Object) return null;
        if (!TryLong(charged, "p95", out var p95)) return null;
        var lastBase = TryLong(root, "last_ledger_base_fee", out var lb) ? lb : StellarTransactions.MinBaseFee;
        return (uint)Math.Clamp(Math.Max(p95, lastBase), StellarTransactions.MinBaseFee, MaxFeeBid);
    }

    /// <summary>0.001 XLM: the most a single transfer from this wallet will bid.</summary>
    public const uint MaxFeeBid = 10_000;

    private static bool TryLong(JsonElement obj, string name, out long value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String &&
               long.TryParse(p.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static int? OptionalInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p)) return 0;
        return p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) && v >= 0 ? v : null;
    }

    /// <summary>A Horizon amount ("12.3456789") in stroops. Zero is a valid answer here — a balance
    /// or a liability can be nothing — which is why this does not go through TryToStroops.</summary>
    private static bool TryStroops(JsonElement obj, string name, out long stroops)
    {
        stroops = 0;
        if (!obj.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String) return false;
        if (!decimal.TryParse(p.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var xlm)) return false;

        var scaled = xlm * StellarTransactions.StroopsPerXlm;
        if (scaled != decimal.Truncate(scaled) || scaled > long.MaxValue) return false;
        stroops = (long)scaled;
        return true;
    }
}

/// <summary>What became of a transaction handed to Horizon.</summary>
public enum StellarSubmitOutcome
{
    /// <summary>In a ledger: the payment happened.</summary>
    Included,

    /// <summary>Refused, with the network's reason. Nothing was sent; it is safe to review again.</summary>
    Rejected,

    /// <summary>
    /// No answer that says either way — a timeout, a 5xx. The transaction MAY be in a ledger, so this
    /// is never offered as a retry: a fresh send would take the next sequence number and pay twice.
    /// Its own time window (five minutes) is what eventually settles it.
    /// </summary>
    Unknown,
}

public sealed record StellarSubmitResult(StellarSubmitOutcome Outcome, string? Hash, string? Reason, IReadOnlyList<string> Codes);

/// <summary>Reads Horizon's answer to <c>POST /transactions</c> (roadmap N.5).</summary>
public static class StellarSubmit
{
    public static StellarSubmitResult Parse(int statusCode, string? body)
    {
        JsonElement root = default;
        var parsed = false;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try { root = JsonDocument.Parse(body).RootElement; parsed = root.ValueKind == JsonValueKind.Object; }
            catch (JsonException) { }
        }

        if (statusCode == 200 && parsed && root.TryGetProperty("hash", out var hash) && hash.ValueKind == JsonValueKind.String)
        {
            // Horizon answers 200 only once the transaction is in a ledger. "successful": false would
            // be a fee-charged failure; treat it as a rejection, never as a payment.
            var ok = !root.TryGetProperty("successful", out var s) || s.ValueKind != JsonValueKind.False;
            return ok
                ? new StellarSubmitResult(StellarSubmitOutcome.Included, hash.GetString(), null, [])
                : new StellarSubmitResult(StellarSubmitOutcome.Rejected, hash.GetString(), "The network included the transaction but it failed; only the fee was charged.", []);
        }

        if (statusCode == 400 && parsed)
        {
            var codes = new List<string>();
            if (root.TryGetProperty("extras", out var extras) && extras.ValueKind == JsonValueKind.Object &&
                extras.TryGetProperty("result_codes", out var rc) && rc.ValueKind == JsonValueKind.Object)
            {
                if (rc.TryGetProperty("transaction", out var tx) && tx.ValueKind == JsonValueKind.String) codes.Add(tx.GetString()!);
                if (rc.TryGetProperty("operations", out var ops) && ops.ValueKind == JsonValueKind.Array)
                    codes.AddRange(ops.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.String).Select(o => o.GetString()!));
            }

            return new StellarSubmitResult(StellarSubmitOutcome.Rejected, null, Explain(codes), codes);
        }

        // Anything else — 504 "timeout", other 5xx, a body we cannot read — says nothing about whether
        // the transaction made it in.
        return new StellarSubmitResult(StellarSubmitOutcome.Unknown, null, null, []);
    }

    /// <summary>The network's result codes, in words a person can act on.</summary>
    public static string Explain(IReadOnlyList<string> codes)
    {
        string? Has(params string[] any) => any.FirstOrDefault(codes.Contains);

        if (Has("op_underfunded", "tx_insufficient_balance", "op_low_reserve") is not null)
            return "Not enough XLM above the minimum balance this account must keep.";
        if (Has("op_no_destination") is not null)
            return "The destination is not a Stellar account yet. Sending it at least 1 XLM creates it.";
        if (Has("op_already_exists") is not null)
            return "The destination became an account in the meantime. Review the send again.";
        if (Has("tx_bad_seq") is not null)
            return "Another transaction was sent from this account in the meantime. Review the send again.";
        if (Has("tx_insufficient_fee") is not null)
            return "The network is busy and wanted a higher fee. Review the send again for a fresh fee.";
        if (Has("tx_too_late") is not null)
            return "The transaction's five-minute window closed before it was included. Nothing was sent.";
        if (Has("tx_bad_auth", "tx_bad_auth_extra") is not null)
            return "The network did not accept the signature. Nothing was sent.";
        if (Has("op_malformed", "tx_malformed") is not null)
            return "The network rejected the payment as malformed. Nothing was sent.";

        return codes.Count == 0
            ? "Stellar refused the transaction without saying why. Nothing was sent."
            : $"Stellar refused the transaction ({string.Join(", ", codes)}). Nothing was sent.";
    }
}
