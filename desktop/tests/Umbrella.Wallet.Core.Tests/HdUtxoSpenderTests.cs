using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Core.Tests;

public sealed class HdUtxoSpenderTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string Dest = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4";

    private static readonly HdAddressDeriver Deriver = new();
    private static readonly HdUtxoSpender Spender = new(Deriver);

    private static OwnedUtxo Utxo(uint change, uint index, long sat, bool confirmed = true)
    {
        var acct = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, change, index);
        return new OwnedUtxo(acct.Path, acct.Address, Guid.NewGuid().ToString("N")[..8].PadLeft(64, '0'), 0, sat, confirmed);
    }

    [Fact]
    public void Insufficient_funds_is_reported_not_signed()
    {
        var utxos = new[] { Utxo(0, 0, 50_000) };
        var req = new UtxoSpendRequest(ChainId.Btc, Dest, 100_000, FeeRateSatPerVByte: 1);
        var (plan, error) = Spender.PlanSpend(ChainId.Btc, utxos, req);

        Assert.Null(plan);
        Assert.Contains("Insufficient", error);
    }

    [Fact]
    public void Unconfirmed_outputs_are_not_spendable()
    {
        // Plenty of value, but all of it unconfirmed — a reorg could erase it, so it must not be spent.
        var utxos = new[]
        {
            Utxo(0, 0, 10_000, confirmed: true),
            Utxo(0, 1, 500_000, confirmed: false),
        };
        var req = new UtxoSpendRequest(ChainId.Btc, Dest, 100_000, FeeRateSatPerVByte: 1);
        var (plan, error) = Spender.PlanSpend(ChainId.Btc, utxos, req);

        Assert.Null(plan);
        Assert.Contains("Insufficient", error);
    }

    [Fact]
    public void Dust_change_is_dropped_and_rolled_into_the_fee()
    {
        var utxos = new[] { Utxo(0, 0, 100_000) };
        // Chosen so the leftover after a normal fee is below the dust limit.
        var req = new UtxoSpendRequest(ChainId.Btc, Dest, 99_700, FeeRateSatPerVByte: 1);
        var (plan, error) = Spender.PlanSpend(ChainId.Btc, utxos, req);

        Assert.Null(error);
        Assert.NotNull(plan);
        Assert.False(plan!.NeedsChange);
        Assert.Equal(0, plan.ChangeSat);
        Assert.Equal(plan.InputSat - req.AmountSat, plan.FeeSat); // the sub-dust remainder went to miners

        var (tx, buildError) = Spender.BuildSigned(Phrase, plan, req, changeAddress: null);
        Assert.Null(buildError);
        Assert.NotNull(tx);
        Assert.Single(tx!.Outputs); // recipient only — no change output
    }

    [Fact]
    public void A_single_input_is_used_when_it_covers_the_spend()
    {
        var utxos = new[] { Utxo(0, 0, 200_000), Utxo(0, 1, 200_000) };
        var req = new UtxoSpendRequest(ChainId.Btc, Dest, 50_000, FeeRateSatPerVByte: 1);
        var (plan, error) = Spender.PlanSpend(ChainId.Btc, utxos, req);

        Assert.Null(error);
        Assert.Single(plan!.Inputs);
        Assert.True(plan.NeedsChange);
    }

    [Fact]
    public void Invalid_destination_address_is_rejected()
    {
        var utxos = new[] { Utxo(0, 0, 200_000) };
        var req = new UtxoSpendRequest(ChainId.Btc, "not-a-bitcoin-address", 50_000, FeeRateSatPerVByte: 1);
        var (plan, error) = Spender.PlanSpend(ChainId.Btc, utxos, req);

        Assert.Null(plan);
        Assert.Contains("destination", error);
    }

    [Fact]
    public void A_litecoin_address_is_rejected_on_the_bitcoin_chain()
    {
        // Wrong-network guard: an LTC bech32 must not be accepted as a BTC destination.
        var ltc = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Ltc, 0, 0).Address;
        var utxos = new[] { Utxo(0, 0, 200_000) };
        var req = new UtxoSpendRequest(ChainId.Btc, ltc, 50_000, FeeRateSatPerVByte: 1);
        var (plan, error) = Spender.PlanSpend(ChainId.Btc, utxos, req);

        Assert.Null(plan);
        Assert.Contains("destination", error);
    }

    // ---- Dogecoin: the newly-enabled send path uses the same spender (legacy P2PKH). Proven offline. ----

    private static OwnedUtxo DogeUtxo(uint change, uint index, long sat, bool confirmed = true)
    {
        var acct = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Doge, change, index);
        return new OwnedUtxo(acct.Path, acct.Address, Guid.NewGuid().ToString("N")[..8].PadLeft(64, '0'), 0, sat, confirmed);
    }

    [Fact]
    public void Doge_spend_builds_signs_and_verifies_offline()
    {
        // A real Dogecoin spend, planned + signed with no funds and no network. Proves the same UTXO
        // spender handles DOGE end to end (Dogecoin.Instance.Mainnet, legacy P2PKH) and the signature
        // verifies — the part where a bug would lose coins. The live UTXO/fee/broadcast path (BlockCypher)
        // still needs a small real send to confirm, but a broken tx here would be caught, not lost.
        var dest = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Doge, 0, 5).Address; // a valid DOGE address
        var utxos = new[] { DogeUtxo(0, 0, 500_000_000), DogeUtxo(0, 1, 500_000_000) }; // 5 + 5 DOGE
        var req = new UtxoSpendRequest(ChainId.Doge, dest, 300_000_000, FeeRateSatPerVByte: 1000);
        var (plan, error) = Spender.PlanSpend(ChainId.Doge, utxos, req);

        Assert.Null(error);
        Assert.NotNull(plan);
        Assert.True(plan!.NeedsChange);

        var change = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Doge, 1, 0).Address;
        var (tx, buildError) = Spender.BuildSigned(Phrase, plan, req, change);
        Assert.Null(buildError);
        Assert.NotNull(tx);
        Assert.Equal(2, tx!.Outputs.Count); // recipient + change, both valid DOGE outputs
    }

    [Fact]
    public void Doge_rejects_a_bitcoin_destination_address()
    {
        // Wrong-network guard: a BTC bech32 must never be accepted as a DOGE destination.
        var utxos = new[] { DogeUtxo(0, 0, 500_000_000) };
        var req = new UtxoSpendRequest(ChainId.Doge, Dest, 100_000_000, FeeRateSatPerVByte: 1000);
        var (plan, error) = Spender.PlanSpend(ChainId.Doge, utxos, req);

        Assert.Null(plan);
        Assert.Contains("destination", error);
    }

    // ---- Bitcoin Cash: the BCH send path uses the SAME UTXO spender via BCash.Instance.Mainnet
    // (BIP44 legacy P2PKH, CashAddr) and NBitcoin's SIGHASH_FORKID replay protection. Proven offline
    // BEFORE send is ever enabled in the catalog — a broken FORKID signature would be caught here, not
    // lost on-chain. The live UTXO/fee/broadcast wiring is verified separately with a small real send. ----

    private static OwnedUtxo BchUtxo(uint change, uint index, long sat, bool confirmed = true)
    {
        var acct = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Bch, change, index);
        return new OwnedUtxo(acct.Path, acct.Address, Guid.NewGuid().ToString("N")[..8].PadLeft(64, '0'), 0, sat, confirmed);
    }

    [Fact]
    public void Bch_spend_builds_signs_and_verifies_offline()
    {
        // A real BCH spend, planned + signed with no funds and no network. The destination and change are
        // genuine CashAddr addresses; NBitcoin signs every input with SIGHASH_FORKID (the BCash network's
        // rule) and Verify() checks the signatures — the exact place a bug would burn coins.
        var dest = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Bch, 0, 5).Address; // a valid CashAddr
        var utxos = new[] { BchUtxo(0, 0, 50_000_000), BchUtxo(0, 1, 50_000_000) }; // 0.5 + 0.5 BCH
        var req = new UtxoSpendRequest(ChainId.Bch, dest, 30_000_000, FeeRateSatPerVByte: 2);
        var (plan, error) = Spender.PlanSpend(ChainId.Bch, utxos, req);

        Assert.Null(error);
        Assert.NotNull(plan);
        Assert.True(plan!.NeedsChange);

        var change = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Bch, 1, 0).Address;
        var (tx, buildError) = Spender.BuildSigned(Phrase, plan, req, change);
        Assert.Null(buildError);
        Assert.NotNull(tx);
        Assert.Equal(2, tx!.Outputs.Count); // recipient + change, both valid BCH outputs
    }

    [Fact]
    public void Bch_rejects_a_bitcoin_bech32_destination_address()
    {
        // Wrong-network guard: a BTC bech32 (segwit) must never be accepted as a BCH destination.
        var utxos = new[] { BchUtxo(0, 0, 50_000_000) };
        var req = new UtxoSpendRequest(ChainId.Bch, Dest, 10_000_000, FeeRateSatPerVByte: 2);
        var (plan, error) = Spender.PlanSpend(ChainId.Bch, utxos, req);

        Assert.Null(plan);
        Assert.Contains("destination", error);
    }

    // ---- Coin control (§3.4): the planner may spend ONLY the coins the user selected. This is the
    // privacy guarantee — a spend must never silently pull in a coin from another identity. The view
    // model filters the scan to the ticked coins by (TxId, Vout); these tests exercise that contract. ----

    [Fact]
    public void Coin_control_spends_only_the_selected_coins()
    {
        // Three confirmed coins on three different addresses — think three identities. The user picks ONE.
        var coinA = Utxo(0, 0, 200_000); // receive #0
        var coinB = Utxo(0, 1, 200_000); // receive #1  <- the only one selected
        var coinC = Utxo(1, 0, 200_000); // change  #0
        var wallet = new[] { coinA, coinB, coinC };

        // Exactly the filter the view model applies before planning.
        var picked = new HashSet<(string, int)> { (coinB.TxId, coinB.Vout) };
        var selected = wallet.Where(u => picked.Contains((u.TxId, u.Vout))).ToList();

        var req = new UtxoSpendRequest(ChainId.Btc, Dest, 50_000, FeeRateSatPerVByte: 1);
        var (plan, error) = Spender.PlanSpend(ChainId.Btc, selected, req);

        Assert.Null(error);
        Assert.NotNull(plan);
        Assert.All(plan!.Inputs, i => Assert.Equal(coinB.TxId, i.TxId));       // only the picked coin
        Assert.DoesNotContain(plan.Inputs, i => i.TxId == coinA.TxId || i.TxId == coinC.TxId);
    }

    [Fact]
    public void Coin_control_fails_closed_when_the_selection_cannot_cover_the_amount()
    {
        // The wallet could easily afford the spend, but the user selected only a small coin. Coin control
        // must NOT reach past the selection to make up the difference — it reports insufficient funds.
        var small = Utxo(0, 0, 40_000);    // the only selected coin
        var big = Utxo(0, 1, 1_000_000);   // deliberately NOT selected
        var wallet = new[] { small, big };

        var picked = new HashSet<(string, int)> { (small.TxId, small.Vout) };
        var selected = wallet.Where(u => picked.Contains((u.TxId, u.Vout))).ToList();

        var req = new UtxoSpendRequest(ChainId.Btc, Dest, 100_000, FeeRateSatPerVByte: 1);
        var (plan, error) = Spender.PlanSpend(ChainId.Btc, selected, req);

        Assert.Null(plan);
        Assert.Contains("Insufficient", error);
    }
}
