using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Payjoin;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// What the sender offers a PayJoin receiver, what it asks for on the wire, and — the part a user can
/// be hurt by — what the wallet says happened when the exchange fails (roadmap P2.2).
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class PayjoinPlanningTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string Dest = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4";

    private static readonly HdAddressDeriver Deriver = new();
    private static readonly HdUtxoSpender Spender = new(Deriver);

    private static (Transaction Tx, long Fee, Script? Change) Original(long inputSat, long amountSat)
    {
        var acct = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 0, 0);
        var utxos = new[] { new OwnedUtxo(acct.Path, acct.Address, new string('a', 64), 0, inputSat, true) };
        var request = new UtxoSpendRequest(ChainId.Btc, Dest, amountSat, FeeRateSatPerVByte: 5);
        var (plan, error) = Spender.PlanSpend(ChainId.Btc, utxos, request);
        Assert.Null(error);

        var change = plan!.NeedsChange ? Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 1, 0).Address : null;
        var (psbt, tx, buildError) = Spender.BuildOriginalPsbt(Phrase, plan, request, change);
        Assert.Null(buildError);

        return (tx!, psbt!.GetFee().Satoshi,
            change is null ? null : BitcoinAddress.Create(change, Network.Main).ScriptPubKey);
    }

    [Fact]
    public void The_offer_pays_for_one_added_input_at_the_payments_own_rate()
    {
        var (tx, fee, change) = Original(500_000, 100_000);
        var p = PayjoinPlanner.ParametersFor(tx, fee, change, senderInputVirtualSize: 68, dustSat: 546);

        var rate = (decimal)fee / tx.GetVirtualSize();
        Assert.Equal((long)Math.Ceiling(rate * 68), p.MaxFeeContributionSat);
        Assert.Equal(tx.Outputs.ToList().FindIndex(o => o.ScriptPubKey == change), p.FeeOutputIndex);
        Assert.Equal(Math.Floor(rate), p.MinFeeRateSatPerVByte);
        Assert.True(p.DisableOutputSubstitution);
    }

    [Fact]
    public void Without_change_nothing_is_offered()
    {
        // No change output means no money of the sender's the receiver could take from. The receiver
        // pays for its own input out of the payment, or declines.
        var (tx, fee, _) = Original(500_000, 100_000);
        var p = PayjoinPlanner.ParametersFor(tx, fee, changeScript: null, senderInputVirtualSize: 68, dustSat: 546);

        Assert.Null(p.FeeOutputIndex);
        Assert.Equal(0, p.MaxFeeContributionSat);
    }

    [Fact]
    public void The_offer_never_takes_change_down_to_dust()
    {
        var (tx, fee, change) = Original(500_000, 100_000);
        var changeValue = tx.Outputs.First(o => o.ScriptPubKey == change).Value.Satoshi;

        // A dust limit just under the change leaves almost nothing to offer; the change must stay above it.
        var p = PayjoinPlanner.ParametersFor(tx, fee, change, senderInputVirtualSize: 68, dustSat: changeValue - 10);

        Assert.True(changeValue - p.MaxFeeContributionSat > changeValue - 10);
    }

    [Fact]
    public void The_request_refuses_substitution_and_states_its_limits()
    {
        var p = new PayjoinParameters(FeeOutputIndex: 1, MaxFeeContributionSat: 340, MinFeeRateSatPerVByte: 5m);
        var uri = PayjoinClient.BuildRequestUri(new Uri("https://pay.example.com/BTC/pj"), p);

        Assert.Equal("https", uri.Scheme);
        Assert.Contains("v=1", uri.Query);
        Assert.Contains("disableoutputsubstitution=true", uri.Query);
        Assert.Contains("additionalfeeoutputindex=1", uri.Query);
        Assert.Contains("maxadditionalfeecontribution=340", uri.Query);
        Assert.Contains("minfeerate=5", uri.Query);
    }

    [Fact]
    public void An_endpoint_with_its_own_query_keeps_it()
    {
        var p = new PayjoinParameters(null, 0, 3m);
        var uri = PayjoinClient.BuildRequestUri(new Uri("https://pay.example.com/pj?store=42"), p);

        Assert.StartsWith("?store=42&v=1", uri.Query);
        Assert.DoesNotContain("additionalfeeoutputindex", uri.Query);   // nothing offered, nothing named
    }

    [Fact]
    public async Task When_the_exchange_fails_the_wallet_says_the_payment_already_left_the_device()
    {
        // The receiver was handed a signed payment the moment the request went out. If everything after
        // that fails — here, nothing is reachable at all — the user must NOT be offered a retry as if
        // nothing happened: the receiver can still broadcast what it holds, and a second send pays twice.
        var proxyBefore = PublicHttp.ActiveProxy;
        PublicHttp.SetProxy("socks5://127.0.0.1:1");
        var storePath = Path.Combine(Path.GetTempPath(), $"umbrella-pj-{Guid.NewGuid():N}.json");
        try
        {
            var acct = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 0, 0);
            var utxos = new[] { new OwnedUtxo(acct.Path, acct.Address, new string('a', 64), 0, 500_000, true) };
            var request = new UtxoSpendRequest(ChainId.Btc, Dest, 100_000, FeeRateSatPerVByte: 5);
            var (plan, _) = Spender.PlanSpend(ChainId.Btc, utxos, request);

            var outcome = await new BitcoinTransactionSender(Deriver).SignAndBroadcastPayjoinAsync(
                Phrase, "w1", new AddressIndexStore(storePath), "BTC", plan!, request,
                new Uri("https://pay.example.invalid/pj"));

            Assert.False(outcome.UsedPayjoin);
            Assert.False(outcome.Ok);                    // and the fallback broadcast could not get out either
            Assert.True(outcome.OriginalLeftDevice);
            Assert.NotNull(outcome.PayjoinFailure);
        }
        finally
        {
            PublicHttp.SetProxy(proxyBefore);
            try { File.Delete(storePath); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task A_payment_the_receiver_never_saw_is_reported_as_never_having_left()
    {
        // Litecoin: PayJoin is not attempted at all, so nothing was handed to anyone and a failed send
        // really is safe to retry.
        var proxyBefore = PublicHttp.ActiveProxy;
        PublicHttp.SetProxy("socks5://127.0.0.1:1");
        var storePath = Path.Combine(Path.GetTempPath(), $"umbrella-pj-{Guid.NewGuid():N}.json");
        try
        {
            var acct = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Ltc, 0, 0);
            var utxos = new[] { new OwnedUtxo(acct.Path, acct.Address, new string('a', 64), 0, 5_000_000, true) };
            var to = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Ltc, 0, 5).Address;
            var request = new UtxoSpendRequest(ChainId.Ltc, to, 1_000_000, FeeRateSatPerVByte: 5);
            var (plan, _) = Spender.PlanSpend(ChainId.Ltc, utxos, request);

            Assert.False(BitcoinTransactionSender.CanAttemptPayjoin("LTC", plan!));

            var outcome = await new BitcoinTransactionSender(Deriver).SignAndBroadcastPayjoinAsync(
                Phrase, "w1", new AddressIndexStore(storePath), "LTC", plan!, request,
                new Uri("https://pay.example.invalid/pj"));

            Assert.False(outcome.OriginalLeftDevice);
            Assert.False(outcome.UsedPayjoin);
        }
        finally
        {
            PublicHttp.SetProxy(proxyBefore);
            try { File.Delete(storePath); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PayJoin_is_not_attempted_for_a_payment_that_mixes_address_kinds()
    {
        var segwit = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 0, 0);
        var taproot = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Btc, 0, 0, kind: UtxoScriptKind.Taproot);
        var utxos = new[]
        {
            new OwnedUtxo(segwit.Path, segwit.Address, new string('a', 64), 0, 60_000, true),
            new OwnedUtxo(taproot.Path, taproot.Address, new string('b', 64), 0, 60_000, true),
        };
        var request = new UtxoSpendRequest(ChainId.Btc, Dest, 100_000, FeeRateSatPerVByte: 5);
        var (plan, _) = Spender.PlanSpend(ChainId.Btc, utxos, request);

        Assert.Equal(2, plan!.Inputs.Count);
        Assert.False(BitcoinTransactionSender.CanAttemptPayjoin("BTC", plan));
    }
}
