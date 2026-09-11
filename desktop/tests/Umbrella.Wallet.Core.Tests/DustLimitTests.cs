using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Bitcoin's 546-satoshi dust limit is not a universal constant, and using it for Dogecoin was wrong
/// by more than two orders of magnitude.
///
/// Dogecoin Core enforces a HARD dust limit of 0.001 DOGE — an output below it makes the transaction
/// non-standard and it is rejected outright — and a SOFT limit of 0.01 DOGE, below which every such
/// output demands an extra 0.01 DOGE of fee or the transaction is rejected for underpaying. 546 koinu
/// is 0.00000546 DOGE, under both.
///
/// The consequence was a Dogecoin send that planned, signed and broadcast, and was then refused by the
/// network — either because the amount was dust, or, more insidiously, because the CHANGE output was,
/// which the user never chose and could not see.
/// </summary>
public sealed class DustLimitTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    /// <summary>A real Dogecoin P2PKH address, so the destination check is not what fails here.</summary>
    private const string DogeDest = "DH5yaieqoZN36fDVciNyRueRGvGLR3mr7L";

    private const long Koinu = 100_000_000;          // 1 DOGE
    private const long SoftDust = 1_000_000;         // 0.01 DOGE
    private const long HardDust = 100_000;           // 0.001 DOGE

    private static readonly HdAddressDeriver Deriver = new();
    private static readonly HdUtxoSpender Spender = new(Deriver);

    private static OwnedUtxo DogeUtxo(long sat, uint index = 0)
    {
        var acct = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Doge, 0, index);
        return new OwnedUtxo(acct.Path, acct.Address, new string('a', 64), 0, sat, Confirmed: true);
    }

    [Fact]
    public void Dogecoins_dust_limit_is_far_above_bitcoins()
    {
        Assert.Equal(546, HdUtxoSpender.DustSatFor(ChainId.Btc));
        Assert.Equal(546, HdUtxoSpender.DustSatFor(ChainId.Ltc));
        Assert.Equal(546, HdUtxoSpender.DustSatFor(ChainId.Bch));

        // At the soft limit, so no output this wallet creates can trigger Dogecoin's extra-fee rule —
        // and comfortably clear of the hard limit that makes a transaction non-standard.
        Assert.Equal(SoftDust, HdUtxoSpender.DustSatFor(ChainId.Doge));
        Assert.True(HdUtxoSpender.DustSatFor(ChainId.Doge) >= HardDust);
    }

    [Fact]
    public void A_dogecoin_amount_bitcoin_would_allow_is_refused()
    {
        // 5,000 koinu is comfortably above Bitcoin's 546 and comfortably below anything Dogecoin will
        // relay. Before this, the wallet accepted it and the network did not.
        var (plan, error) = Spender.PlanSpend(
            ChainId.Doge,
            [DogeUtxo(50 * Koinu)],
            new UtxoSpendRequest(ChainId.Doge, DogeDest, 5_000, FeeRateSatPerVByte: 1));

        Assert.Null(plan);
        Assert.Contains("dust", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_same_amount_is_still_fine_on_bitcoin()
    {
        // Proving the refusal above is about Dogecoin's rules, not a blanket tightening that would
        // stop people making small Bitcoin payments.
        Assert.True(5_000 > HdUtxoSpender.DustSatFor(ChainId.Btc));
    }

    [Fact]
    public void Dogecoin_change_below_the_limit_is_rolled_into_the_fee_not_left_as_an_output()
    {
        // The insidious half. The user picks the amount; the wallet picks the change. A change output
        // of a few thousand koinu would have been created silently and the whole transaction refused.
        var input = 10 * Koinu;
        var amount = input - 400_000;   // leaves ~0.004 DOGE before fee: under the soft limit

        var (plan, error) = Spender.PlanSpend(
            ChainId.Doge,
            [DogeUtxo(input)],
            new UtxoSpendRequest(ChainId.Doge, DogeDest, amount, FeeRateSatPerVByte: 1));

        Assert.Null(error);
        Assert.NotNull(plan);
        Assert.Equal(0, plan!.ChangeSat);

        // Nothing vanished: what would have been dust became fee, and the arithmetic still balances.
        Assert.Equal(plan.InputSat, plan.AmountSat + plan.DevFeeSat + plan.FeeSat + plan.ChangeSat);
    }

    [Fact]
    public void Dogecoin_change_above_the_limit_is_still_returned()
    {
        // The rule must not become "Dogecoin never gets change back" — that would quietly donate real
        // money to miners on every spend.
        var (plan, error) = Spender.PlanSpend(
            ChainId.Doge,
            [DogeUtxo(100 * Koinu)],
            new UtxoSpendRequest(ChainId.Doge, DogeDest, 10 * Koinu, FeeRateSatPerVByte: 1));

        Assert.Null(error);
        Assert.True(plan!.ChangeSat > HdUtxoSpender.DustSatFor(ChainId.Doge));
        Assert.Equal(plan.InputSat, plan.AmountSat + plan.DevFeeSat + plan.FeeSat + plan.ChangeSat);
    }

    [Fact]
    public void Bitcoin_change_rules_are_unchanged()
    {
        // The per-chain table must not have moved Bitcoin. A 546 threshold there is correct and any
        // drift would change what every existing BTC send does.
        var acct = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 0, 0);
        var utxo = new OwnedUtxo(acct.Path, acct.Address, new string('b', 64), 0, 200_000, Confirmed: true);

        var (plan, error) = Spender.PlanSpend(
            ChainId.Btc,
            [utxo],
            new UtxoSpendRequest(ChainId.Btc, "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4", 100_000,
                FeeRateSatPerVByte: 1));

        Assert.Null(error);
        Assert.NotNull(plan);
        Assert.Equal(plan!.InputSat, plan.AmountSat + plan.DevFeeSat + plan.FeeSat + plan.ChangeSat);
    }
}
