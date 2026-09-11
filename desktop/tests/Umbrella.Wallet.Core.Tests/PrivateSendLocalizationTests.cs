using System.Reflection;
using Umbrella.Wallet.App;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The private-send card is the one place the wallet states, in plain words, what it can and cannot do
/// for a user's privacy. Both halves have to survive contact with the translation tables.
///
/// A step or a limit added to the planner without a matching string does not fail loudly — <see cref="Loc"/>
/// falls back to English and then to the raw key, so a missing entry shows up in the UI as
/// "psend.limReused" next to real sentences. On the half of the card that lists what privacy you do NOT
/// have, an unreadable line is worse than no feature: somebody skims past it and sends anyway.
///
/// So every enum value is checked to have real text in every language the wallet ships.
/// </summary>
public sealed class PrivateSendLocalizationTests
{
    private static IReadOnlyDictionary<string, Dictionary<string, string>> Tables()
    {
        var field = typeof(Loc).GetField("Strings", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (Dictionary<string, Dictionary<string, string>>)field!.GetValue(null)!;
    }

    /// <summary>The same key mapping the view-model uses, kept here deliberately: if the two drift, a
    /// step silently starts rendering the wrong sentence, which is the failure this guards.</summary>
    private static string KeyFor(PrivateSendStep step) => step switch
    {
        PrivateSendStep.EnableTor => "psend.stepTor",
        PrivateSendStep.WaitForTor => "psend.stepWait",
        PrivateSendStep.EnableKillSwitch => "psend.stepKill",
        PrivateSendStep.NarrowInputs => "psend.stepInputs",
        PrivateSendStep.FreshChangeAddress => "psend.stepChange",
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "No translation key for this step."),
    };

    private static string KeyFor(PrivateSendLimit limit) => limit switch
    {
        PrivateSendLimit.LedgerIsPublicForever => "psend.limPublic",
        PrivateSendLimit.InputsLinkAddresses => "psend.limLink",
        PrivateSendLimit.RecipientAddressIsReused => "psend.limReused",
        PrivateSendLimit.RecipientStillLearnsWhoPaid => "psend.limRecipient",
        _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, "No translation key for this limit."),
    };

    [Fact]
    public void Every_step_has_wording_in_every_language()
    {
        var tables = Tables();
        var gaps = new List<string>();

        foreach (var step in Enum.GetValues<PrivateSendStep>())
        {
            var key = KeyFor(step);
            foreach (var (code, table) in tables)
            {
                if (!table.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
                    gaps.Add($"{code}: {step} ({key})");
            }
        }

        Assert.Empty(gaps);
    }

    [Fact]
    public void Every_limit_has_wording_in_every_language()
    {
        var tables = Tables();
        var gaps = new List<string>();

        foreach (var limit in Enum.GetValues<PrivateSendLimit>())
        {
            var key = KeyFor(limit);
            foreach (var (code, table) in tables)
            {
                if (!table.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
                    gaps.Add($"{code}: {limit} ({key})");
            }
        }

        Assert.Empty(gaps);
    }

    [Fact]
    public void The_cards_own_labels_are_translated_too()
    {
        // The headings carry the meaning: without "What it cannot change" above the second list, those
        // lines read as more reassurance rather than as the warning they are.
        var tables = Tables();
        var gaps = new List<string>();

        foreach (var key in new[] { "psend.title", "psend.hint", "psend.willDo", "psend.cannot", "psend.ready", "psend.apply" })
        foreach (var (code, table) in tables)
        {
            if (!table.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
                gaps.Add($"{code}: {key}");
        }

        Assert.Empty(gaps);
    }

    [Fact]
    public void No_language_leaves_a_limit_saying_the_same_thing_as_english()
    {
        // A copy-pasted English sentence in the German table is a gap that the parity test cannot see,
        // because the key IS present. Checked only on the limits, where being unreadable is worst.
        var tables = Tables();
        var english = tables["en"];
        var untranslated = new List<string>();

        foreach (var limit in Enum.GetValues<PrivateSendLimit>())
        {
            var key = KeyFor(limit);
            foreach (var (code, table) in tables)
            {
                if (code == "en") continue;
                if (table.TryGetValue(key, out var text) && text == english[key])
                    untranslated.Add($"{code}: {key}");
            }
        }

        Assert.Empty(untranslated);
    }
}
