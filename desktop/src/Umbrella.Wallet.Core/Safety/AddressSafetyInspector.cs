namespace Umbrella.Wallet.Core.Safety;

/// <summary>How safe the destination of a send looks, worst-case first.</summary>
public enum AddressSafetyLevel
{
    /// <summary>A known-good destination (in the address book, or one you have sent to before).</summary>
    Known = 0,

    /// <summary>Never sent here before. Not dangerous, just worth a glance.</summary>
    NewRecipient = 1,

    /// <summary>The destination is one of THIS wallet's own addresses.</summary>
    OwnAddress = 2,

    /// <summary>
    /// Looks almost exactly like a known address — same start and end, different middle. This is the
    /// signature of an <b>address-poisoning</b> attack: the attacker sends you dust from a vanity
    /// address crafted to match a real recipient's first/last characters, hoping you later copy the
    /// wrong one from your history. The loudest warning.
    /// </summary>
    Lookalike = 3,
}

/// <summary>The verdict for one destination, plus the address it resembles (for a Lookalike).</summary>
public sealed record AddressSafetyResult(AddressSafetyLevel Level, string? SimilarTo = null)
{
    public bool IsWarning => Level is AddressSafetyLevel.Lookalike or AddressSafetyLevel.OwnAddress;
}

/// <summary>
/// A pure, offline check of a send destination against the addresses this wallet already knows — its
/// own receive addresses, the address book, and anyone it has paid before. It catches the scams a
/// self-custody wallet can catch without a server or a blocklist:
///
/// <list type="bullet">
/// <item><b>Address poisoning</b> — a destination that shares a long prefix AND suffix with a known
/// address but isn't it. Grinding a full match is infeasible, so a shared 6-char head and 5-char tail
/// on an otherwise different address of the same length is a deliberate lookalike, not a coincidence.</item>
/// <item><b>Sending to yourself</b> — usually a copy-paste slip.</item>
/// <item><b>A brand-new recipient</b> — informational, so a first payment gets a second look.</item>
/// </list>
///
/// It only ever <i>warns</i>; it never blocks a send and never reaches the network. Thresholds are
/// deliberately conservative so a legitimate address almost never trips the lookalike alarm.
/// </summary>
public static class AddressSafetyInspector
{
    // Matching this many leading and trailing characters, on two same-length but non-identical
    // addresses, is the poisoning signature. Kept high enough that honest addresses don't collide.
    private const int PrefixMatch = 6;
    private const int SuffixMatch = 5;

    public static AddressSafetyResult Inspect(
        string? destination,
        IReadOnlyCollection<string>? ownAddresses,
        IReadOnlyCollection<string>? knownAddresses)
    {
        var dest = (destination ?? string.Empty).Trim();
        if (dest.Length == 0) return new AddressSafetyResult(AddressSafetyLevel.NewRecipient);

        var own = ownAddresses ?? [];
        var known = knownAddresses ?? [];

        // Sending to one of your own addresses.
        foreach (var o in own)
            if (Eq(o, dest)) return new AddressSafetyResult(AddressSafetyLevel.OwnAddress);

        // An exact match in the address book / past recipients is a trusted destination.
        foreach (var k in known)
            if (Eq(k, dest)) return new AddressSafetyResult(AddressSafetyLevel.Known);

        // Poisoning lookalike: resembles a known-good OR own address without being it.
        foreach (var candidate in Enumerate(known, own))
        {
            if (Eq(candidate, dest)) continue;
            if (IsLookalike(dest, candidate))
                return new AddressSafetyResult(AddressSafetyLevel.Lookalike, candidate);
        }

        return new AddressSafetyResult(AddressSafetyLevel.NewRecipient);
    }

    /// <summary>
    /// True when <paramref name="a"/> and <paramref name="b"/> are the same length, differ somewhere,
    /// yet share the leading <see cref="PrefixMatch"/> and trailing <see cref="SuffixMatch"/>
    /// characters (case-insensitively) — the address-poisoning fingerprint.
    /// </summary>
    public static bool IsLookalike(string a, string b)
    {
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;              // a poisoned twin is grown to the same length
        if (a.Length < PrefixMatch + SuffixMatch) return false;
        if (Eq(a, b)) return false;                          // identical is not a lookalike

        for (var i = 0; i < PrefixMatch; i++)
            if (!CharEq(a[i], b[i])) return false;

        for (var i = 0; i < SuffixMatch; i++)
            if (!CharEq(a[^(i + 1)], b[^(i + 1)])) return false;

        return true;
    }

    private static IEnumerable<string> Enumerate(IReadOnlyCollection<string> known, IReadOnlyCollection<string> own)
    {
        foreach (var k in known) yield return k;
        foreach (var o in own) yield return o;
    }

    private static bool Eq(string? a, string? b) =>
        string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool CharEq(char a, char b) => char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
}
