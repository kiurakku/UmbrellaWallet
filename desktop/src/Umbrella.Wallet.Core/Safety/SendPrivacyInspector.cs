namespace Umbrella.Wallet.Core.Safety;

/// <summary>How much a pending send protects (or leaks) the user's on-chain privacy, worst-case first.</summary>
public enum SendPrivacyLevel
{
    /// <summary>Nothing about this transaction links your addresses or leaks your network identity.</summary>
    Strong = 0,

    /// <summary>A mild leak — worth knowing, but not a serious deanonymisation.</summary>
    Moderate = 1,

    /// <summary>A real privacy leak — this transaction publicly links several of your addresses.</summary>
    Weak = 2,
}

/// <summary>
/// One privacy observation about a pending send. Language-neutral: <see cref="Code"/> identifies the
/// finding ("linkOne", "linkTwo", "linkMany", "torOn", "torOff") and the presentation layer turns it
/// into localised text (substituting <see cref="Count"/> for the "linkMany" case). A weakness is
/// something the user can act on. Keeping the wording out of Core honours §8.2 — no hardcoded English
/// in the financial flow.
/// </summary>
public sealed record SendPrivacyFinding(string Glyph, string Code, bool IsWeakness, int Count = 0);

/// <summary>The full local privacy assessment of a pending send: an overall level and the findings
/// behind it. The one-line headline is derived from <see cref="Level"/> by the presentation layer.</summary>
public sealed record SendPrivacyReport(SendPrivacyLevel Level, IReadOnlyList<SendPrivacyFinding> Findings)
{
    public bool HasWeakness => Level != SendPrivacyLevel.Strong;
}

/// <summary>
/// The local core of "Privacy Radar": a pure, offline read of how a pending transaction affects the
/// user's own privacy — chain-analysis <b>for</b> the user, not against them. It never reaches the
/// network, never contacts a server, and only ever informs; it does not block a send.
///
/// It is deliberately separate from <see cref="AddressSafetyInspector"/> (which is about scam SAFETY —
/// poisoning, wrong recipient). This is about LINKABILITY:
///
/// <list type="bullet">
/// <item><b>Input linkage</b> — the big one. On a UTXO chain, spending coins that sit on several of
/// your addresses in one transaction publicly ties those addresses to a single owner (the
/// common-input-ownership heuristic every chain-analysis firm uses). Account chains (ETH/SOL/TRON) have
/// a single source address, so this never applies to them.</item>
/// <item><b>Network privacy</b> — whether the broadcast rides Tor, so the node that first sees the
/// transaction can't tie it to your IP.</item>
/// </list>
/// </summary>
public static class SendPrivacyInspector
{
    // Spending from this many distinct addresses at once is a serious, hard-to-undo linkage; two is a
    // mild one worth a nudge toward coin control.
    private const int StrongLinkAddressCount = 3;

    /// <summary>
    /// Assesses a pending send. <paramref name="inputAddresses"/> are the addresses funding it — the
    /// spend plan's inputs on a UTXO chain, or the single from-address on an account chain (pass one).
    /// <paramref name="torEnabled"/> is whether the broadcast will ride Tor. Pure and offline.
    /// </summary>
    public static SendPrivacyReport Inspect(IReadOnlyCollection<string>? inputAddresses, bool torEnabled)
    {
        var findings = new List<SendPrivacyFinding>();

        var distinct = (inputAddresses ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim().ToLowerInvariant())
            .Distinct()
            .Count();

        // --- Input linkage (UTXO chains) ---
        if (distinct >= StrongLinkAddressCount)
            findings.Add(new SendPrivacyFinding("🔗", "linkMany", IsWeakness: true, Count: distinct));
        else if (distinct == 2)
            findings.Add(new SendPrivacyFinding("🔗", "linkTwo", IsWeakness: true));
        else
            findings.Add(new SendPrivacyFinding("🔗", "linkOne", IsWeakness: false));

        // --- Network privacy ---
        findings.Add(torEnabled
            ? new SendPrivacyFinding("🧅", "torOn", IsWeakness: false)
            : new SendPrivacyFinding("🌐", "torOff", IsWeakness: true));

        // --- Overall level ---
        var level =
            distinct >= StrongLinkAddressCount ? SendPrivacyLevel.Weak
            : findings.Any(f => f.IsWeakness) ? SendPrivacyLevel.Moderate
            : SendPrivacyLevel.Strong;

        return new SendPrivacyReport(level, findings);
    }
}
