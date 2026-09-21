namespace Umbrella.Wallet.App;

/// <summary>
/// The first-run acknowledgement: what the wallet must say before it will hold anybody's money, and
/// when it is entitled to consider that said (roadmap L.1 / L.3 / L.8, APP_STORE_NOTES §4).
///
/// Three facts have to land before a seed exists, because afterwards it is too late for two of them:
/// nobody here can recover a lost phrase, a public chain is not made private by this or any wallet,
/// and this is software rather than a financial institution. The age statement is the stores'
/// requirement and is asked in the same breath.
///
/// Pure so the rule can be tested without a window: a tick that only one of the two boxes carries
/// must not open the wallet, and an acceptance recorded against older wording must not count for new
/// wording.
/// </summary>
public static class FirstRunConsent
{
    /// <summary>
    /// The version of the disclaimer text currently shipped. Raise it when what the wallet says about
    /// custody, anonymity, recovery or the terms changes materially — never for a typo, because
    /// asking again for nothing trains people to click through.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>True when this install has not accepted the current wording yet.</summary>
    public static bool NeedsAcceptance(int acceptedVersion) => acceptedVersion < CurrentVersion;

    /// <summary>
    /// Both boxes, separately. They are two different statements — "I am old enough" and "I have read
    /// what this is" — and one tick standing for both is how consent screens become decoration.
    /// </summary>
    public static bool CanAccept(bool ageConfirmed, bool termsAccepted) => ageConfirmed && termsAccepted;
}
