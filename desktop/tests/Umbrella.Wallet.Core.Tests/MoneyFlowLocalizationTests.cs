namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap §8.2: "no hardcoded English strings in the financial flow". The send and backup screens
/// are where a misunderstood sentence costs money, so their messages must come from the translation
/// table, not from a literal in the middle of the code.
///
/// This reads the source itself rather than the running view model: a language-switch test would have
/// to mutate the global <c>Loc</c> singleton, which other tests run alongside.
/// </summary>
public sealed class MoneyFlowLocalizationTests
{
    /// <summary>Assignments that would put an untranslatable sentence in front of the user.</summary>
    private static readonly string[] Forbidden =
    [
        "SendError = \"",
        "SendError = $\"",
        "BackupError = \"",
        "BackupError = $\"",
        "BackupStatus = \"",
        "BackupStatus = $\"",
        // The status line is what the user reads after almost every action — copy, lock, broadcast,
        // wallet switch. All 47 of its messages were hardcoded English while this suite stayed green,
        // because it only watched the send/backup error fields.
        "StatusMessage = \"",
        "StatusMessage = $\"",
    ];

    [Fact]
    public void The_send_and_backup_flows_have_no_hardcoded_user_facing_strings()
    {
        var viewModels = FindViewModelsDirectory();
        if (viewModels is null) return; // not run from a source checkout — nothing to scan

        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(viewModels, "MainViewModel*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (Forbidden.Any(f => lines[i].Contains(f, StringComparison.Ordinal)))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The line-based check above misses a literal inside a switch expression, because the assignment
    /// and the strings are on different lines. That is exactly how seven section descriptions
    /// ("Send · ETH transfers sign locally…") stayed hardcoded English: the line reads
    /// "StatusMessage = section switch", with the sentences underneath it.
    /// </summary>
    [Fact]
    public void A_switch_assigned_to_the_status_line_contains_no_literal_sentences()
    {
        var viewModels = FindViewModelsDirectory();
        if (viewModels is null) return;

        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(viewModels, "MainViewModel*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // A switch EXPRESSION opens with the line ending in "switch". Merely containing the
                // word matches an unrelated key such as "status.switchedTo".
                if (!lines[i].Contains("StatusMessage =", StringComparison.Ordinal) ||
                    !lines[i].TrimEnd().EndsWith("switch", StringComparison.Ordinal)) continue;

                // Walk the arms until the switch closes.
                for (var j = i + 1; j < lines.Length; j++)
                {
                    if (lines[j].Trim() == "};") break;
                    // An arm whose result is a literal, e.g.  "Send" => "Send · ETH transfers…"
                    var trimmed = lines[j].Trim();
                    var arrow = trimmed.IndexOf("=>", StringComparison.Ordinal);
                    if (arrow < 0) continue;
                    var rhs = trimmed[(arrow + 2)..].TrimStart();
                    if (rhs.StartsWith("\"", StringComparison.Ordinal) ||
                        rhs.StartsWith("$\"", StringComparison.Ordinal))
                        offenders.Add($"{Path.GetFileName(file)}:{j + 1}: {lines[j].Trim()}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Literals that are deliberately the same in every language, so a translation table entry would add
    /// nothing: the brand, chain proper nouns, chart range codes, and a technical address placeholder.
    /// </summary>
    private static readonly HashSet<string> AllowedLiterals = new(StringComparer.Ordinal)
    {
        "UMBRELLA WALLET", "the fear",          // brand
        "Bitcoin", "Ethereum", "Solana",        // chain names — proper nouns
        "1H", "7D", "24H", "30D", "1Y",         // chart ranges
        "socks5://127.0.0.1:9050",              // proxy address placeholder
        "node.example.com:18089",               // Monero node address placeholder
    };

    /// <summary>
    /// The companion to the view-model check above, for the XAML. The strings the user actually reads are
    /// mostly in the views, and the view-model scan could never see them — which is exactly how "Coin
    /// control", "All", "None" and "Loading coins…" sat hardcoded in the SEND screen while this suite
    /// stayed green. Any new literal must either come from <c>Loc</c> or be justified in
    /// <see cref="AllowedLiterals"/>.
    /// </summary>
    [Fact]
    public void The_views_have_no_hardcoded_user_facing_strings()
    {
        var views = FindViewsDirectory();
        if (views is null) return; // not run from a source checkout — nothing to scan

        // Text/Content/Watermark/ToolTip assigned a literal (a binding starts with '{', so it is excluded
        // by the character class) containing at least one letter.
        var literal = new System.Text.RegularExpressions.Regex(
            "(?:Text|Content|Watermark|ToolTip\\.Tip)=\"([^\"{}]*[A-Za-z][^\"{}]*)\"");

        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(views, "*.axaml"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (System.Text.RegularExpressions.Match m in literal.Matches(lines[i]))
                {
                    var value = m.Groups[1].Value.Trim();
                    if (AllowedLiterals.Contains(value)) continue;
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: \"{value}\"");
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static string? FindViewsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Umbrella.Wallet.App", "Views");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static string? FindViewModelsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Umbrella.Wallet.App", "ViewModels");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
