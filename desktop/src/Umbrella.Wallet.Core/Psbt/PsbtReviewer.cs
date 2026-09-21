using NBitcoin;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Core.Psbt;

/// <summary>One input of a PSBT under review. <see cref="ValueSat"/> is null when neither the wallet
/// nor the PSBT knows it — someone else's coin the PSBT does not describe.</summary>
public sealed record PsbtInputLine(OutPoint Outpoint, long? ValueSat, string? Address, bool Ours, bool AlreadySigned);

/// <summary>One output. <see cref="Ours"/> means it pays back to this wallet — change, or a transfer
/// between the wallet's own addresses.</summary>
public sealed record PsbtOutputLine(string Destination, long ValueSat, bool Ours);

/// <summary>
/// What a PSBT does to this wallet, and whether the wallet will sign it.
///
/// <see cref="Problems"/> are refusals: while any is present nothing is signed. <see cref="Warnings"/>
/// are things the user should read before deciding, and never block.
/// </summary>
public sealed record PsbtReview(
    IReadOnlyList<PsbtInputLine> Inputs,
    IReadOnlyList<PsbtOutputLine> Outputs,
    long? FeeSat,
    decimal? FeeRateSatPerVByte,
    long OursInSat,
    long OursOutSat,
    int SignableInputs,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Warnings)
{
    /// <summary>What this transaction takes out of the wallet, its share of the fee included.</summary>
    public long NetCostSat => OursInSat - OursOutSat;

    public bool HasForeignInputs => Inputs.Any(i => !i.Ours);

    public bool CanSign => Problems.Count == 0 && SignableInputs > 0;
}

/// <summary>
/// Reviews a PSBT against what the wallet has seen on-chain itself (roadmap H.1).
///
/// The rule that makes this safe to use with a PSBT from anywhere: the wallet signs only inputs that
/// match a coin ITS OWN SCAN found — same outpoint, same value, same script. It never takes a PSBT's
/// word for what one of its coins is worth. That closes the known attack on SegWit v0 signing, where
/// a coordinator misstates an input's amount across two signing rounds to trick the signer into
/// paying an enormous fee: the amount this wallet signs is the amount it read from the chain.
///
/// The price is that signing needs a synced wallet. This is not an air-gapped signer.
/// </summary>
public static class PsbtReviewer
{
    /// <summary>Above this, a fee rate is almost certainly a mistake or an attack.</summary>
    public const decimal SuspiciousFeeRate = 500m;

    public static PsbtReview Review(PSBT psbt, IReadOnlyList<OwnedUtxo> owned, OwnScripts own, Network network)
    {
        var problems = new List<string>();
        var warnings = new List<string>();
        var tx = psbt.GetGlobalTransaction();

        var ownedByOutpoint = new Dictionary<OutPoint, OwnedUtxo>();
        foreach (var u in owned) ownedByOutpoint[new OutPoint(uint256.Parse(u.TxId), (uint)u.Vout)] = u;

        var inputs = new List<PsbtInputLine>();
        long oursIn = 0;
        var allValuesKnown = true;
        var signable = 0;
        var anyOurTaproot = false;

        for (var i = 0; i < psbt.Inputs.Count; i++)
        {
            var pin = psbt.Inputs[i];
            var outpoint = tx.Inputs[i].PrevOut;
            var stated = pin.GetTxOut();
            var alreadySigned = pin.IsFinalized() || pin.PartialSigs.Count > 0 || pin.TaprootKeySignature is not null;

            if (ownedByOutpoint.TryGetValue(outpoint, out var mine))
            {
                var myScript = BitcoinAddress.Create(mine.Address, network).ScriptPubKey;

                if (stated is not null &&
                    (stated.Value.Satoshi != mine.ValueSat || stated.ScriptPubKey != myScript))
                {
                    problems.Add(
                        $"Input #{i + 1} describes your coin {Short(outpoint)} as {stated.Value.Satoshi} sat; " +
                        $"your wallet sees {mine.ValueSat} sat on-chain. A PSBT that misstates your own coin is not signed.");
                }

                if (pin.NonWitnessUtxo is { } prev && prev.GetHash() != outpoint.Hash)
                    problems.Add($"Input #{i + 1} carries a previous transaction that is not the one it spends.");

                oursIn += mine.ValueSat;
                if (!alreadySigned) signable++;
                if (mine.Path.IsTaproot) anyOurTaproot = true;

                inputs.Add(new PsbtInputLine(outpoint, mine.ValueSat, mine.Address, true, alreadySigned));
                continue;
            }

            // Not a coin the scan found. If it nevertheless pays from one of OUR addresses, the PSBT is
            // asking the wallet to sign for a coin it cannot see — spent already, a stale scan, or a coin
            // that does not exist. None of those should be signed on the PSBT's word.
            if (stated is not null && own.Contains(stated.ScriptPubKey))
            {
                problems.Add(
                    $"Input #{i + 1} spends from one of your addresses, but your wallet does not see that coin " +
                    "on-chain. Refresh balances; if it still does not appear, the PSBT refers to a coin you do not have.");
            }

            if (stated is null) allValuesKnown = false;

            inputs.Add(new PsbtInputLine(
                outpoint,
                stated?.Value.Satoshi,
                stated?.ScriptPubKey.GetDestinationAddress(network)?.ToString(),
                false,
                alreadySigned));
        }

        // A Taproot signature commits to the amount of EVERY input, not just its own. With another
        // input's value unknown there is nothing correct to sign.
        if (anyOurTaproot && !allValuesKnown && signable > 0)
            problems.Add("A Taproot signature covers every input's amount, and this PSBT does not state them all.");

        var outputs = new List<PsbtOutputLine>();
        long oursOut = 0, totalOut = 0;
        foreach (var o in tx.Outputs)
        {
            var ours = own.Contains(o.ScriptPubKey);
            if (ours) oursOut += o.Value.Satoshi;
            totalOut += o.Value.Satoshi;
            outputs.Add(new PsbtOutputLine(Describe(o.ScriptPubKey, network), o.Value.Satoshi, ours));
        }

        long? fee = null;
        decimal? rate = null;
        if (allValuesKnown)
        {
            var totalIn = inputs.Sum(x => x.ValueSat ?? 0);
            fee = totalIn - totalOut;

            if (fee < 0)
            {
                problems.Add("The outputs add up to more than the inputs. This is not a valid transaction.");
            }
            else if (psbt.TryGetVirtualSize(out var vsize) && vsize > 0)
            {
                rate = (decimal)fee.Value / vsize;
                if (rate > SuspiciousFeeRate)
                    warnings.Add($"The fee is {rate:0} sat/vB — far above anything the network needs. Check it before signing.");
            }
        }
        else
        {
            warnings.Add("The fee cannot be worked out: the PSBT does not say what the other inputs are worth. " +
                         "What leaves YOUR wallet is still exact — see the cost line.");
        }

        if (inputs.Count > 0 && inputs.Any(x => !x.Ours) && inputs.Any(x => x.Ours))
            warnings.Add("Inputs that are not yours are in this transaction. That is normal for a PayJoin, CoinJoin " +
                         "or multi-party payment — make sure you expected one.");

        return new PsbtReview(inputs, outputs, fee, rate, oursIn, oursOut, signable, problems, warnings);
    }

    private static string Describe(Script script, Network network)
    {
        if (TxNullDataTemplate.Instance.CheckScriptPubKey(script)) return "OP_RETURN (data, no coins)";
        return script.GetDestinationAddress(network)?.ToString() ?? "non-standard script";
    }

    private static string Short(OutPoint o)
    {
        var h = o.Hash.ToString();
        return $"{h[..8]}…:{o.N}";
    }
}
