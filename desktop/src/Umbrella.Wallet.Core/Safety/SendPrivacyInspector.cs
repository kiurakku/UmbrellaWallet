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

/// <summary>One privacy observation about a pending send. A weakness is something the user can act on.</summary>
public sealed record SendPrivacyFinding(string Glyph, string Title, string Detail, bool IsWeakness);

/// <summary>The full local privacy assessment of a pending send: an overall level, a one-line headline,
/// and the individual findings behind it.</summary>
public sealed record SendPrivacyReport(SendPrivacyLevel Level, string Headline, IReadOnlyList<SendPrivacyFinding> Findings)
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
        {
            findings.Add(new SendPrivacyFinding("🔗",
                "Links several of your addresses",
                $"This spend draws coins from {distinct} of your addresses. Spending them together publicly " +
                "ties them to one owner — chain analysis can now group that history. Turn on coin control " +
                "and fund the send from a single address when privacy matters.",
                IsWeakness: true));
        }
        else if (distinct == 2)
        {
            findings.Add(new SendPrivacyFinding("🔗",
                "Combines two of your addresses",
                "This spend draws coins from two of your addresses, which links them on-chain. If that " +
                "matters, use coin control to spend from just one.",
                IsWeakness: true));
        }
        else
        {
            findings.Add(new SendPrivacyFinding("🔗",
                "One source address",
                "The spend is funded from a single address, so it links none of your other addresses.",
                IsWeakness: false));
        }

        // --- Network privacy ---
        findings.Add(torEnabled
            ? new SendPrivacyFinding("🧅", "Broadcast over Tor",
                "Your IP stays hidden from the node that first relays this transaction.", IsWeakness: false)
            : new SendPrivacyFinding("🌐", "Tor is off",
                "The broadcast node can associate this transaction with your IP address. Turn on Tor in " +
                "Settings to hide it.", IsWeakness: true));

        // --- Overall level ---
        var level =
            distinct >= StrongLinkAddressCount ? SendPrivacyLevel.Weak
            : findings.Any(f => f.IsWeakness) ? SendPrivacyLevel.Moderate
            : SendPrivacyLevel.Strong;

        var headline = level switch
        {
            SendPrivacyLevel.Strong => "Strong privacy — nothing here links your addresses or leaks your IP.",
            SendPrivacyLevel.Moderate => "Moderate privacy — one thing below could reduce it.",
            _ => "Weak privacy — this transaction links several of your addresses.",
        };

        return new SendPrivacyReport(level, headline, findings);
    }
}
