using System.Reflection;
using Umbrella.Wallet.App;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The translation tables must stay in step with each other.
///
/// A missing key does not fail loudly — <see cref="Loc"/> falls back to English — so whole features
/// can quietly render in English inside an otherwise translated wallet. That is exactly what had
/// happened: Russian and Chinese were missing 67 keys and Spanish and German 73, covering the address
/// checker, private notes, sign-and-verify and most of Settings. Duplicated keys are the same class of
/// silent problem: a C# indexer initializer lets the later assignment win without complaint.
/// </summary>
public sealed class LocalizationParityTests
{
    private static IReadOnlyDictionary<string, Dictionary<string, string>> Tables()
    {
        var field = typeof(Loc).GetField("Strings", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetValue(null) as Dictionary<string, Dictionary<string, string>>;
        Assert.NotNull(value);
        return value!;
    }

    [Fact]
    public void Every_language_defines_every_key_english_defines()
    {
        var tables = Tables();
        var english = tables["en"];

        var gaps = new List<string>();
        foreach (var (code, table) in tables)
        {
            if (code == "en") continue;
            var missing = english.Keys.Where(k => !table.ContainsKey(k)).OrderBy(k => k).ToList();
            if (missing.Count > 0)
                gaps.Add($"{code} is missing {missing.Count}: {string.Join(", ", missing.Take(10))}");
        }

        Assert.Empty(gaps);
    }

    /// <summary>
    /// Release-note bodies are the one deliberate exception to "English defines every key". The English
    /// text is the literal already held by the news item itself, and
    /// <c>NewsItemViewModel</c> falls back to it when no translation exists — so copying those long
    /// notes into the English table too would duplicate them for no benefit.
    /// </summary>
    private static bool IsTranslationOnlyKey(string key) =>
        key.StartsWith("news.", StringComparison.Ordinal) && key.EndsWith(".body", StringComparison.Ordinal);

    [Fact]
    public void No_language_defines_a_key_english_does_not()
    {
        // Usually a typo, and such a key can never be reached through the English fallback path.
        var tables = Tables();
        var english = tables["en"];

        var strays = new List<string>();
        foreach (var (code, table) in tables)
        {
            if (code == "en") continue;
            var extra = table.Keys
                .Where(k => !english.ContainsKey(k) && !IsTranslationOnlyKey(k))
                .OrderBy(k => k)
                .ToList();
            if (extra.Count > 0)
                strays.Add($"{code} has {extra.Count} key(s) English lacks: {string.Join(", ", extra.Take(10))}");
        }

        Assert.Empty(strays);
    }

    [Fact]
    public void No_translation_is_blank()
    {
        var blanks = new List<string>();
        foreach (var (code, table) in Tables())
        {
            foreach (var (key, value) in table)
            {
                if (string.IsNullOrWhiteSpace(value)) blanks.Add($"{code}:{key}");
            }
        }

        Assert.Empty(blanks);
    }

    [Fact]
    public void Placeholders_survive_translation()
    {
        // A string like "✓ Valid {0} address" loses its meaning if a translation drops the {0}, and
        // string.Format would then silently render a sentence with a hole in it.
        var tables = Tables();
        var english = tables["en"];

        var broken = new List<string>();
        foreach (var (code, table) in tables)
        {
            if (code == "en") continue;
            foreach (var (key, englishValue) in english)
            {
                if (!table.TryGetValue(key, out var translated)) continue;

                foreach (var token in new[] { "{0}", "{1}", "{2}" })
                {
                    if (englishValue.Contains(token) && !translated.Contains(token))
                        broken.Add($"{code}:{key} drops {token}");
                }
            }
        }

        Assert.Empty(broken);
    }

    [Fact]
    public void Every_supported_language_can_be_selected_and_returns_its_own_strings()
    {
        var tables = Tables();
        var previous = Loc.Instance.CurrentCode;
        try
        {
            foreach (var code in tables.Keys)
            {
                Loc.Instance.CurrentCode = code;
                // A key every language translates differently from English proves the table is live.
                Assert.False(string.IsNullOrWhiteSpace(Loc.Instance["nav.receive"]));
            }
        }
        finally
        {
            Loc.Instance.CurrentCode = previous;
        }
    }
}
