using NBitcoin;

namespace Umbrella.Wallet.Core.Payjoin;

/// <summary>
/// What the sender tells the receiver it will tolerate (BIP-78 query parameters).
///
/// <see cref="DisableOutputSubstitution"/> is always true in this wallet. Substitution lets the
/// receiver replace the payment output with another of its choosing; it is a convenience for the
/// receiver and, for the sender, a way for whoever answers at the endpoint to redirect the payment.
/// Refusing it costs the receiver a little flexibility and closes that door entirely.
/// </summary>
public sealed record PayjoinParameters(
    int? FeeOutputIndex,
    long MaxFeeContributionSat,
    decimal MinFeeRateSatPerVByte,
    bool DisableOutputSubstitution = true);

/// <summary>The verdict on a receiver's proposal. <see cref="FeeContributionSat"/> is how much of the
/// sender's change the receiver took toward the fee — shown to the user afterwards.</summary>
public sealed record PayjoinCheck(bool Ok, string? Reason, long FeeContributionSat)
{
    public static PayjoinCheck Fail(string reason) => new(false, reason, 0);
}

/// <summary>
/// The sender's side of BIP-78: decides whether a PayJoin proposal from a receiver may be signed.
///
/// The receiver is not trusted. It already holds a fully signed payment (the original), and the only
/// thing it can gain by misbehaving is more of the sender's money or information — so every rule
/// below is about keeping the sender's cost exactly where the review screen left it, give or take a
/// fee contribution the sender offered up front and the user was shown:
///
/// <list type="number">
/// <item>Version and lock time unchanged; every input carries the same sequence, and the sender's
/// inputs keep the sequence they had.</item>
/// <item>No key paths and no signatures anywhere in the proposal. The sender's inputs come back
/// bare — not finalized, no UTXO data — so what the sender signs is the sender's own view of them.
/// Every receiver input comes back finalized with its UTXO, and of the SAME script type as the
/// sender's; a mixed-type transaction is exactly the fingerprint PayJoin exists to avoid.</item>
/// <item>Every one of the sender's inputs is still there.</item>
/// <item>The payment output is still there, once, and not smaller. Every other sender output —
/// change, the service fee, a memo — is still there, once, with its value unchanged, except the one
/// named fee output, which may shrink by at most the offered contribution, and only by as much as the
/// fee actually rose, and only by as much as the receiver's added inputs cost at the original rate.</item>
/// <item>The absolute fee did not fall.</item>
/// <item>And, independently of all the matching above, the sender's total cost did not rise by more
/// than the offered contribution — a last line that does not depend on the rules above being
/// complete.</item>
/// </list>
///
/// Anything the checker does not understand is a refusal. A refusal costs nothing: the original
/// payment is still valid and gets broadcast instead.
/// </summary>
public static class PayjoinProposalChecker
{
    public static PayjoinCheck Check(
        PSBT original,
        PSBT proposal,
        Script paymentScript,
        PayjoinParameters parameters,
        int senderInputVirtualSize)
    {
        try
        {
            return CheckCore(original, proposal, paymentScript, parameters, senderInputVirtualSize);
        }
        catch (Exception ex)
        {
            // A malformed proposal that trips NBitcoin is a proposal we cannot vouch for.
            return PayjoinCheck.Fail($"The receiver's proposal could not be read: {ex.Message}");
        }
    }

    private static PayjoinCheck CheckCore(
        PSBT original, PSBT proposal, Script paymentScript, PayjoinParameters p, int senderInputVsize)
    {
        var o = original.GetGlobalTransaction();
        var pr = proposal.GetGlobalTransaction();

        if (pr.Version != o.Version) return PayjoinCheck.Fail("The receiver changed the transaction version.");
        if (pr.LockTime != o.LockTime) return PayjoinCheck.Fail("The receiver changed the lock time.");

        // --- the sender's own inputs, as the ORIGINAL describes them ----------------------------------
        var senderCoins = new Dictionary<OutPoint, TxOut>();
        foreach (var input in original.Inputs)
        {
            var utxo = input.GetTxOut();
            if (utxo is null) return PayjoinCheck.Fail("The original payment is missing its input data.");
            senderCoins[input.PrevOut] = utxo;
        }

        var senderKinds = senderCoins.Values.Select(c => KindOf(c.ScriptPubKey)).Distinct().ToList();
        if (senderKinds.Count != 1)
            return PayjoinCheck.Fail("This payment spends more than one kind of address, so PayJoin was not attempted.");
        var senderKind = senderKinds[0];

        // --- inputs ------------------------------------------------------------------------------------
        if (pr.Inputs.Select(i => i.PrevOut).Distinct().Count() != pr.Inputs.Count)
            return PayjoinCheck.Fail("The receiver's proposal spends the same coin twice.");

        if (pr.Inputs.Select(i => (uint)i.Sequence).Distinct().Count() != 1)
            return PayjoinCheck.Fail("The receiver's inputs do not all use the same sequence number.");

        long receiverInputSat = 0;
        var senderInputsSeen = 0;

        for (var i = 0; i < proposal.Inputs.Count; i++)
        {
            var pin = proposal.Inputs[i];
            var txin = pr.Inputs[i];

            if (pin.HDKeyPaths.Count > 0 || pin.HDTaprootKeyPaths.Count > 0)
                return PayjoinCheck.Fail("The receiver's proposal carries key paths.");
            if (pin.PartialSigs.Count > 0)
                return PayjoinCheck.Fail("The receiver's proposal carries partial signatures.");

            if (senderCoins.ContainsKey(txin.PrevOut))
            {
                senderInputsSeen++;

                var originalSequence = o.Inputs.First(x => x.PrevOut == txin.PrevOut).Sequence;
                if (txin.Sequence != originalSequence)
                    return PayjoinCheck.Fail("The receiver changed the sequence of one of your inputs.");
                if (pin.IsFinalized() || pin.TaprootKeySignature is not null)
                    return PayjoinCheck.Fail("The receiver's proposal came back with your input already signed.");
                if (pin.WitnessUtxo is not null || pin.NonWitnessUtxo is not null)
                    return PayjoinCheck.Fail("The receiver's proposal describes your own input to you.");
            }
            else
            {
                if (!CarriesFinalSignature(pin))
                    return PayjoinCheck.Fail("One of the receiver's inputs is not signed.");

                var utxo = pin.WitnessUtxo;
                if (pin.NonWitnessUtxo is { } prev)
                {
                    // The full previous transaction must actually be the one the input spends, or its
                    // stated value is whatever the receiver chose to write.
                    if (prev.GetHash() != txin.PrevOut.Hash || txin.PrevOut.N >= prev.Outputs.Count)
                        return PayjoinCheck.Fail("One of the receiver's inputs does not match its previous transaction.");
                    utxo ??= prev.Outputs[(int)txin.PrevOut.N];
                }

                if (utxo is null)
                    return PayjoinCheck.Fail("One of the receiver's inputs does not say what it spends.");
                if (KindOf(utxo.ScriptPubKey) != senderKind)
                    return PayjoinCheck.Fail("The receiver added a different kind of input, which would mark the transaction.");

                receiverInputSat += utxo.Value.Satoshi;
            }
        }

        if (senderInputsSeen != senderCoins.Count)
            return PayjoinCheck.Fail("The receiver dropped one of your inputs.");

        // --- outputs -----------------------------------------------------------------------------------
        foreach (var pout in proposal.Outputs)
        {
            if (pout.HDKeyPaths.Count > 0 || pout.HDTaprootKeyPaths.Count > 0)
                return PayjoinCheck.Fail("The receiver's proposal carries key paths.");
        }

        var originalOutputs = o.Outputs.ToList();
        if (originalOutputs.Select(x => x.ScriptPubKey).Distinct().Count() != originalOutputs.Count)
            return PayjoinCheck.Fail("The original payment pays one address twice, so it cannot be checked.");

        long contribution = 0;
        long senderKeptInProposal = 0;
        var paymentSeen = false;

        for (var k = 0; k < originalOutputs.Count; k++)
        {
            var theirs = originalOutputs[k];
            var matches = pr.Outputs.Where(x => x.ScriptPubKey == theirs.ScriptPubKey).ToList();

            if (theirs.ScriptPubKey == paymentScript)
            {
                paymentSeen = true;
                if (!p.DisableOutputSubstitution) continue;

                if (matches.Count != 1)
                    return PayjoinCheck.Fail("The payment to the receiver is missing or split in the proposal.");
                if (matches[0].Value < theirs.Value)
                    return PayjoinCheck.Fail("The receiver lowered the payment amount.");
                continue;
            }

            if (matches.Count != 1)
                return PayjoinCheck.Fail("One of your outputs is missing or duplicated in the proposal.");

            var taken = theirs.Value.Satoshi - matches[0].Value.Satoshi;
            if (k == p.FeeOutputIndex)
            {
                // A receiver that ADDS to the sender's change is doing something this wallet cannot
                // explain, and an output that moved for no reason is not signed.
                if (taken < 0) return PayjoinCheck.Fail("The receiver changed your change output.");
                contribution = taken;
            }
            else if (taken != 0)
            {
                return PayjoinCheck.Fail("The receiver changed one of your outputs.");
            }

            senderKeptInProposal += matches[0].Value.Satoshi;
        }

        if (!paymentSeen)
            return PayjoinCheck.Fail("The original payment does not pay the receiver's address.");

        // --- fees --------------------------------------------------------------------------------------
        var senderInputSat = senderCoins.Values.Sum(c => c.Value.Satoshi);
        var originalFee = senderInputSat - originalOutputs.Sum(x => x.Value.Satoshi);
        var proposalFee = senderInputSat + receiverInputSat - pr.Outputs.Sum(x => x.Value.Satoshi);

        if (proposalFee < originalFee)
            return PayjoinCheck.Fail("The receiver lowered the network fee.");

        if (contribution > p.MaxFeeContributionSat)
            return PayjoinCheck.Fail("The receiver took more toward the fee than you offered.");
        if (contribution > proposalFee - originalFee)
            return PayjoinCheck.Fail("The receiver took part of your change for something other than the fee.");

        var originalVsize = original.ExtractTransaction().GetVirtualSize();
        var originalRate = (decimal)originalFee / originalVsize;
        var addedInputs = pr.Inputs.Count - o.Inputs.Count;
        var allowedForInputs = (long)Math.Ceiling(originalRate * senderInputVsize * Math.Max(0, addedInputs));
        if (contribution > allowedForInputs)
            return PayjoinCheck.Fail("The receiver took more toward the fee than its added inputs cost.");

        // --- the last line, independent of the matching above -----------------------------------------
        // Cost to the sender = what it puts in minus what comes back to it. Everything above should
        // already guarantee this; it is checked again because a hole in the rules above is exactly the
        // kind of bug a hostile receiver would go looking for.
        var originalCost = senderInputSat -
                           originalOutputs.Where(x => x.ScriptPubKey != paymentScript).Sum(x => x.Value.Satoshi);
        var proposalCost = senderInputSat - senderKeptInProposal;
        if (proposalCost > originalCost + p.MaxFeeContributionSat)
            return PayjoinCheck.Fail("The proposal would cost you more than the payment you reviewed.");

        return new PayjoinCheck(true, null, contribution);
    }

    /// <summary>
    /// The last BIP-78 rule, which can only be checked once the sender has signed: the final fee rate
    /// must not be below the minimum the sender asked for. Signatures are only known after signing, so
    /// this is separate from <see cref="Check"/>.
    /// </summary>
    public static string? CheckFinalFeeRate(Transaction signed, long feeSat, decimal minFeeRateSatPerVByte)
    {
        var rate = (decimal)feeSat / signed.GetVirtualSize();
        return rate + 0.0001m < minFeeRateSatPerVByte
            ? $"The PayJoin transaction pays {rate:0.##} sat/vB, below the {minFeeRateSatPerVByte:0.##} sat/vB you asked for."
            : null;
    }

    /// <summary>
    /// True when a finalized input actually carries something that could satisfy its script.
    ///
    /// NBitcoin's <c>IsFinalized()</c> is true when ANY final field is present — and finalizing a
    /// SegWit input leaves an empty <c>FinalScriptSig</c> behind. So an input whose witness has been
    /// removed still reports itself finalized while carrying no signature at all. The signer's
    /// consensus check would catch that later; the checker should not need it to.
    /// </summary>
    private static bool CarriesFinalSignature(PSBTInput input) =>
        (input.FinalScriptWitness is { } witness && witness.PushCount > 0) ||
        (input.FinalScriptSig is { } scriptSig && scriptSig.Length > 0);

    /// <summary>The script family of an output, for the "no mixed input types" rule.</summary>
    public static string KindOf(Script script)
    {
        if (PayToWitPubKeyHashTemplate.Instance.CheckScriptPubKey(script)) return "p2wpkh";
        if (PayToTaprootTemplate.Instance.CheckScriptPubKey(script)) return "p2tr";
        if (PayToWitScriptHashTemplate.Instance.CheckScriptPubKey(script)) return "p2wsh";
        if (PayToScriptHashTemplate.Instance.CheckScriptPubKey(script)) return "p2sh";
        if (PayToPubkeyHashTemplate.Instance.CheckScriptPubKey(script)) return "p2pkh";
        return "other";
    }
}
