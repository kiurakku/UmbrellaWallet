using System.Numerics;

namespace Umbrella.Wallet.Core.Polkadot;

/// <summary>An Asset Hub account as read before a send.</summary>
public sealed record DotAccount(uint Nonce, BigInteger Free, BigInteger Reserved, BigInteger Frozen)
{
    /// <summary>
    /// What a keep-alive transfer (and its fee) can take — pallet_balances' <c>reducible_balance</c> with
    /// Preserve: the free balance minus the larger of what is frozen beyond the reserve, and the
    /// existential deposit the account must keep.
    /// </summary>
    public BigInteger Spendable(BigInteger existentialDeposit)
    {
        var frozenBeyondReserve = BigInteger.Max(Frozen - Reserved, BigInteger.Zero);
        return BigInteger.Max(Free - BigInteger.Max(frozenBeyondReserve, existentialDeposit), BigInteger.Zero);
    }
}

/// <summary>Where a submitted Polkadot transfer stands.</summary>
public enum DotSubmitOutcome
{
    /// <summary>In a finalized block, and the dispatch succeeded.</summary>
    Included,
    /// <summary>Refused before any block, or its era ended without it. Nothing was sent.</summary>
    Rejected,
    /// <summary>In a finalized block but the dispatch failed: only the fee was charged.</summary>
    FailedFeeCharged,
    /// <summary>No answer either way.</summary>
    Unknown,
}

/// <summary>
/// Reading Asset Hub's answers for a send (roadmap N.8): the account record, the fee estimate, the
/// node's validation of a signed transfer, and the events of the block that holds it. Kept apart from the
/// network so each is tested offline; anything that does not parse is unknown, never a default.
/// </summary>
public static class PolkadotSendRules
{
    public const decimal PlanckPerDot = 10_000_000_000m;

    /// <summary>A positive DOT amount with at most ten decimal places, as planck.</summary>
    public static bool TryToPlanck(decimal dot, out BigInteger planck)
    {
        planck = BigInteger.Zero;
        if (dot <= 0) return false;
        var scaled = dot * PlanckPerDot;
        if (scaled != decimal.Truncate(scaled)) return false;
        planck = new BigInteger(scaled);
        return true;
    }

    public static decimal ToDot(BigInteger planck) => (decimal)planck / PlanckPerDot;

    /// <summary>
    /// <c>System.Account</c> from <c>state_getStorage</c>: null hex is an account that does not exist
    /// (the chain has nothing for it); anything that is not an 80-byte AccountInfo is unreadable.
    /// </summary>
    public static (bool Exists, DotAccount? Account) ParseAccount(string? hex, bool missing)
    {
        if (missing) return (false, new DotAccount(0, 0, 0, 0));
        if (hex is null || !hex.StartsWith("0x", StringComparison.Ordinal) || hex.Length != 2 + 160) return (false, null);
        var r = new ScaleReader(Convert.FromHexString(hex[2..]));
        var nonce = r.U32();
        r.U32(); r.U32(); r.U32();                    // consumers, providers, sufficients
        return (true, new DotAccount(nonce, r.U128(), r.U128(), r.U128()));
    }

    /// <summary>The fee from <c>TransactionPaymentApi_query_info</c>: weight (two compacts), class, then
    /// the partial fee as a u128.</summary>
    public static BigInteger? ParseQueryInfoFee(string? hex)
    {
        if (hex is null || !hex.StartsWith("0x", StringComparison.Ordinal)) return null;
        try
        {
            var r = new ScaleReader(Convert.FromHexString(hex[2..]));
            r.Compact();
            r.Compact();
            if (r.U8() > 2) return null;               // dispatch class: Normal, Operational, Mandatory
            var fee = r.U128();
            return r.AtEnd && fee > 0 ? fee : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static readonly string[] InvalidReasons =
    [
        "the call itself is invalid", "the account cannot pay the fee", "the nonce is ahead of the account",
        "this account has already sent a transaction with this nonce", "the signature does not match",
        "the transaction's era began too long ago", "the block has no room for it", "the runtime refused it",
        "a mandatory check failed", "a mandatory check failed", "the signer is not allowed",
        "the account could not be determined", "the origin is unknown",
    ];

    /// <summary>
    /// <c>TaggedTransactionQueue_validate_transaction</c>: null when the node would accept the transfer
    /// into its pool, otherwise the reason — before anything is broadcast.
    /// </summary>
    public static (bool Valid, string? Reason) ParseValidity(string? hex)
    {
        if (hex is null || !hex.StartsWith("0x", StringComparison.Ordinal) || hex.Length < 4) return (false, "The node gave no answer.");
        var bytes = Convert.FromHexString(hex[2..]);
        if (bytes[0] == 0x00) return (true, null);
        if (bytes.Length >= 3 && bytes[0] == 0x01 && bytes[1] == 0x00)
            return (false, bytes[2] < InvalidReasons.Length ? $"The network refused the transfer: {InvalidReasons[bytes[2]]}." : "The network refused the transfer.");
        return (false, "The network could not check the transfer right now.");
    }

    /// <summary>The position of an extrinsic among a block's (<c>chain_getBlock</c> hex strings), or -1.</summary>
    public static int IndexIn(IEnumerable<string> blockExtrinsics, byte[] extrinsic)
    {
        var target = "0x" + Convert.ToHexString(extrinsic).ToLowerInvariant();
        var i = 0;
        foreach (var e in blockExtrinsics)
        {
            if (string.Equals(e, target, StringComparison.OrdinalIgnoreCase)) return i;
            i++;
        }

        return -1;
    }

    /// <summary>
    /// Whether the extrinsic at <paramref name="index"/> succeeded, from the block's <c>System.Events</c>:
    /// the System pallet's ExtrinsicSuccess or ExtrinsicFailed with that extrinsic's phase. Every other
    /// event is walked past with the runtime's own types. Null when neither is found.
    /// </summary>
    public static bool? ExtrinsicSucceeded(RuntimeMetadata md, string eventsHex, int index)
    {
        if (md.PalletNamed("System") is not { } system || !system.PlainStorage.TryGetValue("Events", out var eventsType)) return null;
        if (md.Types[eventsType] is not { Kind: RuntimeMetadata.Kind.Sequence } seq) return null;
        if (md.Types[seq.Element] is not { Kind: RuntimeMetadata.Kind.Composite } record || record.Fields.Count != 3) return null;

        var phaseType = md.Types[record.Fields[0].Type];
        var eventType = md.Types[record.Fields[1].Type];
        var systemEvent = eventType.Variants.FirstOrDefault(v => v.Index == system.Index);
        if (systemEvent is not { Fields.Count: 1 }) return null;
        var systemEvents = md.Types[systemEvent.Fields[0].Type];
        var success = systemEvents.Variants.FirstOrDefault(v => v.Name == "ExtrinsicSuccess")?.Index;
        var failed = systemEvents.Variants.FirstOrDefault(v => v.Name == "ExtrinsicFailed")?.Index;
        var applyExtrinsic = phaseType.Variants.FirstOrDefault(v => v.Name == "ApplyExtrinsic")?.Index;
        if (success is null || failed is null || applyExtrinsic is null) return null;

        try
        {
            var r = new ScaleReader(Convert.FromHexString(eventsHex.StartsWith("0x", StringComparison.Ordinal) ? eventsHex[2..] : eventsHex));
            var count = r.CompactInt();
            for (var i = 0; i < count; i++)
            {
                var phase = r.Fork();
                var isOurs = phase.U8() == applyExtrinsic && phase.U32() == (uint)index;
                md.Skip(r, record.Fields[0].Type);

                var ev = r.Fork();
                var pallet = ev.U8();
                var variant = ev.U8();
                md.Skip(r, record.Fields[1].Type);
                md.Skip(r, record.Fields[2].Type);

                if (isOurs && pallet == system.Index)
                {
                    if (variant == success) return true;
                    if (variant == failed) return false;
                }
            }
        }
        catch (FormatException)
        {
            return null;
        }

        return null;
    }
}
