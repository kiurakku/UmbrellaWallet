using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// What an explorer's answer to a broadcast means.
///
/// The case that matters most reads like a failure: Esplora and the others return "already in the
/// mempool" with a 400. Calling that a failure is how a wallet pays twice — the user presses Retry,
/// the coins behind the first transaction now look spent, a different set is chosen, and both
/// transactions confirm. The messages below are the ones bitcoind and the explorers actually send.
/// </summary>
public sealed class UtxoBroadcastTests
{
    [Theory]
    [InlineData(true, "e1b2…")]                                                    // plain success: the txid
    [InlineData(false, "sendrawtransaction RPC error -27: txn-already-in-mempool")]
    [InlineData(false, "Transaction already in block chain")]
    [InlineData(false, "txn-already-known")]
    public void A_transaction_the_network_already_has_counts_as_sent(bool httpOk, string body) =>
        Assert.Equal(UtxoBroadcastAnswer.Accepted, UtxoBroadcast.Classify(httpOk, body));

    [Theory]
    [InlineData("sendrawtransaction RPC error: bad-txns-inputs-missingorspent")]
    [InlineData("min relay fee not met, 110 < 141")]
    [InlineData("dust")]
    [InlineData("non-mandatory-script-verify-flag (Signature must be zero for failed CHECK(MULTI)SIG operation)")]
    [InlineData("too-long-mempool-chain, too many descendants")]
    public void A_refusal_on_consensus_or_policy_grounds_means_nothing_was_sent(string body) =>
        Assert.Equal(UtxoBroadcastAnswer.Rejected, UtxoBroadcast.Classify(httpSuccess: false, body));

    [Theory]
    [InlineData("")]                                   // a dead connection: no answer at all
    [InlineData("502 Bad Gateway")]
    [InlineData("Service Temporarily Unavailable")]
    [InlineData("txn-mempool-conflict")]               // something is spending these coins — possibly us
    [InlineData("some explorer wording nobody has seen")]
    public void Anything_else_is_unclear_and_must_not_be_offered_as_a_retry(string body) =>
        Assert.Equal(UtxoBroadcastAnswer.Unclear, UtxoBroadcast.Classify(httpSuccess: false, body));

    [Fact]
    public void An_answerless_failure_is_unclear_rather_than_rejected()
    {
        // The difference is the whole point: "rejected" invites a retry, "unclear" must not.
        Assert.Equal(UtxoBroadcastAnswer.Unclear, UtxoBroadcast.Classify(httpSuccess: false, null));
        Assert.NotEqual(UtxoBroadcastAnswer.Rejected, UtxoBroadcast.Classify(httpSuccess: false, null));
    }
}
