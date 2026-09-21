using NBitcoin;
using NBitcoin.DataEncoders;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Psbt;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// PSBT export, review and signing (roadmap H.1).
///
/// The rule under test above all: the wallet signs only coins its own scan found on-chain, with the
/// value it read there — never with a value a PSBT supplied. Each refusal is provoked on its own.
/// </summary>
public sealed class PsbtTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string OtherPhrase =
        "legal winner thank year wave sausage worth useful legal winner thank yellow";
    private const string Dest = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4";

    private static readonly HdAddressDeriver Deriver = new();
    private static readonly HdUtxoSpender Spender = new(Deriver);

    private static OwnedUtxo Coin(uint index, long sat, char tx = 'a', UtxoScriptKind kind = UtxoScriptKind.Default)
    {
        var acct = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 0, index, kind: kind);
        return new OwnedUtxo(acct.Path, acct.Address, new string(tx, 64), 0, sat, true);
    }

    private static OwnScripts Own() => OwnScripts.For(Deriver, Phrase, ChainId.Btc, UtxoScanFloors.None);

    /// <summary>An unsigned PSBT of an ordinary send from <paramref name="coin"/>, change back to us.</summary>
    private static (PSBT Psbt, UtxoSpendPlan Plan, string Change) Unsigned(OwnedUtxo coin, long amount = 100_000)
    {
        var request = new UtxoSpendRequest(ChainId.Btc, Dest, amount, FeeRateSatPerVByte: 5);
        var (plan, error) = Spender.PlanSpend(ChainId.Btc, new[] { coin }, request);
        Assert.Null(error);

        var changePath = new UtxoDerivationPath(ChainId.Btc, 1, 0, plan!.ChangeKind);
        var change = Deriver.DeriveUtxoAccount(Phrase, changePath).Address;
        var (psbt, exportError) = Spender.BuildUnsignedPsbt(Phrase, plan, request, change, changePath);
        Assert.Null(exportError);
        return (psbt!, plan, change);
    }

    // ------------------------------------------------------------------------------------------------
    // Reading
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_PSBT_is_read_from_base64_hex_and_the_binary_file_alike()
    {
        var (psbt, _, _) = Unsigned(Coin(0, 500_000));
        var bytes = psbt.ToBytes();

        Assert.True(PsbtCodec.TryRead(psbt.ToBase64(), out var a, out _));
        Assert.True(PsbtCodec.TryRead(Encoders.Hex.EncodeData(bytes), out var b, out _));
        Assert.True(PsbtCodec.TryRead(bytes, out var c, out _));
        Assert.True(PsbtCodec.TryRead(System.Text.Encoding.UTF8.GetBytes(psbt.ToBase64() + "\n"), out var d, out _));

        foreach (var p in new[] { a, b, c, d })
            Assert.Equal(psbt.GetGlobalTransaction().GetHash(), p!.GetGlobalTransaction().GetHash());
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("SGVsbG8gd29ybGQ=")]   // valid base64, not a PSBT
    public void Anything_else_is_refused_with_a_reason(string text)
    {
        Assert.False(PsbtCodec.TryRead(text, out var psbt, out var error));
        Assert.Null(psbt);
        Assert.False(string.IsNullOrEmpty(error));
    }

    // ------------------------------------------------------------------------------------------------
    // Recognising our own
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void Own_scripts_cover_both_branches_and_both_chains_of_each()
    {
        var own = Own();

        foreach (var kind in new[] { UtxoScriptKind.Default, UtxoScriptKind.Taproot })
        foreach (var change in new[] { 0u, 1u })
        {
            var leaf = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, change, 7, kind: kind);
            Assert.True(own.TryGetPath(leaf.ScriptPubKey, out var path));
            Assert.Equal(leaf.Path, path);
        }

        var stranger = Deriver.DeriveBitcoinLikeAt(OtherPhrase, ChainId.Btc, 0, 0).ScriptPubKey;
        Assert.False(own.Contains(stranger));
    }

    [Fact]
    public void The_Taproot_account_xpub_derives_the_BIP86_published_address()
    {
        // The verify screen now exports the Taproot account too, because the wallet counts those
        // coins; an independent scanner given only the SegWit xpub would report less than the wallet
        // shows. Checked against BIP-86's own vector, not against ourselves.
        var xpub = Deriver.DeriveAccountXpub(Phrase, ChainId.Btc, kind: UtxoScriptKind.Taproot);
        var key = ExtPubKey.Parse(xpub, Network.Main);
        var first = key.Derive(0).Derive(0).PubKey.GetAddress(ScriptPubKeyType.TaprootBIP86, Network.Main);

        Assert.Equal("bc1p5cyxnuxmeuwuvkwfem96lqzszd02n6xdcjrs20cac6yqjjwudpxqkedrcr", first.ToString());
        Assert.Equal("m/86'/0'/0'", HdAddressDeriver.AccountXpubPath(ChainId.Btc, UtxoScriptKind.Taproot));
    }

    // ------------------------------------------------------------------------------------------------
    // Export
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void An_exported_PSBT_is_unsigned_and_names_the_paths_another_wallet_needs()
    {
        var coin = Coin(3, 500_000);
        var (psbt, _, change) = Unsigned(coin);
        var fingerprint = Deriver.MasterFingerprint(Phrase);

        Assert.False(psbt.IsAllFinalized());
        Assert.All(psbt.Inputs, i => Assert.Empty(i.PartialSigs));

        var inputPath = Assert.Single(psbt.Inputs[0].HDKeyPaths).Value;
        Assert.Equal(fingerprint, inputPath.MasterFingerprint);
        Assert.Equal(HdAddressDeriver.KeyPathFor(coin.Path), inputPath.KeyPath);

        var changeScript = BitcoinAddress.Create(change, Network.Main).ScriptPubKey;
        var changeOutput = psbt.Outputs.Single(o => o.ScriptPubKey == changeScript);
        Assert.NotEmpty(changeOutput.HDKeyPaths);
    }

    [Fact]
    public void A_Taproot_input_in_an_export_names_its_path_too()
    {
        // BIP-371: a Taproot input's derivation lives under its own key type. Without it, Sparrow or a
        // hardware wallet sees a coin it cannot tell is its own.
        var coin = Coin(2, 500_000, kind: UtxoScriptKind.Taproot);
        var (psbt, _, _) = Unsigned(coin);
        var fingerprint = Deriver.MasterFingerprint(Phrase);

        var tap = Assert.Single(psbt.Inputs[0].HDTaprootKeyPaths).Value;
        Assert.Equal(fingerprint, tap.RootedKeyPath.MasterFingerprint);
        Assert.Equal(HdAddressDeriver.KeyPathFor(coin.Path), tap.RootedKeyPath.KeyPath);
        Assert.NotNull(psbt.Inputs[0].TaprootInternalKey);

        // And the change output, which returns to the Taproot branch, is named the same way.
        Assert.Contains(psbt.Outputs, o => o.HDTaprootKeyPaths.Count == 1 && o.TaprootInternalKey is not null);
    }

    [Fact]
    public void A_Taproot_export_signed_back_here_completes_and_verifies()
    {
        var coin = Coin(2, 500_000, kind: UtxoScriptKind.Taproot);
        var (psbt, _, _) = Unsigned(coin);

        var (signed, count, complete, error) = Spender.SignOwnInputs(Phrase, psbt, new[] { coin });

        Assert.Null(error);
        Assert.Equal(1, count);
        Assert.True(complete);   // verified against the coin inside SignOwnInputs
        Assert.Single(signed!.ExtractTransaction().Inputs[0].WitScript.Pushes);
    }

    [Fact]
    public void An_exported_PSBT_signed_back_here_is_the_same_payment()
    {
        var coin = Coin(0, 500_000);
        var (psbt, plan, _) = Unsigned(coin);

        var (signed, count, complete, error) = Spender.SignOwnInputs(Phrase, psbt, new[] { coin });

        Assert.Null(error);
        Assert.Equal(1, count);
        Assert.True(complete);
        Assert.Equal(
            plan.AmountSat,
            signed!.ExtractTransaction().Outputs.First(o =>
                o.ScriptPubKey == BitcoinAddress.Create(Dest, Network.Main).ScriptPubKey).Value.Satoshi);
    }

    // ------------------------------------------------------------------------------------------------
    // Review
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void An_ordinary_payment_reads_as_amount_plus_fee_leaving_the_wallet()
    {
        var coin = Coin(0, 500_000);
        var (psbt, plan, _) = Unsigned(coin);

        var review = PsbtReviewer.Review(psbt, new[] { coin }, Own(), Network.Main);

        Assert.True(review.CanSign, string.Join(" | ", review.Problems));
        Assert.Equal(plan.AmountSat + plan.FeeSat, review.NetCostSat);
        Assert.Equal(plan.FeeSat, review.FeeSat);
        Assert.Contains(review.Outputs, o => o.Ours);          // the change is recognised as ours
        Assert.Contains(review.Outputs, o => !o.Ours && o.Destination == Dest);
        Assert.False(review.HasForeignInputs);
    }

    [Fact]
    public void A_PSBT_that_misstates_one_of_our_coins_is_refused()
    {
        // The SegWit v0 amount attack: the coordinator says our coin is worth less than it is, so the
        // "fee" it shows looks sane while the signature pays the difference to miners.
        var coin = Coin(0, 500_000);
        var (psbt, _, _) = Unsigned(coin);
        psbt.Inputs[0].WitnessUtxo = new TxOut(Money.Satoshis(120_000), psbt.Inputs[0].WitnessUtxo!.ScriptPubKey);

        var review = PsbtReviewer.Review(psbt, new[] { coin }, Own(), Network.Main);

        Assert.False(review.CanSign);
        Assert.Contains(review.Problems, p => p.Contains("describes your coin"));
    }

    [Fact]
    public void Our_address_with_a_coin_the_scan_did_not_find_is_refused()
    {
        // Spent already, a stale scan, or invented: in every case not something to sign on the PSBT's word.
        var coin = Coin(0, 500_000);
        var (psbt, _, _) = Unsigned(coin);

        var review = PsbtReviewer.Review(psbt, Array.Empty<OwnedUtxo>(), Own(), Network.Main);

        Assert.False(review.CanSign);
        Assert.Contains(review.Problems, p => p.Contains("does not see that coin"));
    }

    [Fact]
    public void A_foreign_input_of_unknown_value_leaves_the_fee_unknown_but_our_cost_exact()
    {
        var coin = Coin(0, 500_000);
        var psbt = Collaborative(coin, foreignValueStated: false, foreignSigned: false);

        var review = PsbtReviewer.Review(psbt, new[] { coin }, Own(), Network.Main);

        Assert.True(review.CanSign, string.Join(" | ", review.Problems));   // SegWit v0 signs per-input
        Assert.Null(review.FeeSat);
        Assert.Contains(review.Warnings, w => w.Contains("cannot be worked out"));
        Assert.True(review.HasForeignInputs);
        Assert.Equal(500_000 - OurChangeIn(psbt), review.NetCostSat);
    }

    [Fact]
    public void Our_Taproot_coin_beside_an_input_of_unknown_value_is_refused()
    {
        var coin = Coin(0, 500_000, kind: UtxoScriptKind.Taproot);
        var psbt = Collaborative(coin, foreignValueStated: false, foreignSigned: false);

        var review = PsbtReviewer.Review(psbt, new[] { coin }, Own(), Network.Main);

        Assert.False(review.CanSign);
        Assert.Contains(review.Problems, p => p.Contains("Taproot"));
    }

    [Fact]
    public void Outputs_worth_more_than_the_inputs_are_refused()
    {
        var coin = Coin(0, 500_000);
        var tx = Network.Main.CreateTransaction();
        tx.Inputs.Add(new TxIn(new OutPoint(uint256.Parse(coin.TxId), 0)));
        tx.Outputs.Add(new TxOut(Money.Satoshis(600_000), BitcoinAddress.Create(Dest, Network.Main)));
        var psbt = PSBT.FromTransaction(tx, Network.Main);

        var review = PsbtReviewer.Review(psbt, new[] { coin }, Own(), Network.Main);

        Assert.Contains(review.Problems, p => p.Contains("more than the inputs"));
    }

    [Fact]
    public void An_absurd_fee_rate_is_called_out()
    {
        var coin = Coin(0, 500_000);
        var tx = Network.Main.CreateTransaction();
        tx.Inputs.Add(new TxIn(new OutPoint(uint256.Parse(coin.TxId), 0)));
        tx.Outputs.Add(new TxOut(Money.Satoshis(100_000), BitcoinAddress.Create(Dest, Network.Main)));   // 400k to miners
        var psbt = PSBT.FromTransaction(tx, Network.Main);
        psbt.Inputs[0].WitnessUtxo = new TxOut(Money.Satoshis(500_000), Deriver.DeriveUtxoAccount(Phrase, coin.Path).ScriptPubKey);

        var review = PsbtReviewer.Review(psbt, new[] { coin }, Own(), Network.Main);

        Assert.Contains(review.Warnings, w => w.Contains("far above"));
        Assert.Equal(500_000, review.NetCostSat);   // and the cost line says exactly what leaves
    }

    [Fact]
    public void A_PSBT_with_nothing_of_ours_cannot_be_signed()
    {
        var coin = Coin(0, 500_000);
        var (psbt, _, _) = Unsigned(coin);

        // Reviewed as if by another wallet that owns none of it.
        var otherOwn = OwnScripts.For(Deriver, OtherPhrase, ChainId.Btc, UtxoScanFloors.None);
        var review = PsbtReviewer.Review(psbt, Array.Empty<OwnedUtxo>(), otherOwn, Network.Main);

        Assert.Equal(0, review.SignableInputs);
        Assert.False(review.CanSign);
    }

    // ------------------------------------------------------------------------------------------------
    // Signing
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_amount_we_sign_is_the_one_our_scan_read_not_the_one_the_PSBT_stated()
    {
        // Even called without the reviewer, the signer replaces a misstated value with the wallet's own
        // record — so the signature commits to the truth and the transaction verifies against it.
        var coin = Coin(0, 500_000);
        var (psbt, _, _) = Unsigned(coin);
        psbt.Inputs[0].WitnessUtxo = new TxOut(Money.Satoshis(1), psbt.Inputs[0].WitnessUtxo!.ScriptPubKey);

        var (signed, _, complete, error) = Spender.SignOwnInputs(Phrase, psbt, new[] { coin });

        Assert.Null(error);
        Assert.True(complete);
        Assert.Equal(500_000, signed!.Inputs[0].GetTxOut()!.Value.Satoshi);
    }

    [Fact]
    public void A_collaborative_PSBT_is_signed_for_our_part_and_completes_with_theirs()
    {
        var coin = Coin(0, 500_000);
        var psbt = Collaborative(coin, foreignValueStated: true, foreignSigned: true);

        var (signed, count, complete, error) = Spender.SignOwnInputs(Phrase, psbt, new[] { coin });

        Assert.Null(error);
        Assert.Equal(1, count);
        Assert.True(complete);                                 // verified against both coins inside
        Assert.Equal(2, signed!.ExtractTransaction().Inputs.Count);
    }

    [Fact]
    public void Without_the_other_signature_the_PSBT_goes_back_partially_signed()
    {
        var coin = Coin(0, 500_000);
        var psbt = Collaborative(coin, foreignValueStated: true, foreignSigned: false);

        var (signed, count, complete, error) = Spender.SignOwnInputs(Phrase, psbt, new[] { coin });

        Assert.Null(error);
        Assert.Equal(1, count);
        Assert.False(complete);
        var ours = signed!.Inputs.First(i => i.PrevOut.Hash == uint256.Parse(coin.TxId));
        Assert.NotEmpty(ours.PartialSigs);
    }

    // ------------------------------------------------------------------------------------------------

    private static readonly OutPoint ForeignOutpoint = new(uint256.Parse(new string('e', 64)), 1);

    /// <summary>Two parties, one input each: ours pays <see cref="Dest"/>, change back to us, and
    /// the other party's coin pays back to the other party.</summary>
    private static PSBT Collaborative(OwnedUtxo ours, bool foreignValueStated, bool foreignSigned)
    {
        var theirs = Deriver.DeriveBitcoinLikeAt(OtherPhrase, ChainId.Btc, 0, 0);
        var theirsBack = Deriver.DeriveBitcoinLikeAt(OtherPhrase, ChainId.Btc, 1, 0);
        var ourChange = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 1, 0, kind: ours.Path.Kind);
        var ourScript = Deriver.DeriveUtxoAccount(Phrase, ours.Path).ScriptPubKey;

        var tx = Network.Main.CreateTransaction();
        tx.Inputs.Add(new TxIn(new OutPoint(uint256.Parse(ours.TxId), (uint)ours.Vout)));
        tx.Inputs.Add(new TxIn(ForeignOutpoint));
        tx.Outputs.Add(new TxOut(Money.Satoshis(100_000), BitcoinAddress.Create(Dest, Network.Main)));
        tx.Outputs.Add(new TxOut(Money.Satoshis(399_000), ourChange.ScriptPubKey));
        tx.Outputs.Add(new TxOut(Money.Satoshis(299_000), theirsBack.ScriptPubKey));

        var psbt = PSBT.FromTransaction(tx, Network.Main);
        var foreignCoin = new TxOut(Money.Satoshis(300_000), theirs.ScriptPubKey);

        if (foreignSigned)
        {
            // The other party signs its own input with both UTXOs in view, then finalizes it.
            psbt.Inputs[0].WitnessUtxo = new TxOut(Money.Satoshis(ours.ValueSat), ourScript);
            psbt.Inputs[1].WitnessUtxo = foreignCoin;
            psbt.Inputs[1].Sign(theirs.PrivateKey);
            Assert.True(psbt.Inputs[1].TryFinalizeInput(out _));
            psbt.Inputs[0].WitnessUtxo = null;
        }
        else if (foreignValueStated)
        {
            psbt.Inputs[1].WitnessUtxo = foreignCoin;
        }

        return psbt;
    }

    private static long OurChangeIn(PSBT psbt)
    {
        var own = Own();
        return psbt.GetGlobalTransaction().Outputs.Where(o => own.Contains(o.ScriptPubKey)).Sum(o => o.Value.Satoshi);
    }
}
