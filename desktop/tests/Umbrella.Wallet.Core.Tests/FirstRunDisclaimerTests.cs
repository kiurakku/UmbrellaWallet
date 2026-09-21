using System.Reflection;
using Umbrella.Wallet.App;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap L.1 / L.3 / L.8 — the first-run acknowledgement, and the wording rules around it.
///
/// Two of the three things this screen says are useless afterwards: nobody can recover a lost
/// recovery phrase, and a public chain is not made private by this or any wallet. So it has to come
/// before a seed exists, and it has to be a gate rather than a notice — a disclaimer the user can
/// walk around is a disclaimer for the developer's benefit.
///
/// The second half of this file enforces APP_STORE_NOTES §3: the claims that get a wallet rejected
/// by a store are the same claims that mislead somebody about what a transparent chain reveals, so
/// they are refused in every language rather than only in the English the author reads.
/// </summary>
public sealed class FirstRunDisclaimerTests
{
    [Fact]
    public void A_fresh_install_has_not_accepted_anything()
    {
        Assert.True(FirstRunConsent.NeedsAcceptance(new UiSettings().AcceptedTermsVersion));
    }

    [Fact]
    public void Accepting_the_current_wording_satisfies_the_gate()
    {
        Assert.False(FirstRunConsent.NeedsAcceptance(FirstRunConsent.CurrentVersion));
    }

    /// <summary>
    /// An acceptance is against WORDS, not a boolean. If what the wallet says about custody or
    /// anonymity changes materially, the old tick cannot stand in for the new text.
    /// </summary>
    [Fact]
    public void An_acceptance_of_older_wording_does_not_carry_over()
    {
        Assert.True(FirstRunConsent.NeedsAcceptance(FirstRunConsent.CurrentVersion - 1));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void One_tick_never_stands_in_for_both(bool age, bool terms)
    {
        // "I am old enough" and "I have read what this is" are different statements; collapsing them
        // into one checkbox is how a consent screen becomes decoration.
        Assert.False(FirstRunConsent.CanAccept(age, terms));
    }

    [Fact]
    public void Both_ticks_open_the_wallet()
    {
        Assert.True(FirstRunConsent.CanAccept(true, true));
    }

    /// <summary>
    /// The screen must actually carry the five points APP_STORE_NOTES §4 requires, in every language
    /// the wallet ships — a translated wallet that shows an English-only disclaimer has not told that
    /// user anything.
    /// </summary>
    [Fact]
    public void Every_language_carries_the_whole_first_run_text()
    {
        string[] required =
        [
            "first.title", "first.custodyTitle", "first.custodyBody",
            "first.privacyTitle", "first.privacyBody", "first.riskTitle", "first.riskBody",
            "first.age", "first.terms", "first.accept",
        ];

        var missing = new List<string>();
        foreach (var (code, table) in Tables())
        {
            foreach (var key in required)
            {
                if (!table.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    missing.Add($"{code}:{key}");
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// APP_STORE_NOTES §3. These are absolute claims about privacy that are false on a transparent
    /// chain, and they are exactly what gets a crypto wallet rejected — but the reason they are
    /// banned here is MANIFESTO §1: the person reading them is reading them because they need the
    /// truth.
    /// </summary>
    [Fact]
    public void No_language_makes_an_absolute_privacy_claim()
    {
        (string Phrase, string Why)[] forbidden =
        [
            ("fully private", "absolute claim; false on BTC/ETH"),
            ("untraceable", "absolute claim"),
            ("100% anonym", "absolute claim"),
            ("completely anonymous", "absolute claim"),
            ("totally anonymous", "absolute claim"),
            ("guaranteed anonymity", "Tor hides an IP; it guarantees nothing"),
            ("повністю анонім", "absolute claim (uk)"),
            ("полностью аноним", "absolute claim (ru)"),
            ("completamente anónim", "absolute claim (es)"),
            ("völlig anonym", "absolute claim (de)"),
            ("完全匿名", "absolute claim (zh)"),
        ];

        var offenders = new List<string>();
        foreach (var (code, table) in Tables())
        {
            foreach (var (key, value) in table)
            {
                foreach (var (phrase, why) in forbidden)
                {
                    if (value.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                        offenders.Add($"{code}:{key} — \"{phrase}\" ({why})");
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The gate itself. Every onboarding stage must be conditioned on the acknowledgement, so there
    /// is no way into the wallet around it — not create, not import, and not unlock for somebody who
    /// already has a vault.
    ///
    /// Read from the source rather than driven through a view model on purpose: the rest of the suite
    /// runs as an install that has already accepted (see TestDataIsolation), and a test that rewrote
    /// that shared state would hand every other test a half-configured install.
    /// </summary>
    [Fact]
    public void Every_way_into_the_wallet_is_behind_the_acknowledgement()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "desktop", "src", "Umbrella.Wallet.App", "ViewModels", "MainViewModel.cs"));

        foreach (var stage in new[] { "IsWelcomeStage", "IsCreateStage", "IsImportStage", "IsUnlockStage" })
        {
            var line = source
                .Split('\n')
                .FirstOrDefault(l => l.Contains($"public bool {stage} =>", StringComparison.Ordinal));

            Assert.NotNull(line);
            Assert.Contains("!NeedsDisclaimer", line!, StringComparison.Ordinal);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "desktop", "src"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root not found from " + AppContext.BaseDirectory);
    }

    private static IReadOnlyDictionary<string, Dictionary<string, string>> Tables()
    {
        var field = typeof(Loc).GetField("Strings", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetValue(null) as Dictionary<string, Dictionary<string, string>>;
        Assert.NotNull(value);
        return value!;
    }
}
