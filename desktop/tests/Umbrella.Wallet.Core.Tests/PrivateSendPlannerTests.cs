using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// "Send this privately" as one switch instead of a checklist nobody remembers.
///
/// The tests that matter most are the ones about what the plan REFUSES to promise. Turning on Tor
/// hides an IP from a block explorer; it does not make Bitcoin private. If the wallet let somebody
/// believe otherwise they might send something they should not have, so every plan carries the limits
/// alongside the steps — and a transparent chain always says so.
/// </summary>
public sealed class PrivateSendPlannerTests
{
    private static PrivateSendContext Bitcoin(
        bool tor = false, bool bootstrapped = false, bool killSwitch = false,
        int inputs = 1, bool recipientReused = false) =>
        new(tor, bootstrapped, killSwitch,
            ChainHidesAmounts: false, IsUtxoChain: true, inputs, recipientReused);

    private static PrivateSendContext Monero(
        bool tor = true, bool bootstrapped = true, bool killSwitch = true) =>
        new(tor, bootstrapped, killSwitch,
            ChainHidesAmounts: true, IsUtxoChain: false, InputAddressCount: 0,
            RecipientAddressReused: false);

    [Fact]
    public void From_a_cold_start_it_turns_tor_on_first_and_waits_for_it()
    {
        // Order matters: if the fee lookup has already gone out over clearnet, switching Tor on
        // afterwards protects nothing.
        var plan = PrivateSendPlanner.Plan(Bitcoin());

        Assert.Equal(PrivateSendStep.EnableTor, plan.Steps[0]);
        Assert.Equal(PrivateSendStep.WaitForTor, plan.Steps[1]);
    }

    [Fact]
    public void Tor_already_on_but_still_bootstrapping_only_waits()
    {
        var plan = PrivateSendPlanner.Plan(Bitcoin(tor: true, bootstrapped: false));

        Assert.DoesNotContain(PrivateSendStep.EnableTor, plan.Steps);
        Assert.Contains(PrivateSendStep.WaitForTor, plan.Steps);
    }

    [Fact]
    public void Tor_already_up_is_not_asked_for_again()
    {
        var plan = PrivateSendPlanner.Plan(Bitcoin(tor: true, bootstrapped: true));

        Assert.DoesNotContain(PrivateSendStep.EnableTor, plan.Steps);
        Assert.DoesNotContain(PrivateSendStep.WaitForTor, plan.Steps);
    }

    [Fact]
    public void The_kill_switch_is_included_so_a_tor_failure_cannot_go_direct()
    {
        var plan = PrivateSendPlanner.Plan(Bitcoin(tor: true, bootstrapped: true));
        Assert.Contains(PrivateSendStep.EnableKillSwitch, plan.Steps);

        var already = PrivateSendPlanner.Plan(Bitcoin(tor: true, bootstrapped: true, killSwitch: true));
        Assert.DoesNotContain(PrivateSendStep.EnableKillSwitch, already.Steps);
    }

    [Fact]
    public void A_spend_pulling_in_many_addresses_is_narrowed()
    {
        var many = PrivateSendPlanner.Plan(Bitcoin(tor: true, bootstrapped: true, killSwitch: true, inputs: 6));
        Assert.Contains(PrivateSendStep.NarrowInputs, many.Steps);

        var few = PrivateSendPlanner.Plan(Bitcoin(tor: true, bootstrapped: true, killSwitch: true, inputs: 1));
        Assert.DoesNotContain(PrivateSendStep.NarrowInputs, few.Steps);
    }

    [Fact]
    public void Change_always_goes_to_a_fresh_address_on_utxo_chains()
    {
        var plan = PrivateSendPlanner.Plan(Bitcoin(tor: true, bootstrapped: true, killSwitch: true));
        Assert.Contains(PrivateSendStep.FreshChangeAddress, plan.Steps);
    }

    [Fact]
    public void Account_chains_get_no_change_or_input_steps()
    {
        // Monero and the account-model chains have no UTXO set to narrow and no change address.
        var plan = PrivateSendPlanner.Plan(Monero());

        Assert.DoesNotContain(PrivateSendStep.NarrowInputs, plan.Steps);
        Assert.DoesNotContain(PrivateSendStep.FreshChangeAddress, plan.Steps);
    }

    // --- what it refuses to promise ---------------------------------------------------------------

    [Fact]
    public void A_transparent_chain_always_says_the_ledger_is_public_forever()
    {
        // Even with Tor on, the kill-switch on and one input, Bitcoin is still a public ledger. This
        // is the line that stops the feature being a lie.
        var plan = PrivateSendPlanner.Plan(Bitcoin(tor: true, bootstrapped: true, killSwitch: true));

        Assert.Contains(PrivateSendLimit.LedgerIsPublicForever, plan.Limits);
    }

    [Fact]
    public void Monero_is_not_told_its_ledger_is_public()
    {
        var plan = PrivateSendPlanner.Plan(Monero());
        Assert.DoesNotContain(PrivateSendLimit.LedgerIsPublicForever, plan.Limits);
    }

    [Fact]
    public void Joining_several_addresses_is_named_as_something_no_switch_undoes()
    {
        var plan = PrivateSendPlanner.Plan(Bitcoin(tor: true, bootstrapped: true, killSwitch: true, inputs: 4));
        Assert.Contains(PrivateSendLimit.InputsLinkAddresses, plan.Limits);
    }

    [Fact]
    public void A_reused_recipient_address_is_named()
    {
        var plan = PrivateSendPlanner.Plan(
            Bitcoin(tor: true, bootstrapped: true, killSwitch: true, recipientReused: true));

        Assert.Contains(PrivateSendLimit.RecipientAddressIsReused, plan.Limits);
    }

    [Fact]
    public void Every_plan_admits_the_recipient_still_knows_who_paid()
    {
        // True on Monero too. No amount of network privacy hides you from the person you are paying,
        // and that is the misunderstanding most likely to hurt someone.
        foreach (var plan in new[]
                 {
                     PrivateSendPlanner.Plan(Bitcoin()),
                     PrivateSendPlanner.Plan(Bitcoin(tor: true, bootstrapped: true, killSwitch: true)),
                     PrivateSendPlanner.Plan(Monero()),
                 })
        {
            Assert.Contains(PrivateSendLimit.RecipientStillLearnsWhoPaid, plan.Limits);
        }
    }

    [Fact]
    public void A_fully_configured_monero_send_has_nothing_left_to_switch_on()
    {
        var plan = PrivateSendPlanner.Plan(Monero());

        Assert.True(plan.AlreadyAsPrivateAsPossible);
        Assert.Empty(plan.Steps);
        // …but it still carries its limits, so "nothing to do" never reads as "nothing to know".
        Assert.NotEmpty(plan.Limits);
    }

    [Fact]
    public void A_bitcoin_send_is_never_reported_as_needing_nothing()
    {
        // Even at best it has the fresh-change step, so the plan never claims a UTXO send is finished
        // being made private.
        var plan = PrivateSendPlanner.Plan(Bitcoin(tor: true, bootstrapped: true, killSwitch: true));
        Assert.False(plan.AlreadyAsPrivateAsPossible);
    }

    [Fact]
    public void No_step_is_ever_listed_twice()
    {
        var plan = PrivateSendPlanner.Plan(Bitcoin(inputs: 9));
        Assert.Equal(plan.Steps.Distinct().Count(), plan.Steps.Count);
        Assert.Equal(plan.Limits.Distinct().Count(), plan.Limits.Count);
    }
}
