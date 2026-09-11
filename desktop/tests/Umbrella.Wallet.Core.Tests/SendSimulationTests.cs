using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The send review answers "what happens to my money if I press this". Every case here is a real
/// surprise people hit: the fee landing on top of the amount rather than coming out of it, a balance
/// afterwards that is not what was assumed, and the moment of panic on Bitcoin-family chains when most
/// of the balance appears to move somewhere unfamiliar — which is change returning to the user's own
/// address.
///
/// The arithmetic is the whole feature. If it is wrong, the wallet is confidently lying at exactly the
/// moment somebody is deciding whether to part with their money.
/// </summary>
public sealed class SendSimulationTests
{
    private static decimal Of(SendSimulation.Result r, SendEffectKind kind) =>
        r.Effects.Single(e => e.Kind == kind).Amount;

    [Fact]
    public void The_fee_is_charged_on_top_of_the_amount_not_taken_out_of_it()
    {
        // The single most common misunderstanding: people expect the recipient to get amount-minus-fee.
        var r = SendSimulation.Build(balance: 1.0m, amount: 0.5m, networkFee: 0.01m);

        Assert.Equal(0.5m, Of(r, SendEffectKind.Recipient));
        Assert.Equal(0.01m, Of(r, SendEffectKind.NetworkFee));
        Assert.Equal(0.51m, r.TotalLeaving);
        Assert.Equal(0.49m, r.BalanceAfter);
    }

    [Fact]
    public void Change_is_shown_as_returning_to_the_user()
    {
        // On a UTXO chain the whole input is consumed and the remainder comes back. Without this line
        // the block explorer looks like it sent most of the balance to a stranger.
        var r = SendSimulation.Build(
            balance: 1.0m, amount: 0.2m, networkFee: 0.0001m, changeReturned: 0.7999m);

        Assert.Equal(0.7999m, Of(r, SendEffectKind.ChangeReturned));
    }

    [Fact]
    public void Account_chains_get_no_change_line_at_all()
    {
        // Ethereum has no change. "Change: 0" would be noise that teaches the reader nothing.
        var r = SendSimulation.Build(balance: 2m, amount: 1m, networkFee: 0.002m);
        Assert.DoesNotContain(r.Effects, e => e.Kind == SendEffectKind.ChangeReturned);
    }

    [Fact]
    public void Emptying_the_wallet_is_called_out()
    {
        var r = SendSimulation.Build(balance: 1.0m, amount: 0.99m, networkFee: 0.01m);

        Assert.Equal(0m, r.BalanceAfter);
        Assert.Contains(r.Warnings, w => w.Kind == SendWarningKind.EmptiesBalance);
    }

    [Fact]
    public void A_fee_that_eats_a_large_share_of_a_small_send_is_called_out()
    {
        // Sending the equivalent of a few dollars with a fee nearly as large is a real and avoidable
        // loss; the user usually just has not looked at the fee line.
        var r = SendSimulation.Build(balance: 1m, amount: 0.001m, networkFee: 0.0005m);

        var warning = r.Warnings.Single(w => w.Kind == SendWarningKind.FeeIsLargeShareOfAmount);
        Assert.Equal(0.5m, warning.Value);
    }

    [Fact]
    public void A_normal_fee_is_not_called_out()
    {
        var r = SendSimulation.Build(balance: 10m, amount: 1m, networkFee: 0.0001m);
        Assert.DoesNotContain(r.Warnings, w => w.Kind == SendWarningKind.FeeIsLargeShareOfAmount);
    }

    [Fact]
    public void Dust_change_is_called_out_only_when_a_threshold_is_known()
    {
        var withThreshold = SendSimulation.Build(
            balance: 1m, amount: 0.5m, networkFee: 0.0001m,
            changeReturned: 0.0000005m, dustThreshold: 0.00001m);
        Assert.Contains(withThreshold.Warnings, w => w.Kind == SendWarningKind.ChangeIsDust);

        // A chain with no dust concept must not produce a bogus warning.
        var noThreshold = SendSimulation.Build(
            balance: 1m, amount: 0.5m, networkFee: 0.0001m, changeReturned: 0.0000005m);
        Assert.DoesNotContain(noThreshold.Warnings, w => w.Kind == SendWarningKind.ChangeIsDust);
    }

    [Fact]
    public void A_send_larger_than_the_balance_is_flagged_and_never_shows_a_negative_balance()
    {
        // Showing "-0.02 BTC" would look like a bug and tell the user nothing useful.
        var r = SendSimulation.Build(balance: 0.5m, amount: 0.5m, networkFee: 0.02m);

        var warning = r.Warnings.Single(w => w.Kind == SendWarningKind.ExceedsBalance);
        Assert.Equal(0.02m, warning.Value);
        Assert.Equal(0m, r.BalanceAfter);
        Assert.Equal(0m, Of(r, SendEffectKind.BalanceAfter));
    }

    [Fact]
    public void Exceeding_the_balance_and_emptying_it_are_never_reported_together()
    {
        // They are contradictory. Reporting both would leave the reader unsure which is true.
        var r = SendSimulation.Build(balance: 0.5m, amount: 0.5m, networkFee: 0.02m);

        Assert.Contains(r.Warnings, w => w.Kind == SendWarningKind.ExceedsBalance);
        Assert.DoesNotContain(r.Warnings, w => w.Kind == SendWarningKind.EmptiesBalance);
    }

    [Fact]
    public void Negative_inputs_cannot_produce_a_nonsense_simulation()
    {
        var r = SendSimulation.Build(balance: 1m, amount: -5m, networkFee: -1m, changeReturned: -2m);

        Assert.Equal(0m, Of(r, SendEffectKind.Recipient));
        Assert.Equal(0m, Of(r, SendEffectKind.NetworkFee));
        Assert.Equal(0m, r.TotalLeaving);
        Assert.Equal(1m, r.BalanceAfter);
        Assert.DoesNotContain(r.Effects, e => e.Kind == SendEffectKind.ChangeReturned);
    }

    [Fact]
    public void Full_precision_is_preserved_for_satoshi_level_amounts()
    {
        // decimal, not double: 0.1 + 0.2 must be exactly 0.3 on a screen someone is trusting.
        var r = SendSimulation.Build(balance: 1m, amount: 0.1m, networkFee: 0.2m);
        Assert.Equal(0.3m, r.TotalLeaving);
        Assert.Equal(0.7m, r.BalanceAfter);

        var tiny = SendSimulation.Build(balance: 0.00000002m, amount: 0.00000001m, networkFee: 0.00000001m);
        Assert.Equal(0.00000002m, tiny.TotalLeaving);
        Assert.Equal(0m, tiny.BalanceAfter);
    }

    [Fact]
    public void The_effects_are_ordered_so_the_total_follows_what_makes_it_up()
    {
        var r = SendSimulation.Build(
            balance: 1m, amount: 0.5m, networkFee: 0.01m, changeReturned: 0.48m);

        var order = r.Effects.Select(e => e.Kind).ToList();
        Assert.Equal(
            [SendEffectKind.Recipient, SendEffectKind.NetworkFee, SendEffectKind.TotalLeaving,
             SendEffectKind.ChangeReturned, SendEffectKind.BalanceAfter],
            order);
    }
}
