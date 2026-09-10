using Umbrella.Wallet.App;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the recovery-phrase back-check: it passes only when the exact words at the asked positions are
/// typed back (case-insensitive, trimmed), and the random position picker yields distinct, in-range,
/// sorted positions.
/// </summary>
public sealed class SeedVerificationTests
{
    private const string Phrase =
        "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima";

    [Fact]
    public void Passes_when_the_right_words_are_entered_case_and_space_insensitively()
    {
        Assert.True(SeedVerification.Check(Phrase, new[] { 1, 3 }, new[] { "alpha", "charlie" }));
        Assert.True(SeedVerification.Check(Phrase, new[] { 1 }, new[] { "  ALPHA " })); // trimmed + case-fold
    }

    [Fact]
    public void Fails_on_a_wrong_word_count_mismatch_out_of_range_or_blank()
    {
        Assert.False(SeedVerification.Check(Phrase, new[] { 1 }, new[] { "bravo" }));   // wrong word
        Assert.False(SeedVerification.Check(Phrase, new[] { 1, 2 }, new[] { "alpha" })); // count mismatch
        Assert.False(SeedVerification.Check(Phrase, new[] { 99 }, new[] { "alpha" }));   // out of range
        Assert.False(SeedVerification.Check(Phrase, new[] { 1 }, new[] { "" }));         // blank
        Assert.False(SeedVerification.Check(Phrase, System.Array.Empty<int>(), System.Array.Empty<string>()));
    }

    [Fact]
    public void PickPositions_are_distinct_in_range_and_sorted()
    {
        var p = SeedVerification.PickPositions(24, 3, new System.Random(123));
        Assert.Equal(3, p.Count);
        Assert.Equal(p.Distinct().Count(), p.Count);            // distinct
        Assert.All(p, x => Assert.InRange(x, 1, 24));           // in range
        Assert.True(p.SequenceEqual(p.OrderBy(x => x)));        // sorted ascending
    }

    [Fact]
    public void PickPositions_clamps_to_the_word_count_and_handles_empty()
    {
        var p = SeedVerification.PickPositions(2, 5, new System.Random(1));
        Assert.Equal(2, p.Count);
        Assert.All(p, x => Assert.InRange(x, 1, 2));
        Assert.Empty(SeedVerification.PickPositions(0, 3, new System.Random(1)));
    }
}
