using System.Reflection;
using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Release notes carry a translation key, and getting it wrong fails silently.
///
/// <c>NewsItemViewModel</c> builds its lookup as <c>news.{Key}.title</c>, so passing "news.v47" as the
/// key asks for <c>news.news.v47.title</c> — which no table defines, so the item quietly falls back to
/// the English literal. Nothing breaks, nothing logs, and every non-English user reads the release
/// note in English while the translation sits unused two files away. That is exactly what happened to
/// the 4.7 entry.
///
/// So every keyed news item is checked to resolve to something other than its own slug.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class NewsKeyTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"umbrella-news-{Guid.NewGuid():N}");

    /// <summary>
    /// A real view model, because the News list is built in a field initialiser.
    ///
    /// The first version of this test read it off an uninitialised instance to avoid the cost. That
    /// returned an EMPTY list, so the loop below never ran and the test passed against the very bug it
    /// was written for — which is worse than not having it. Proven by putting the broken key back and
    /// watching it still pass.
    /// </summary>
    private IReadOnlyList<NewsItemViewModel> News()
    {
        var vm = new MainViewModel(new Umbrella.Wallet.Infrastructure.EncryptedFileSeedVault(
            Path.Combine(_directory, "vault.json")));
        return vm.News.ToList();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
        catch { /* best effort */ }
    }

    private static IReadOnlyDictionary<string, Dictionary<string, string>> Tables()
    {
        var field = typeof(Loc).GetField("Strings", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (Dictionary<string, Dictionary<string, string>>)field!.GetValue(null)!;
    }

    [Fact]
    public void Every_release_note_translation_key_actually_resolves()
    {
        // Read the keys straight out of the translation tables rather than out of the view model: the
        // failure mode is a key that exists in ONE place and not the other, so checking one side
        // against the other is the whole point.
        var english = Tables()["en"];

        var titleKeys = english.Keys
            .Where(k => k.StartsWith("news.", StringComparison.Ordinal) && k.EndsWith(".title", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(titleKeys);

        // A doubled prefix is the specific mistake: news.news.v47.title.
        var doubled = titleKeys.Where(k => k.StartsWith("news.news.", StringComparison.Ordinal)).ToList();
        Assert.Empty(doubled);
    }

    [Fact]
    public void The_current_release_note_is_translated_not_just_present()
    {
        // 4.7 specifically: the entry the wallet opens on after this update. If its key is wrong the
        // note still displays, in English, which is why nobody would notice.
        var tables = Tables();

        foreach (var (code, table) in tables)
        {
            Assert.True(table.ContainsKey("news.v47.title"), $"{code} has no title for the 4.7 note");
        }

        // And the Ukrainian body is there, since that is the language this wallet is read in most.
        Assert.True(tables["uk"].ContainsKey("news.v47.body"));
    }

    [Fact]
    public void A_news_key_is_a_bare_version_not_a_prefixed_slug()
    {
        // The rule, stated where somebody adding 4.8 will read it: the key is "v48", and
        // NewsItemViewModel adds the "news." and the ".title"/".body" itself.
        foreach (var item in News())
        {
            var key = item.Key;
            if (string.IsNullOrEmpty(key)) continue;

            Assert.False(key.StartsWith("news.", StringComparison.Ordinal),
                $"news key '{key}' already carries the prefix that NewsItemViewModel adds");
        }
    }
}
