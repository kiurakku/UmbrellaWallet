using NBitcoin;

namespace Umbrella.Wallet.Core.Payjoin;

/// <summary>
/// Decides what the sender offers a PayJoin receiver (roadmap P2.2).
///
/// The offer is deliberately small: enough of the sender's change to pay for ONE added input at the
/// payment's own fee rate, and never so much that the change would fall to dust. That is what BIP-78
/// describes as the fair split — each side pays for the inputs it adds — and it is the number shown on
/// the review screen before the user confirms, so the most the PayJoin can cost is known in advance.
/// </summary>
public static class PayjoinPlanner
{
    /// <summary>
    /// The most the offer can ever be for a payment at <paramref name="requestedFeeRate"/>: one sender
    /// input's worth. The review screen shows this number before Confirm, and
    /// <see cref="ParametersFor"/> never offers more, so what the user read is an upper bound.
    /// </summary>
    public static long OfferCapSat(double requestedFeeRate, int senderInputVirtualSize) =>
        (long)Math.Ceiling((decimal)requestedFeeRate * senderInputVirtualSize);

    public static PayjoinParameters ParametersFor(
        Transaction original, long originalFeeSat, Script? changeScript, int senderInputVirtualSize, long dustSat,
        long? offerCapSat = null)
    {
        var rate = (decimal)originalFeeSat / original.GetVirtualSize();

        // BIP-78 minfeerate: the receiver must not dilute the rate the user chose. Whole sat/vB, rounded
        // down, so the ±1 byte of a DER signature the receiver cannot predict does not fail an honest
        // proposal. Never below the relay minimum of 1.
        var minFeeRate = Math.Max(1m, Math.Floor(rate));

        if (changeScript is null)
            return new PayjoinParameters(null, 0, minFeeRate);

        var index = original.Outputs.ToList().FindIndex(o => o.ScriptPubKey == changeScript);
        if (index < 0)
            return new PayjoinParameters(null, 0, minFeeRate);

        var change = original.Outputs[index].Value.Satoshi;
        var forOneInput = (long)Math.Ceiling(rate * senderInputVirtualSize);
        var affordable = Math.Max(0, change - dustSat - 1);
        var offer = Math.Min(forOneInput, affordable);
        if (offerCapSat is { } cap) offer = Math.Min(offer, cap);

        return offer > 0
            ? new PayjoinParameters(index, offer, minFeeRate)
            : new PayjoinParameters(null, 0, minFeeRate);
    }
}
