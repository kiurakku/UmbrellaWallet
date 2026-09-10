using System;
using System.Collections.Generic;
using System.Linq;

namespace Umbrella.Wallet.App;

/// <summary>
/// The back-of-the-backup check: after showing the recovery phrase, the wallet asks the user to type a
/// few of its words back (by position) to prove they actually wrote it down. Pure and offline so it is
/// unit-testable — it never touches the vault, and comparison is case-insensitive and trimmed so an
/// extra space or capital letter is not a false failure.
/// </summary>
public static class SeedVerification
{
    /// <summary>Picks <paramref name="howMany"/> distinct 1-based word positions from 1..wordCount, sorted
    /// ascending (so the prompts read "word 3 / 7 / 24"). Fewer are returned if the phrase is shorter.</summary>
    public static IReadOnlyList<int> PickPositions(int wordCount, int howMany, Random rng)
    {
        if (wordCount <= 0 || howMany <= 0) return Array.Empty<int>();
        var pool = Enumerable.Range(1, wordCount).ToList();
        var take = Math.Min(howMany, pool.Count);
        var picked = new List<int>(take);
        for (var i = 0; i < take; i++)
        {
            var idx = rng.Next(pool.Count);
            picked.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        picked.Sort();
        return picked;
    }

    /// <summary>True only when every (position, word) pair matches the phrase — case-insensitive, trimmed.
    /// Any count mismatch, out-of-range position, or wrong word fails the whole check.</summary>
    public static bool Check(string? phrase, IReadOnlyList<int> positions, IReadOnlyList<string?> words)
    {
        if (positions.Count == 0 || positions.Count != words.Count) return false;
        var parts = (phrase ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i < positions.Count; i++)
        {
            var pos = positions[i];
            if (pos < 1 || pos > parts.Length) return false;
            var got = (words[i] ?? string.Empty).Trim();
            if (got.Length == 0) return false;
            if (!string.Equals(parts[pos - 1], got, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
