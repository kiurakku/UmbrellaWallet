using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Payjoin;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// PayJoin, the sender's side (BIP-78), roadmap P2.2.
///
/// A receiver is simulated with a real key: it takes the sender's original payment, adds one of its
/// own coins, signs that coin, and takes the offered fee contribution from the sender's change — what
/// BTCPay Server and other BIP-78 receivers do. That honest proposal must be accepted, signed and pass
/// a full consensus check.
///
/// Then every rule of the checker is attacked on its own. Each test changes ONE thing about the honest
/// proposal and asserts the refusal names that thing, so a test cannot pass because some other rule
/// happened to trip first.
/// </summary>
public sealed class PayjoinSenderTests
{
    private const string SenderPhrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string ReceiverPhrase =
        "legal winner thank year wave sausage worth useful legal winner thank yellow";

    private static readonly HdAddressDeriver Deriver = new();
    private static readonly HdUtxoSpender Spender = new(Deriver);

    private sealed record Fixture(
        PSBT Original,
        Transaction OriginalTx,
        UtxoSpendPlan Plan,
        Script PaymentScript,
        Script ChangeScript,
        Script? DevFeeScript,
        PayjoinParameters Params,
        Key ReceiverKey,
        OutPoint ReceiverOutpoint,
        TxOut ReceiverCoin,
        int SenderInputVsize);

    private static Fixture NewFixture(
        UtxoScriptKind senderKind = UtxoScriptKind.Default,
        UtxoScriptKind receiverKind = UtxoScriptKind.Default,
        bool withDevFee = false)
    {
        var sender = Deriver.DeriveBitcoinLikeAt(SenderPhrase, ChainId.Btc, 0, 0, kind: senderKind);
        var utxos = new[] { new OwnedUtxo(sender.Path, sender.Address, new string('a', 64), 0, 500_000, true) };

        var receiverPay = Deriver.DeriveBitcoinLikeAt(ReceiverPhrase, ChainId.Btc, 0, 0);
        var devFeeAddress = withDevFee
            ? Deriver.DeriveBitcoinLikeAt(ReceiverPhrase, ChainId.Btc, 0, 9).Address
            : null;

        var request = new UtxoSpendRequest(
            ChainId.Btc, receiverPay.Address, 100_000, FeeRateSatPerVByte: 5,
            DevFeeSat: withDevFee ? 1_000 : 0, DevFeeAddress: devFeeAddress);

        var (plan, planError) = Spender.PlanSpend(ChainId.Btc, utxos, request);
        Assert.Null(planError);

        var change = Deriver.DeriveBitcoinLikeAt(SenderPhrase, ChainId.Btc, 1, 0, kind: plan!.ChangeKind).Address;
        var (psbt, tx, buildError) = Spender.BuildOriginalPsbt(SenderPhrase, plan, request, change);
        Assert.Null(buildError);

        var changeScript = BitcoinAddress.Create(change, Network.Main).ScriptPubKey;
        var feeIndex = tx!.Outputs.ToList().FindIndex(o => o.ScriptPubKey == changeScript);
        Assert.True(feeIndex >= 0);

        var originalFee = psbt!.GetFee().Satoshi;
        var rate = (decimal)originalFee / tx.GetVirtualSize();
        var inputVsize = HdUtxoSpender.InputVirtualSize(ChainId.Btc, senderKind);

        var receiverCoinAccount = Deriver.DeriveBitcoinLikeAt(ReceiverPhrase, ChainId.Btc, 0, 1, kind: receiverKind);

        return new Fixture(
            psbt, tx, plan,
            receiverPay.ScriptPubKey,
            changeScript,
            devFeeAddress is null ? null : BitcoinAddress.Create(devFeeAddress, Network.Main).ScriptPubKey,
            new PayjoinParameters(feeIndex, (long)Math.Ceiling(rate * inputVsize), Math.Floor(rate)),
            receiverCoinAccount.PrivateKey,
            new OutPoint(uint256.Parse(new string('c', 64)), 0),
            new TxOut(Money.Satoshis(300_000), receiverCoinAccount.ScriptPubKey),
            inputVsize);
    }

    /// <summary>What a well-behaved BIP-78 receiver sends back, with hooks to spoil one detail.</summary>
    private static PSBT Propose(
        Fixture f,
        long? contribution = null,
        Action<Transaction>? tamperTx = null,
        Action<PSBT>? tamperPsbt = null)
    {
        var tx = f.Original.GetGlobalTransaction().Clone();
        tx.Inputs.Add(new TxIn(f.ReceiverOutpoint) { Sequence = tx.Inputs[0].Sequence });

        // The receiver's coin is added to the payment it receives, and the offered contribution comes
        // out of the sender's change to pay for the extra input.
        tx.Outputs.First(o => o.ScriptPubKey == f.PaymentScript).Value += f.ReceiverCoin.Value;
        tx.Outputs.First(o => o.ScriptPubKey == f.ChangeScript).Value -=
            Money.Satoshis(contribution ?? f.Params.MaxFeeContributionSat);

        tamperTx?.Invoke(tx);

        var psbt = PSBT.FromTransaction(tx, Network.Main);

        // Taproot sighashes commit to every spent output, so the receiver signs with the whole set in
        // view (it has the sender's from the original) and strips the sender's again afterwards.
        foreach (var input in psbt.Inputs)
        {
            input.WitnessUtxo = input.PrevOut == f.ReceiverOutpoint
                ? f.ReceiverCoin
                : f.Original.Inputs.FindIndexedInput(input.PrevOut)?.GetTxOut();
        }

        var receiverInput = psbt.Inputs.FindIndexedInput(f.ReceiverOutpoint);
        if (receiverInput is not null)
        {
            receiverInput.Sign(f.ReceiverKey);
            Assert.True(receiverInput.TryFinalizeInput(out _));
        }

        foreach (var input in psbt.Inputs)
        {
            if (input.PrevOut != f.ReceiverOutpoint) input.WitnessUtxo = null;
        }

        tamperPsbt?.Invoke(psbt);
        return psbt;
    }

    private static PayjoinCheck CheckOf(Fixture f, PSBT proposal) =>
        PayjoinProposalChecker.Check(f.Original, proposal, f.PaymentScript, f.Params, f.SenderInputVsize);

    // ------------------------------------------------------------------------------------------------
    // The honest path
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void An_honest_proposal_is_accepted_signed_and_verifies()
    {
        var f = NewFixture();
        var proposal = Propose(f);

        var check = CheckOf(f, proposal);
        Assert.True(check.Ok, check.Reason);
        Assert.Equal(f.Params.MaxFeeContributionSat, check.FeeContributionSat);

        var (tx, fee, error) = Spender.SignPayjoinProposal(SenderPhrase, f.Plan, f.Original, proposal);
        Assert.Null(error);
        Assert.NotNull(tx);

        // Two inputs now: one the sender's, one the receiver's — which is the whole point. An observer
        // applying "all inputs belong to the payer" gets this transaction wrong.
        Assert.Equal(2, tx!.Inputs.Count);
        Assert.Contains(tx.Inputs, i => i.PrevOut == f.ReceiverOutpoint);

        // The receiver gets exactly what it was paid plus its own coin back.
        Assert.Equal(100_000 + 300_000, tx.Outputs.First(o => o.ScriptPubKey == f.PaymentScript).Value.Satoshi);

        Assert.Null(PayjoinProposalChecker.CheckFinalFeeRate(tx, fee, f.Params.MinFeeRateSatPerVByte));
    }

    [Fact]
    public void An_honest_Taproot_proposal_is_accepted_signed_and_verifies()
    {
        // The wallet can spend restored Taproot coins (P2.1); a PayJoin from them has to work too, and
        // the receiver must match with a Taproot coin of its own.
        var f = NewFixture(UtxoScriptKind.Taproot, UtxoScriptKind.Taproot);
        var proposal = Propose(f);

        var check = CheckOf(f, proposal);
        Assert.True(check.Ok, check.Reason);

        var (tx, _, error) = Spender.SignPayjoinProposal(SenderPhrase, f.Plan, f.Original, proposal);
        Assert.Null(error);
        Assert.Equal(2, tx!.Inputs.Count);
    }

    [Fact]
    public void The_original_is_itself_a_complete_payment()
    {
        // If the PayJoin fails the wallet broadcasts the original, and the receiver may broadcast it
        // instead of cooperating at all. So it has to be the reviewed payment, fully signed.
        var f = NewFixture();

        Assert.True(f.Original.IsAllFinalized());
        Assert.Single(f.OriginalTx.Inputs);
        Assert.Equal(100_000, f.OriginalTx.Outputs.First(o => o.ScriptPubKey == f.PaymentScript).Value.Satoshi);
        Assert.All(f.Original.Inputs, i => Assert.Empty(i.HDKeyPaths));   // no key paths leaked
    }

    // ------------------------------------------------------------------------------------------------
    // Money: every way the receiver could take more than the review allowed
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_lowered_payment_is_refused()
    {
        var f = NewFixture();
        var proposal = Propose(f, tamperTx: tx =>
            tx.Outputs.First(o => o.ScriptPubKey == f.PaymentScript).Value -= Money.Satoshis(300_001));

        Assert.Contains("lowered the payment", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void Taking_more_than_the_offered_contribution_is_refused()
    {
        var f = NewFixture();
        var proposal = Propose(f, contribution: f.Params.MaxFeeContributionSat + 1);

        Assert.Contains("more toward the fee than you offered", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void A_contribution_that_does_not_go_to_the_fee_is_refused()
    {
        // The receiver takes the contribution from the change and quietly adds it to its own payment:
        // the fee did not rise, so the "contribution" was just money moved to the receiver.
        var f = NewFixture();
        var proposal = Propose(f, tamperTx: tx =>
            tx.Outputs.First(o => o.ScriptPubKey == f.PaymentScript).Value +=
                Money.Satoshis(f.Params.MaxFeeContributionSat));

        Assert.Contains("something other than the fee", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void A_contribution_larger_than_the_added_input_costs_is_refused()
    {
        // Even within a generous offer, the receiver may only take what its own extra input costs at
        // the original fee rate.
        var f = NewFixture();
        f = f with { Params = f.Params with { MaxFeeContributionSat = 50_000 } };
        var proposal = Propose(f, contribution: 10_000);

        Assert.Contains("more toward the fee than its added inputs cost", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void A_lowered_network_fee_is_refused()
    {
        var f = NewFixture();
        var proposal = Propose(f, contribution: 0, tamperTx: tx =>
            tx.Outputs.First(o => o.ScriptPubKey == f.PaymentScript).Value += Money.Satoshis(500));

        Assert.Contains("lowered the network fee", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void A_changed_service_fee_output_is_refused()
    {
        // Every sender output other than the one named for the fee contribution is untouchable.
        var f = NewFixture(withDevFee: true);
        var proposal = Propose(f, tamperTx: tx =>
            tx.Outputs.First(o => o.ScriptPubKey == f.DevFeeScript).Value -= Money.Satoshis(100));

        Assert.Contains("changed one of your outputs", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void A_duplicated_change_output_is_refused()
    {
        var f = NewFixture();
        var proposal = Propose(f, tamperTx: tx => tx.Outputs.Add(new TxOut(Money.Satoshis(1_000), f.ChangeScript)));

        Assert.Contains("missing or duplicated", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void A_substituted_payment_address_is_refused()
    {
        // Output substitution is always disabled: whoever answers at the endpoint cannot redirect the
        // payment somewhere else.
        var f = NewFixture();
        var elsewhere = Deriver.DeriveBitcoinLikeAt(ReceiverPhrase, ChainId.Btc, 0, 7).ScriptPubKey;
        var proposal = Propose(f, tamperTx: tx =>
            tx.Outputs.First(o => o.ScriptPubKey == f.PaymentScript).ScriptPubKey = elsewhere);

        Assert.Contains("payment to the receiver is missing", CheckOf(f, proposal).Reason);
    }

    // ------------------------------------------------------------------------------------------------
    // Structure: inputs, sequences, version, the things that fingerprint or smuggle
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_dropped_sender_input_is_refused()
    {
        var f = NewFixture();
        var proposal = Propose(f, tamperTx: tx => tx.Inputs.RemoveAt(0));

        Assert.Contains("dropped one of your inputs", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void A_receiver_input_of_a_different_kind_is_refused()
    {
        // A SegWit payment with a Taproot input bolted on is a transaction nobody else makes — exactly
        // the fingerprint PayJoin is meant to remove.
        var f = NewFixture(UtxoScriptKind.Default, UtxoScriptKind.Taproot);
        var proposal = Propose(f);

        Assert.Contains("different kind of input", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void An_unsigned_receiver_input_is_refused()
    {
        var f = NewFixture();
        var proposal = Propose(f, tamperPsbt: p =>
        {
            var r = p.Inputs.FindIndexedInput(f.ReceiverOutpoint)!;
            r.FinalScriptWitness = null;
        });

        Assert.Contains("not signed", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void A_sender_input_described_by_the_receiver_is_refused()
    {
        // The sender signs against its OWN record of what it is spending. A proposal that tells it
        // what its input is worth is trying to change what gets signed.
        var f = NewFixture();
        var proposal = Propose(f, tamperPsbt: p =>
        {
            var s = p.Inputs.First(i => i.PrevOut != f.ReceiverOutpoint);
            s.WitnessUtxo = new TxOut(Money.Satoshis(1), f.ChangeScript);
        });

        Assert.Contains("describes your own input", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void A_receiver_input_whose_previous_transaction_does_not_match_is_refused()
    {
        var f = NewFixture();
        var proposal = Propose(f, tamperPsbt: p =>
        {
            var fake = Network.Main.CreateTransaction();
            fake.Outputs.Add(new TxOut(Money.Satoshis(900_000), f.ReceiverCoin.ScriptPubKey));
            p.Inputs.FindIndexedInput(f.ReceiverOutpoint)!.NonWitnessUtxo = fake;
        });

        Assert.Contains("does not match its previous transaction", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void A_changed_lock_time_is_refused()
    {
        var f = NewFixture();
        var proposal = Propose(f, tamperTx: tx => tx.LockTime = new LockTime(800_000));

        Assert.Contains("lock time", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void A_changed_version_is_refused()
    {
        var f = NewFixture();
        var proposal = Propose(f, tamperTx: tx => tx.Version = tx.Version == 1u ? 2u : 1u);

        Assert.Contains("version", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void Mismatched_sequences_are_refused()
    {
        var f = NewFixture();
        var proposal = Propose(f, tamperTx: tx => tx.Inputs[^1].Sequence = new Sequence(12345));

        Assert.Contains("same sequence", CheckOf(f, proposal).Reason);
    }

    [Fact]
    public void A_changed_sequence_on_the_senders_input_is_refused()
    {
        // Uniform, but not what the sender signed originally.
        var f = NewFixture();
        var proposal = Propose(f, tamperTx: tx =>
        {
            foreach (var i in tx.Inputs) i.Sequence = new Sequence(12345);
        });

        Assert.Contains("changed the sequence of one of your inputs", CheckOf(f, proposal).Reason);
    }

    // ------------------------------------------------------------------------------------------------
    // The signer, on its own: it must not rely on the checker having run
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_signer_refuses_an_unsigned_stranger_even_at_one_of_our_own_addresses()
    {
        // A receiver that knows the sender reused an address can add ANOTHER coin at that address,
        // unsigned, hoping the sender's key signs it too. The checker refuses this; the signer must
        // refuse it as well, without the checker's help.
        var f = NewFixture();
        var sameAddressCoin = new OutPoint(uint256.Parse(new string('d', 64)), 3);
        var ourScript = f.Original.Inputs[0].GetTxOut()!.ScriptPubKey;

        var tx = f.Original.GetGlobalTransaction().Clone();
        tx.Inputs.Add(new TxIn(sameAddressCoin) { Sequence = tx.Inputs[0].Sequence });
        tx.Outputs.First(o => o.ScriptPubKey == f.PaymentScript).Value += Money.Satoshis(200_000);
        var proposal = PSBT.FromTransaction(tx, Network.Main);
        proposal.Inputs.FindIndexedInput(sameAddressCoin)!.WitnessUtxo = new TxOut(Money.Satoshis(200_000), ourScript);

        var (signed, _, error) = Spender.SignPayjoinProposal(SenderPhrase, f.Plan, f.Original, proposal);

        Assert.Null(signed);
        Assert.Contains("not yours and is not signed", error);
    }

    [Fact]
    public void A_fee_rate_below_the_requested_minimum_is_reported()
    {
        var f = NewFixture();
        var (tx, fee, error) = Spender.SignPayjoinProposal(SenderPhrase, f.Plan, f.Original, Propose(f));
        Assert.Null(error);

        var rate = (decimal)fee / tx!.GetVirtualSize();
        Assert.NotNull(PayjoinProposalChecker.CheckFinalFeeRate(tx, fee, rate + 1));
        Assert.Null(PayjoinProposalChecker.CheckFinalFeeRate(tx, fee, Math.Floor(rate)));
    }
}
