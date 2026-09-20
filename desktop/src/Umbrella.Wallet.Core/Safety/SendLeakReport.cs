namespace Umbrella.Wallet.Core.Safety;

/// <summary>
/// What a send actually exposed, as the wallet knows it at the moment of broadcast. Every field is a
/// fact the wallet has — never an estimate, because an invented number in a privacy report is worse
/// than no report.
/// </summary>
public readonly record struct SendLeakSignals(
    /// <summary>The broadcast went through Tor (or the user's proxy), so the node saw an exit node.</summary>
    bool RoutedThroughTor,
    /// <summary>The Tor-only kill-switch was armed, so no part of this send could fall back to clearnet.</summary>
    bool KillSwitchArmed,
    /// <summary>How many of the user's own addresses this spend drew on. Two or more publicly tie them
    /// together — the common-input-ownership heuristic, the single biggest self-custody leak.</summary>
    int InputAddressCount,
    /// <summary>The chain hides amounts and parties by protocol (Monero). Everything else is public.</summary>
    bool ChainHidesAmounts,
    /// <summary>Change returned to a fresh internal address rather than back to a used one.</summary>
    bool FreshChangeUsed,
    /// <summary>The user picked the coins to spend, rather than letting the planner choose.</summary>
    bool CoinControlUsed);

/// <summary>
/// One line of the report: what happened, and whether it protected the user or exposed them.
/// Language-neutral codes; the UI builds the sentence (roadmap §8.2).
/// </summary>
public sealed record SendLeakFinding(string Code, bool IsProtected, int Count = 0);

/// <summary>
/// Roadmap P1.12 — "what leaked?", said immediately after a send instead of never.
///
/// A wallet that only shows privacy settings tells you what you configured. It does not tell you what
/// the operation you just performed actually cost, and those are different questions: Tor can be on
/// and the spend can still have joined five of your addresses forever, in public, because that is how
/// a UTXO chain works.
///
/// So this reports the specific transfer, in both directions — what was protected and what was
/// exposed — while the user can still act on it (choose different coins next time, turn Tor on before
/// the next send, stop reusing that receive address).
///
/// Pure and offline. It reads signals; it never reaches for them.
/// </summary>
public static class SendLeakReport
{
    public static IReadOnlyList<SendLeakFinding> Build(SendLeakSignals s)
    {
        var findings = new List<SendLeakFinding>();

        // 1. The network. This is the only part a wallet can genuinely hide.
        findings.Add(s.RoutedThroughTor
            ? new SendLeakFinding("ipHidden", true)
            : new SendLeakFinding("ipSeen", false));

        if (s.RoutedThroughTor && !s.KillSwitchArmed)
            findings.Add(new SendLeakFinding("noKillSwitch", false));

        // 2. The ledger. On a transparent chain the transaction is public the moment it confirms, and
        //    no setting in this or any wallet changes that.
        findings.Add(s.ChainHidesAmounts
            ? new SendLeakFinding("ledgerPrivate", true)
            : new SendLeakFinding("ledgerPublic", false));

        // 3. Linkage — the leak people do not expect, and the one that lasts.
        if (!s.ChainHidesAmounts)
        {
            findings.Add(s.InputAddressCount >= 2
                ? new SendLeakFinding("inputsLinked", false, s.InputAddressCount)
                : new SendLeakFinding("inputsSingle", true, s.InputAddressCount));

            findings.Add(s.FreshChangeUsed
                ? new SendLeakFinding("freshChange", true)
                : new SendLeakFinding("changeReused", false));

            if (s.CoinControlUsed) findings.Add(new SendLeakFinding("coinControl", true));
        }

        // 4. The one nobody can fix: the person being paid knows who paid them.
        findings.Add(new SendLeakFinding("recipientKnows", false));

        return findings;
    }

    /// <summary>True when at least one line of the report is an exposure the user could have avoided.</summary>
    public static bool HasAvoidableExposure(IReadOnlyList<SendLeakFinding> findings) =>
        findings.Any(f => !f.IsProtected && f.Code is "ipSeen" or "noKillSwitch" or "inputsLinked" or "changeReused");
}
