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
}
