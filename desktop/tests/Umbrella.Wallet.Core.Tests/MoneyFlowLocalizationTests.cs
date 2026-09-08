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
