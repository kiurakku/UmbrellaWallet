namespace Umbrella.Wallet.Core.Safety;

/// <summary>
/// How long the wallet refuses to try the next password after a run of wrong ones.
///
/// This is aimed at one attacker in particular: somebody sitting at an unlocked machine with the
/// wallet open, typing guesses. Without a delay they can try as fast as they can type; with it, the
/// tenth guess costs minutes and the twentieth costs an hour, which turns a coffee break into an
/// impossibility.
///
/// It is deliberately NOT sold as more than that. Anyone who can copy <c>vault.json</c> can attack it
/// offline where no UI rule reaches them — that attack is answered by Argon2id (64 MiB, t=4, p=2),
/// which makes each guess expensive in itself, and by the length of the password. Anyone who can edit
/// the settings file can reset this counter. Both are true, both are written down, and neither is a
/// reason to leave the keyboard case unanswered.
///
/// Failures persist across restarts on purpose. A counter that resets when the app is reopened stops
/// exactly nobody: closing the window is easier than waiting.
/// </summary>
public static class UnlockThrottle
{
    /// <summary>Wrong answers allowed before any delay at all. Mistyping a long password twice is
    /// ordinary and should not be punished.</summary>
    public const int FreeAttempts = 3;

    /// <summary>The delay never grows past this, so a forgotten password cannot lock somebody out of
    /// their own funds for a day. The vault is theirs; the wallet's job is to slow an attacker, not to
    /// take it hostage.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The wait after <paramref name="consecutiveFailures"/> wrong passwords, doubling each time past
    /// the free ones: 5s, 10s, 20s, 40s … up to <see cref="MaxDelay"/>.
    /// </summary>
    public static TimeSpan DelayAfter(int consecutiveFailures)
    {
        if (consecutiveFailures <= FreeAttempts) return TimeSpan.Zero;

        var steps = consecutiveFailures - FreeAttempts;      // 1, 2, 3, …
        // Cap the exponent before shifting: 1 << 40 is not a long the way anybody wants.
        if (steps > 20) return MaxDelay;

        var seconds = 5.0 * Math.Pow(2, steps - 1);
        var delay = TimeSpan.FromSeconds(seconds);
        return delay > MaxDelay ? MaxDelay : delay;
    }

    /// <summary>
    /// How much of the wait is left, given when the last wrong password was entered. Zero means the
    /// next attempt may go ahead.
    /// </summary>
    /// <param name="now">Passed in rather than read from the clock, so the rule is testable and so a
    /// clock that jumps backwards cannot silently extend a lockout.</param>
    public static TimeSpan Remaining(int consecutiveFailures, DateTimeOffset lastFailure, DateTimeOffset now)
    {
        var required = DelayAfter(consecutiveFailures);
        if (required == TimeSpan.Zero) return TimeSpan.Zero;

        var elapsed = now - lastFailure;

        // A clock moved backwards would otherwise leave the user waiting out the difference. Treat
        // negative elapsed time as "no time has passed", never as extra punishment.
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        var left = required - elapsed;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>True when an unlock attempt should be refused right now.</summary>
    public static bool IsBlocked(int consecutiveFailures, DateTimeOffset lastFailure, DateTimeOffset now) =>
        Remaining(consecutiveFailures, lastFailure, now) > TimeSpan.Zero;

    /// <summary>A short "try again in …" for the lock screen, in whole seconds or minutes.</summary>
    public static string Describe(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return string.Empty;

        var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        if (seconds < 60) return $"{seconds}s";

        var minutes = (int)Math.Ceiling(remaining.TotalMinutes);
        return $"{minutes}m";
    }
}
