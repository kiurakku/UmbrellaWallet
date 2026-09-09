using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the local Privacy Radar logic: a pending send's on-chain linkability + network privacy, computed
/// purely offline. The flagship signal is input linkage — spending from several of your addresses at once
/// publicly ties them together (common-input-ownership), the #1 self-custody deanonymisation vector.
/// </summary>
public sealed class SendPrivacyInspectorTests
{
    [Fact]
    public void One_input_over_tor_is_strong()
    {
        var r = SendPrivacyInspector.Inspect(new[] { "bc1qsingle" }, torEnabled: true);

        Assert.Equal(SendPrivacyLevel.Strong, r.Level);
        Assert.False(r.HasWeakness);
        Assert.DoesNotContain(r.Findings, f => f.IsWeakness);
    }

    [Fact]
    public void One_input_but_tor_off_is_moderate()
    {
        var r = SendPrivacyInspector.Inspect(new[] { "bc1qsingle" }, torEnabled: false);

        Assert.Equal(SendPrivacyLevel.Moderate, r.Level);
        Assert.Contains(r.Findings, f => f.IsWeakness && f.Code == "torOff");
    }

    [Fact]
    public void Two_addresses_link_and_are_moderate()
    {
        var r = SendPrivacyInspector.Inspect(new[] { "bc1qa", "bc1qb" }, torEnabled: true);

        Assert.Equal(SendPrivacyLevel.Moderate, r.Level);
        Assert.Contains(r.Findings, f => f.IsWeakness && f.Code == "linkTwo");
    }

    [Fact]
    public void Three_or_more_addresses_is_weak_even_over_tor()
    {
        var r = SendPrivacyInspector.Inspect(new[] { "a", "b", "c" }, torEnabled: true);

        Assert.Equal(SendPrivacyLevel.Weak, r.Level);
        // The linkage finding carries how many addresses got tied together (for the localised text).
        Assert.Contains(r.Findings, f => f.IsWeakness && f.Code == "linkMany" && f.Count == 3);
    }

    [Fact]
    public void Distinct_count_ignores_case_and_whitespace()
    {
        // The same address written three ways is ONE source — not a linkage.
        var r = SendPrivacyInspector.Inspect(new[] { "BC1QA", "bc1qa", " bc1qa " }, torEnabled: true);

        Assert.Equal(SendPrivacyLevel.Strong, r.Level);
        Assert.Contains(r.Findings, f => !f.IsWeakness && f.Code == "linkOne");
    }

    [Fact]
    public void Empty_input_never_throws_and_is_not_a_linkage()
    {
        var r = SendPrivacyInspector.Inspect(null, torEnabled: true);
        Assert.Equal(SendPrivacyLevel.Strong, r.Level);
        Assert.DoesNotContain(r.Findings, f => f.IsWeakness && f.Code.StartsWith("link"));
    }
}
