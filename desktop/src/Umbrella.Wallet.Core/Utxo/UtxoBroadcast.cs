namespace Umbrella.Wallet.Core.Utxo;

/// <summary>What an explorer's answer to a raw transaction means.</summary>
public enum UtxoBroadcastAnswer
{
    /// <summary>The network has it — accepted now, or already there from an earlier attempt.</summary>
    Accepted,
    /// <summary>Refused by consensus or policy: it cannot be in a block, and nothing was spent.</summary>
    Rejected,
    /// <summary>No usable answer. It may be in the mempool; only the chain can settle it.</summary>
    Unclear,
}

/// <summary>
/// Reading a UTXO explorer's answer to a broadcast (Esplora, Haskoin, BlockCypher).
///
/// Two answers look like failures and are not. "Already in the mempool" is the transaction being
/// exactly where it was meant to go — the explorers return it with a 400. And a connection that dies
/// after the request has gone says nothing at all about whether the transaction was relayed.
///
/// Calling either of those a failure is how a wallet pays twice: the user presses Retry, the coins
/// that funded the first transaction now look spent, a DIFFERENT set of coins is chosen, and both
/// transactions confirm.
/// </summary>
public static class UtxoBroadcast
{
    /// <summary>The explorer already had this transaction, or took it now.</summary>
    private static readonly string[] AlreadyThere =
    [
        "already in mempool", "txn-already-in-mempool", "txn-already-known", "already known", "already-known",
        "transaction already in block chain", "already exists", "duplicate transaction",
        "transaction already in the mempool", "-27",   // bitcoind: transaction already in chain
    ];

    /// <summary>Refusals a node makes on its own terms: the transaction never entered a block.</summary>
    private static readonly string[] Refusals =
    [
        "bad-txns", "missingorspent", "min relay fee not met", "insufficient fee", "mempool min fee not met",
        "dust", "non-final", "non-mandatory-script-verify-flag", "mandatory-script-verify-flag-failed",
        "scriptsig", "too-long-mempool-chain", "tx-size", "version", "absurdly-high-fee",
        "sendrawtransaction rpc error", "invalid transaction", "decode failed",
    ];

    public static UtxoBroadcastAnswer Classify(bool httpSuccess, string? body)
    {
        var text = (body ?? "").ToLowerInvariant();

        // "Already in the mempool" is the answer that matters most, and it arrives as an error.
        if (AlreadyThere.Any(n => text.Contains(n, StringComparison.Ordinal))) return UtxoBroadcastAnswer.Accepted;
        if (httpSuccess) return UtxoBroadcastAnswer.Accepted;
        if (text.Length == 0) return UtxoBroadcastAnswer.Unclear;

        // A conflicting spend is a refusal of THIS transaction, but it also means some other transaction
        // is spending those coins — which may well be this same send, relayed a moment ago.
        if (text.Contains("txn-mempool-conflict", StringComparison.Ordinal)) return UtxoBroadcastAnswer.Unclear;

        return Refusals.Any(n => text.Contains(n, StringComparison.Ordinal))
            ? UtxoBroadcastAnswer.Rejected
            : UtxoBroadcastAnswer.Unclear;
    }
}
