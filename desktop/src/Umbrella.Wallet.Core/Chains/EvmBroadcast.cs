using Org.BouncyCastle.Crypto.Digests;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>What a node's answer to a raw transaction means.</summary>
public enum EvmBroadcastAnswer
{
    /// <summary>The node took it: it is in the mempool (or already was).</summary>
    Accepted,
    /// <summary>It can never be included as it stands, and nothing was spent — the fee is only paid by a
    /// transaction that makes it into a block.</summary>
    Rejected,
    /// <summary>The answer does not say either way. It may be in the mempool already; only the chain can
    /// settle it, and a fresh send could pay twice.</summary>
    Unclear,
}

/// <summary>
/// Reading an EVM node's answer to <c>eth_sendRawTransaction</c>, and the hash of a signed transaction.
///
/// The hash is worth computing locally: it is fixed the moment the transaction is signed, so a wallet
/// knows what to look for even when the answer to the broadcast never arrives. Without it, a lost
/// answer looks exactly like a failure — and a "failure" that was really a broadcast is how somebody
/// sends twice.
/// </summary>
public static class EvmBroadcast
{
    /// <summary>The transaction hash: Keccak-256 of the signed bytes, "0x" and lower-case hex.</summary>
    public static string Hash(string signedHex)
    {
        var hex = signedHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? signedHex[2..] : signedHex;
        var bytes = Convert.FromHexString(hex);
        var digest = new KeccakDigest(256);
        digest.BlockUpdate(bytes, 0, bytes.Length);
        var hash = new byte[32];
        digest.DoFinal(hash, 0);
        return "0x" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// What a node's JSON-RPC error means for a transaction that was already signed and sent.
    ///
    /// "Already known" is not a failure — the node has it, which is exactly what was wanted. A nonce the
    /// chain considers too low usually means this account's earlier transaction (possibly this very one,
    /// resent) is already mined, so it is unclear rather than refused. Everything a node refuses on its
    /// own terms — the fee, the funds, the gas, the signature — means it never entered a block.
    /// </summary>
    public static EvmBroadcastAnswer Classify(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return EvmBroadcastAnswer.Unclear;
        var message = error.ToLowerInvariant();

        if (Contains(message, "already known", "already imported", "already exists", "alreadyexists",
                "known transaction", "transaction already in pool", "duplicate transaction"))
            return EvmBroadcastAnswer.Accepted;

        if (Contains(message, "nonce too low", "oldnonce", "nonce is too low", "replacement transaction underpriced"))
            return EvmBroadcastAnswer.Unclear;

        if (Contains(message, "insufficient funds", "intrinsic gas too low", "gas limit", "underpriced",
                "invalid sender", "invalid signature", "exceeds block gas limit", "fee cap", "nonce too high",
                "oversized data", "transaction type not supported", "max fee per gas less than block base fee"))
            return EvmBroadcastAnswer.Rejected;

        // An error nobody here has seen before is not evidence that nothing was sent.
        return EvmBroadcastAnswer.Unclear;
    }

    private static bool Contains(string message, params string[] needles) =>
        needles.Any(n => message.Contains(n, StringComparison.Ordinal));
}
