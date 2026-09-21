namespace Umbrella.Wallet.Core.Safety;

/// <summary>The wallet's overall network / metadata privacy, at a glance.</summary>
public enum PrivacyGrade
{
    Exposed,
    Moderate,
    Strong,
}

/// <summary>
/// The live signals the score is built from — all real wallet settings, never guesses — so the score is
/// a mirror of the user's actual posture, not a promise. <c>RichMarketDataLeak</c> is true when the
/// optional third-party market-data connector is ON (price lookups reveal your coins to that server).
/// </summary>
public readonly record struct PrivacySignals(
    bool TorEnabled,
    bool TorOnly,
    bool RichMarketDataLeak);

/// <summary>
/// One reason the score is what it is: a strength to keep, or a weakness that's dragging it down.
///
/// <c>LimitCode</c> is the half that MANIFESTO §2 makes mandatory — what this protection does not do,
/// or what the weakness actually costs. A privacy feature shown without its limits is how somebody
/// ends up trusting more than they should, and "Tor: on" reads to most people as "I am anonymous"
/// when it means "the servers I query see an exit node instead of my address" — and nothing else.
///
/// Both are language-neutral codes so the UI builds the localized sentence (roadmap §8.2).
/// </summary>
public sealed record PrivacyScoreFinding(string Code, bool IsStrength, string LimitCode);

/// <summary>The graded result: a 0–100 value, its band, the single most valuable fix (null when nothing
/// is left to improve), and every finding behind it.</summary>
public sealed record PrivacyScore(
    int Value, PrivacyGrade Grade, string? TopFixCode, IReadOnlyList<PrivacyScoreFinding> Findings);

/// <summary>
/// Grades the wallet's privacy posture from its live network/metadata settings — the "Privacy Radar" for
/// the whole wallet, not just one send. Pure and offline (no network, no state, no I/O), so it is fully
/// unit-testable and can never itself leak anything.
///
/// The weights put network anonymity first — Tor is the single biggest lever — then the Tor-only kill
/// switch, then third-party metadata leaks. Telemetry is always off in this wallet, so it is a constant
/// strength rather than a scored lever (a score can never be padded with something the user can't change).
/// </summary>
public static class PrivacyScoreInspector
{
    public const int TorWeight = 50;
    public const int KillSwitchWeight = 25;
    public const int NoMarketLeakWeight = 25;

    public const int StrongFrom = 80;
    public const int ModerateFrom = 50;

    public static PrivacyScore Evaluate(PrivacySignals s)
    {
        var findings = new List<PrivacyScoreFinding>();
        var value = 0;

        // Network anonymity — the biggest lever.
        if (s.TorEnabled) { value += TorWeight; findings.Add(new PrivacyScoreFinding("torOn", true, "torOn")); }
        else findings.Add(new PrivacyScoreFinding("torOff", false, "torOff"));

        // Kill switch: block all clearnet if Tor drops, so nothing quietly falls back to a bare connection.
        if (s.TorOnly) { value += KillSwitchWeight; findings.Add(new PrivacyScoreFinding("killOn", true, "killOn")); }
        else findings.Add(new PrivacyScoreFinding("killOff", false, "killOff"));

        // Third-party market data is an opt-in metadata leak: price lookups tell a server which coins you hold.
        if (!s.RichMarketDataLeak) { value += NoMarketLeakWeight; findings.Add(new PrivacyScoreFinding("marketOff", true, "marketOff")); }
        else findings.Add(new PrivacyScoreFinding("marketOn", false, "marketOn"));

        var grade = value >= StrongFrom ? PrivacyGrade.Strong
                  : value >= ModerateFrom ? PrivacyGrade.Moderate
                  : PrivacyGrade.Exposed;

        // The single most valuable fix = the highest-impact weakness still open, in priority order
        // (network anonymity first, then the active metadata leak, then the kill switch).
        string? topFix =
            !s.TorEnabled ? "torOff"
            : s.RichMarketDataLeak ? "marketOn"
            : !s.TorOnly ? "killOff"
            : null;

        return new PrivacyScore(value, grade, topFix, findings);
    }
}
