using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap P1.12 — the report that says what a send actually cost, and P1.11's rule that a privacy
/// statement never appears without its limits.
///
/// The failure this guards against is a report that flatters. "Tor: on" is true and, on its own,
/// misleading: the spend can still have tied five of the user's addresses together forever, in
/// public, because that is what spending from five addresses does. A report that listed only the
/// green lines would be worse than none — it would be a wallet telling somebody they were careful.
/// </summary>
public sealed class SendLeakReportTests
{
    private static SendLeakSignals Signals(
        bool tor = false, bool killSwitch = false, int inputs = 1,
        bool hidesAmounts = false, bool freshChange = true, bool coinControl = false) =>
        new(tor, killSwitch, inputs, hidesAmounts, freshChange, coinControl);

    private static bool Has(IReadOnlyList<SendLeakFinding> f, string code) => f.Any(x => x.Code == code);

    [Fact]
    public void A_clearnet_send_says_the_node_saw_the_ip()
    {
        var report = SendLeakReport.Build(Signals(tor: false));

        Assert.True(Has(report, "ipSeen"));
        Assert.False(Has(report, "ipHidden"));
        Assert.True(SendLeakReport.HasAvoidableExposure(report));
    }

    [Fact]
    public void A_tor_send_says_the_ip_was_hidden_and_still_lists_what_was_not()
    {
        var report = SendLeakReport.Build(Signals(tor: true, killSwitch: true));

        Assert.True(Has(report, "ipHidden"));

        // The limits ship with it, always: a public ledger and a recipient who knows who paid them.
        Assert.True(Has(report, "ledgerPublic"));
        Assert.True(Has(report, "recipientKnows"));
        Assert.Contains(report, f => !f.IsProtected);
    }

    /// <summary>
    /// Tor without the kill-switch is a real gap, not a detail: the circuit can drop between the
    /// quote and the broadcast, and without the switch the request goes out in the clear anyway.
    /// </summary>
    [Fact]
    public void Tor_without_the_kill_switch_is_reported()
    {
        Assert.True(Has(SendLeakReport.Build(Signals(tor: true, killSwitch: false)), "noKillSwitch"));
        Assert.False(Has(SendLeakReport.Build(Signals(tor: true, killSwitch: true)), "noKillSwitch"));
    }

    [Fact]
    public void Spending_from_several_addresses_reports_the_linkage_and_the_count()
    {
        var report = SendLeakReport.Build(Signals(inputs: 4));

        var linked = Assert.Single(report, f => f.Code == "inputsLinked");
        Assert.False(linked.IsProtected);
        Assert.Equal(4, linked.Count);
    }

    [Fact]
    public void Spending_from_one_address_is_reported_as_the_better_case()
    {
        var report = SendLeakReport.Build(Signals(inputs: 1));

        Assert.True(Has(report, "inputsSingle"));
        Assert.False(Has(report, "inputsLinked"));
    }

    [Fact]
    public void Change_returning_to_a_used_address_is_an_exposure()
    {
        Assert.True(Has(SendLeakReport.Build(Signals(freshChange: false)), "changeReused"));
        Assert.True(SendLeakReport.HasAvoidableExposure(SendLeakReport.Build(Signals(freshChange: false))));
    }

    /// <summary>
    /// On Monero there is no linkage to report and no public amount — but the report is NOT empty of
    /// limits, because "nothing to turn on" must never read as "nothing to know" (MANIFESTO §2).
    /// </summary>
    [Fact]
    public void A_private_chain_reports_its_protection_and_still_names_a_limit()
    {
        var report = SendLeakReport.Build(Signals(tor: true, killSwitch: true, hidesAmounts: true));

        Assert.True(Has(report, "ledgerPrivate"));
        Assert.False(Has(report, "inputsLinked"));
        Assert.False(Has(report, "ledgerPublic"));

        // The recipient still knows who paid them. That one has no switch anywhere.
        Assert.True(Has(report, "recipientKnows"));
        Assert.Contains(report, f => !f.IsProtected);
    }

    [Fact]
    public void Choosing_the_coins_by_hand_is_credited()
    {
        Assert.True(Has(SendLeakReport.Build(Signals(coinControl: true)), "coinControl"));
        Assert.False(Has(SendLeakReport.Build(Signals(coinControl: false)), "coinControl"));
    }

    /// <summary>
    /// The best possible transparent-chain send still has nothing "avoidable" left — the remaining
    /// exposures are the chain's, not the user's. Without this the warning banner would be permanent,
    /// and a warning that is always on is one nobody reads.
    /// </summary>
    [Fact]
    public void A_careful_send_has_no_avoidable_exposure_left()
    {
        var report = SendLeakReport.Build(
            Signals(tor: true, killSwitch: true, inputs: 1, freshChange: true, coinControl: true));

        Assert.False(SendLeakReport.HasAvoidableExposure(report));
        Assert.Contains(report, f => !f.IsProtected);   // but the ledger and the recipient remain
    }

    /// <summary>
    /// P1.11: every finding the Security Center shows carries a limit code, so no status can render
    /// as a bare reassurance. A new finding added without one fails here rather than shipping.
    /// </summary>
    [Fact]
    public void Every_privacy_score_finding_states_a_limit()
    {
        foreach (var tor in new[] { true, false })
        foreach (var kill in new[] { true, false })
        foreach (var leak in new[] { true, false })
        {
            var score = PrivacyScoreInspector.Evaluate(new PrivacySignals(tor, kill, leak));
            Assert.All(score.Findings, f => Assert.False(string.IsNullOrWhiteSpace(f.LimitCode)));
        }
    }
}
