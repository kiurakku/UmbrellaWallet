using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the wallet-wide privacy grade. The contract that matters: Tor is the heaviest lever, a "Strong"
/// grade is only earned with every privacy lever on, and the named "biggest win" is always the
/// highest-impact weakness still open.
/// </summary>
public sealed class PrivacyScoreInspectorTests
{
    private static PrivacyScore Eval(bool tor, bool kill, bool leak) =>
        PrivacyScoreInspector.Evaluate(new PrivacySignals(tor, kill, leak));

    [Fact]
    public void Every_lever_on_is_a_perfect_strong_score_with_nothing_left_to_fix()
    {
        var r = Eval(tor: true, kill: true, leak: false);
        Assert.Equal(100, r.Value);
        Assert.Equal(PrivacyGrade.Strong, r.Grade);
        Assert.Null(r.TopFixCode);
    }

    [Fact]
    public void Everything_off_is_exposed_and_names_tor_as_the_biggest_win()
    {
        var r = Eval(tor: false, kill: false, leak: true);
        Assert.Equal(0, r.Value);
        Assert.Equal(PrivacyGrade.Exposed, r.Grade);
        Assert.Equal("torOff", r.TopFixCode);
    }

    [Fact]
    public void Tor_is_the_heaviest_lever_and_the_market_leak_is_the_next_win()
    {
        var off = Eval(tor: false, kill: false, leak: true);
        var withTor = Eval(tor: true, kill: false, leak: true);
        Assert.Equal(0, off.Value);
        Assert.Equal(50, withTor.Value);                 // Tor alone is worth half the whole score
        Assert.Equal(PrivacyGrade.Moderate, withTor.Grade);
        Assert.Equal("marketOn", withTor.TopFixCode);    // after Tor, the active leak is the next win
    }

    [Fact]
    public void The_kill_switch_is_the_last_win_when_only_it_is_missing()
    {
        var r = Eval(tor: true, kill: false, leak: false);
        Assert.Equal(75, r.Value);
        Assert.Equal("killOff", r.TopFixCode);
    }

    [Theory]
    [InlineData(true, true, false, PrivacyGrade.Strong)]    // 100
    [InlineData(true, true, true, PrivacyGrade.Moderate)]   // 75
    [InlineData(false, true, false, PrivacyGrade.Moderate)] // 50
    [InlineData(false, false, false, PrivacyGrade.Exposed)] // 25
    [InlineData(false, false, true, PrivacyGrade.Exposed)]  // 0
    public void Grades_follow_the_bands(bool tor, bool kill, bool leak, PrivacyGrade expected) =>
        Assert.Equal(expected, Eval(tor, kill, leak).Grade);

    [Fact]
    public void Findings_carry_one_polarised_entry_per_signal()
    {
        var r = Eval(tor: true, kill: false, leak: true);
        Assert.Equal(3, r.Findings.Count);
        Assert.Contains(r.Findings, f => f.Code == "torOn" && f.IsStrength);
        Assert.Contains(r.Findings, f => f.Code == "killOff" && !f.IsStrength);
        Assert.Contains(r.Findings, f => f.Code == "marketOn" && !f.IsStrength);
    }
}
