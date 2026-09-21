using System.Text;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Utxo;

/// <summary>What the user wants to send, before inputs are chosen. Amounts are in satoshi.</summary>
public sealed record UtxoSpendRequest(
    ChainId Chain,
    string ToAddress,
    long AmountSat,
    double FeeRateSatPerVByte,
    long DevFeeSat = 0,
    string? DevFeeAddress = null,
    string? Memo = null);

/// <summary>
/// A concrete spend: which owned UTXOs fund it (drawn from ANY external or internal address), the
/// amounts, and how much change is due. The change address itself is assigned by the caller after it
/// durably reserves the next internal index — so the plan can be shown on a review screen before any
/// index is burned.
/// </summary>
public sealed record UtxoSpendPlan(
    ChainId Chain,
    IReadOnlyList<OwnedUtxo> Inputs,
    long InputSat,
    long AmountSat,
    long DevFeeSat,
    long FeeSat,
    long ChangeSat,
    /// <summary>Which branch the change address must be derived on (roadmap P2.1). Change goes back
    /// to the same script type the inputs came from, because an output whose type differs from the
    /// inputs is the one a chain-analysis heuristic reads as NOT the change — mixing types would
    /// point at the recipient.</summary>
    UtxoScriptKind ChangeKind = UtxoScriptKind.Default)
{
    public bool NeedsChange => ChangeSat > 0;
}

/// <summary>
/// Selects UTXOs across every address the wallet controls and signs a transaction, deriving each
/// input's key from the exact HD path it was received on — not just receive #0. Change returns to a
/// fresh internal (change=1) address. Pure and offline: no network, no store; the caller supplies the
/// discovered UTXOs and the reserved change address, so the whole spend is testable without funds
/// (roadmap §3.3).
/// </summary>
public sealed class HdUtxoSpender
{
    public const long DustSat = 546;

    /// <summary>
    /// The smallest output a chain will actually relay, in its own base units.
    ///
    /// Bitcoin's 546 is not a universal constant, and applying it to Dogecoin was wrong by more than
    /// two orders of magnitude. Dogecoin Core enforces a HARD dust limit of 0.001 DOGE - outputs below
    /// it are non-standard and the transaction is rejected outright - and a SOFT limit of 0.01 DOGE,
    /// below which each such output demands an extra 0.01 DOGE of fee or the transaction is rejected
    /// as underpaying. 546 koinu is 0.00000546 DOGE: under both.
    ///
    /// So a Dogecoin send could be planned, signed and handed to the network only to be refused,
    /// either because the amount itself was dust or because the CHANGE output was. The soft limit is
    /// used here, not the hard one: staying above it means no output ever triggers the extra fee, and
    /// anything smaller is rolled into the fee instead of becoming an unspendable scrap.
    ///
    /// Litecoin and Bitcoin Cash both kept Bitcoin's relay parameters, so 546 is right for them.
    /// </summary>
    public static long DustSatFor(ChainId chain) => chain switch
    {
        ChainId.Doge => 1_000_000,   // 0.01 DOGE — Dogecoin Core's soft dust limit
        _ => DustSat,
    };

    private readonly HdAddressDeriver _deriver;

    public HdUtxoSpender(HdAddressDeriver? deriver = null) => _deriver = deriver ?? new HdAddressDeriver();

    // Rough virtual sizes per input/output plus fixed overhead, by script type. Segwit (BTC/LTC
    // P2WPKH) is far smaller than legacy (DOGE P2PKH), and a Taproot key-path input is smaller again
    // — one 64-byte Schnorr signature and no public key in the witness.
    private static (int InputVb, int OutputVb, int OverheadVb) SizeModel(ScriptPubKeyType t) => t switch
    {
        ScriptPubKeyType.TaprootBIP86 => (58, 43, 11),
        ScriptPubKeyType.Segwit => (68, 31, 11),
        _ => (148, 34, 10),
    };

    /// <summary>The virtual size of one input, by the branch it was received on. A plan that draws
    /// from both branches has inputs of two different sizes, and estimating them all as one would
    /// either overpay or — worse — underpay and stall the transaction in the mempool.</summary>
    public static int InputVirtualSize(ChainId chain, UtxoScriptKind kind) => InputVbFor(chain, kind);

    private static int InputVbFor(ChainId chain, UtxoScriptKind kind)
    {
        if (kind == UtxoScriptKind.Taproot) return SizeModel(ScriptPubKeyType.TaprootBIP86).InputVb;
        var (_, _, _, scriptType) = HdAddressDeriver.BitcoinLikeParams(chain);
        return SizeModel(scriptType).InputVb;
    }

    /// <summary>
    /// The branch a plan's change must return to: the inputs' own, when they agree.
    ///
    /// The "change output looks like the inputs" heuristic cuts both ways. Send a Taproot spend's
    /// change to a SegWit address and an observer reads the remaining Taproot output — the
    /// recipient's — as the change, and the user as the owner of an address they do not control.
    /// Matching the inputs keeps the wallet from volunteering that.
    /// </summary>
    public static UtxoScriptKind ChangeKindFor(IReadOnlyList<OwnedUtxo> inputs) =>
        inputs.Count > 0 && inputs.All(u => u.Path.Kind == UtxoScriptKind.Taproot)
            ? UtxoScriptKind.Taproot
            : UtxoScriptKind.Default;

    /// <summary>
    /// Chooses confirmed inputs largest-first across all owned addresses and computes the fee and
    /// change. Returns an error string rather than throwing on bad input or insufficient funds.
    /// </summary>
    public (UtxoSpendPlan? Plan, string? Error) PlanSpend(
        ChainId chain, IReadOnlyList<OwnedUtxo> utxos, UtxoSpendRequest request)
    {
        if (request.AmountSat <= 0) return (null, "Amount must be positive.");
        var dust = DustSatFor(chain);
        if (request.AmountSat < dust) return (null, $"Amount is below the dust limit ({dust} sat).");
        if (request.FeeRateSatPerVByte <= 0) return (null, "Fee rate must be positive.");

        var (_, _, network, scriptType) = HdAddressDeriver.BitcoinLikeParams(chain);

        try { BitcoinAddress.Create(request.ToAddress, network); }
        catch { return (null, "That is not a valid destination address for this network."); }

        long devFee = 0;
        if (request.DevFeeSat > 0 && !string.IsNullOrWhiteSpace(request.DevFeeAddress))
        {
            try { BitcoinAddress.Create(request.DevFeeAddress!, network); devFee = request.DevFeeSat; }
            catch { devFee = 0; } // a bad fee address must never block the user's own transfer
        }

        if (!string.IsNullOrEmpty(request.Memo) && Encoding.ASCII.GetByteCount(request.Memo) > 80)
            return (null, "Memo is too long for an OP_RETURN (max 80 bytes).");

        var (_, outVb, overhead) = SizeModel(scriptType);
        var taprootOutVb = SizeModel(ScriptPubKeyType.TaprootBIP86).OutputVb;
        var opReturnVb = string.IsNullOrEmpty(request.Memo) ? 0 : Encoding.ASCII.GetByteCount(request.Memo) + 11;
        var baseOutputs = 1 + (devFee > 0 ? 1 : 0); // recipient (+ dev fee)

        // Only confirmed outputs are spendable — unconfirmed change can vanish on a reorg.
        var candidates = utxos.Where(u => u.Confirmed).OrderByDescending(u => u.ValueSat).ToList();
        if (candidates.Count == 0) return (null, "No confirmed spendable outputs.");

        var selected = new List<OwnedUtxo>();
        long total = 0, fee = 0, inputVb = 0;
        foreach (var u in candidates)
        {
            selected.Add(u);
            total += u.ValueSat;
            inputVb += InputVbFor(chain, u.Path.Kind);
            // Assume a change output while selecting; drop it below if change is dust. The change
            // output is sized on the branch it will actually land on, not on the chain default.
            var changeVb = ChangeKindFor(selected) == UtxoScriptKind.Taproot ? taprootOutVb : outVb;
            var vsize = inputVb + baseOutputs * outVb + changeVb + overhead + opReturnVb;
            fee = (long)Math.Ceiling(vsize * request.FeeRateSatPerVByte);
            if (total >= request.AmountSat + devFee + fee) break;
        }

        if (total < request.AmountSat + devFee + fee)
            return (null, $"Insufficient funds: have {total} sat, need {request.AmountSat + devFee + fee} sat including fee.");

        var change = total - request.AmountSat - devFee - fee;
        long changeSat;
        if (change > dust)
        {
            changeSat = change;
        }
        else
        {
            // Change would be dust: drop the change output and roll the remainder into the fee.
            fee = total - request.AmountSat - devFee;
            changeSat = 0;
        }

        return (new UtxoSpendPlan(
            chain, selected, total, request.AmountSat, devFee, fee, changeSat, ChangeKindFor(selected)), null);
    }

    /// <summary>
    /// Builds and signs the transaction for a plan. Each input's Coin carries its own scriptPubKey and
    /// each input's key is derived from its own path, so NBitcoin signs every input with the right key.
    /// When the plan has change, <paramref name="changeAddress"/> (a freshly reserved internal address)
    /// receives it. The signed transaction is verified before it is returned; nothing is broadcast here.
    /// </summary>
    public (Transaction? Tx, string? Error) BuildSigned(
        string mnemonic, UtxoSpendPlan plan, UtxoSpendRequest request, string? changeAddress)
    {
        try
        {
            var (builder, builderError) = PrepareBuilder(mnemonic, plan, request, changeAddress);
            if (builder is null) return (null, builderError);

            var tx = builder.BuildTransaction(sign: true);
            if (!builder.Verify(tx, out var errors))
                return (null, "Signature verification failed: " + string.Join("; ", errors.Select(e => e.ToString())));

            return (tx, null);
        }
        catch (Exception ex)
        {
            return (null, $"Build failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The same transaction as <see cref="BuildSigned"/>, as a signed and finalized PSBT — the
    /// "original" of a BIP-78 PayJoin (roadmap P2.2). It is a complete, broadcastable payment on its
    /// own: the receiver may broadcast it instead of cooperating, and the sender broadcasts it if the
    /// PayJoin fails, so it has to be exactly what the user reviewed.
    /// </summary>
    public (PSBT? Psbt, Transaction? Tx, string? Error) BuildOriginalPsbt(
        string mnemonic, UtxoSpendPlan plan, UtxoSpendRequest request, string? changeAddress)
    {
        try
        {
            var (builder, builderError) = PrepareBuilder(mnemonic, plan, request, changeAddress);
            if (builder is null) return (null, null, builderError);

            var psbt = builder.BuildPSBT(sign: true);
            if (!psbt.TryFinalize(out var finalizeErrors))
                return (null, null, "Could not finalize: " + string.Join("; ", finalizeErrors.Select(e => e.ToString())));

            var tx = psbt.ExtractTransaction();
            if (!builder.Verify(tx, out var errors))
                return (null, null, "Signature verification failed: " + string.Join("; ", errors.Select(e => e.ToString())));

            return (psbt, tx, null);
        }
        catch (Exception ex)
        {
            return (null, null, $"Build failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Signs the sender's inputs in a receiver's PayJoin proposal — only after
    /// <see cref="Payjoin.PayjoinProposalChecker"/> has accepted it — and returns the final transaction
    /// with every input, the receiver's included, verified against consensus rules.
    ///
    /// Only the outpoints of <paramref name="plan"/> are signed. If a receiver slipped in another coin
    /// that happens to belong to this wallet, it stays unsigned and the transaction fails to finalize;
    /// the wallet never signs an input the user did not review.
    /// </summary>
    public (Transaction? Tx, long FeeSat, string? Error) SignPayjoinProposal(
        string mnemonic, UtxoSpendPlan plan, PSBT original, PSBT proposal)
    {
        try
        {
            var (_, _, network, _) = HdAddressDeriver.BitcoinLikeParams(plan.Chain);
            var signed = proposal.Clone();

            var ours = new Dictionary<OutPoint, DerivedUtxoAccount>();
            foreach (var u in plan.Inputs)
                ours[new OutPoint(uint256.Parse(u.TxId), (uint)u.Vout)] = _deriver.DeriveUtxoAccount(mnemonic, u.Path);

            var originalUtxos = original.Inputs.ToDictionary(i => i.PrevOut, i => i.GetTxOut());

            foreach (var input in signed.Inputs)
            {
                if (!ours.ContainsKey(input.PrevOut))
                {
                    // Anything that is not one of the reviewed inputs must arrive already signed by the
                    // receiver. An unsigned stranger here could be another coin at one of OUR addresses
                    // (address reuse makes that possible), and signing it would spend money the user
                    // never saw on the review screen.
                    if (!input.IsFinalized())
                        return (null, 0, "The PayJoin proposal contains an input that is not yours and is not signed.");
                    continue;
                }

                // The receiver strips the sender's UTXO data (BIP-78); the sender restores it from its
                // OWN record, never from anything the receiver sent.
                input.WitnessUtxo = originalUtxos[input.PrevOut]
                                    ?? throw new InvalidOperationException("original input without its UTXO");
            }

            // Input by input, and only the reviewed outpoints — not SignWithKeys, which signs every
            // input a key happens to match.
            foreach (var input in signed.Inputs)
            {
                if (ours.TryGetValue(input.PrevOut, out var account)) input.Sign(account.PrivateKey);
            }

            if (!signed.TryFinalize(out var finalizeErrors))
                return (null, 0, "Could not finalize the PayJoin: " + string.Join("; ", finalizeErrors.Select(e => e.ToString())));

            var tx = signed.ExtractTransaction();

            // Every input — the receiver's as well as ours — checked against the output it spends.
            var builder = network.CreateTransactionBuilder();
            builder.DustPrevention = false;
            var coins = signed.Inputs.Select(i => i.GetSignableCoin() ?? i.GetCoin()).ToList();
            if (coins.Any(c => c is null)) return (null, 0, "A PayJoin input is missing its UTXO.");
            builder.AddCoins(coins!);
            if (!builder.Verify(tx, out var errors))
                return (null, 0, "PayJoin verification failed: " + string.Join("; ", errors.Select(e => e.ToString())));

            var fee = coins.Sum(c => c!.TxOut.Value.Satoshi) - tx.Outputs.Sum(o => o.Value.Satoshi);
            return (tx, fee, null);
        }
        catch (Exception ex)
        {
            return (null, 0, $"PayJoin signing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The builder both signing paths share: every input with its own key, the recipient, the
    /// service fee and memo when present, the planned fee, and change. One place, so the PSBT a
    /// PayJoin receiver sees and the transaction a plain send broadcasts cannot drift apart.
    /// </summary>
    private (TransactionBuilder? Builder, string? Error) PrepareBuilder(
        string mnemonic, UtxoSpendPlan plan, UtxoSpendRequest request, string? changeAddress)
    {
        var (_, _, network, _) = HdAddressDeriver.BitcoinLikeParams(plan.Chain);
        var builder = network.CreateTransactionBuilder();

        // We size every output ourselves in PlanSpend (the recipient is validated above the dust limit,
        // change below dust is rolled into the fee, and the memo rides a provably-unspendable zero-value
        // OP_RETURN). NBitcoin's dust guard would otherwise reject that OP_RETURN on some altcoin
        // networks (NBitcoin.Altcoins' BCash doesn't exempt it the way Bitcoin mainnet does), so we turn
        // the guard off — the plan, not the builder, is the authority on outputs.
        builder.DustPrevention = false;

        foreach (var input in plan.Inputs)
        {
            var account = _deriver.DeriveUtxoAccount(mnemonic, input.Path);
            var coin = new Coin(
                uint256.Parse(input.TxId), (uint)input.Vout,
                Money.Satoshis(input.ValueSat), account.ScriptPubKey);
            builder.AddCoins(coin);
            builder.AddKeys(account.PrivateKey);
        }

        builder.Send(BitcoinAddress.Create(request.ToAddress, network), Money.Satoshis(plan.AmountSat));

        if (plan.DevFeeSat > 0 && !string.IsNullOrWhiteSpace(request.DevFeeAddress))
            builder.Send(BitcoinAddress.Create(request.DevFeeAddress!, network), Money.Satoshis(plan.DevFeeSat));

        if (!string.IsNullOrWhiteSpace(request.Memo))
        {
            var memoBytes = Encoding.ASCII.GetBytes(request.Memo);
            if (memoBytes.Length > 80) return (null, "Memo exceeds the 80-byte OP_RETURN limit.");
            builder.Send(TxNullDataTemplate.Instance.GenerateScriptPubKey(memoBytes), Money.Zero);
        }

        builder.SendFees(Money.Satoshis(plan.FeeSat));

        if (plan.NeedsChange)
        {
            if (string.IsNullOrWhiteSpace(changeAddress))
                return (null, "A change address is required but was not supplied.");
            builder.SetChange(BitcoinAddress.Create(changeAddress, network));
        }

        return (builder, null);
    }
}
