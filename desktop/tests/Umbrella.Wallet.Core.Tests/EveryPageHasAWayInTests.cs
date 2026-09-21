using System.Text.RegularExpressions;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Every page in the window has a way in. The 4.5.0 redesign removed the old navigation and left three
/// working pages — Connect (watch-only and exchange balances), NFTs and Staking — with no button, no
/// palette entry and no code path that opened them. They shipped, invisible, for two weeks.
/// </summary>
public sealed class EveryPageHasAWayInTests
{
    [Fact]
    public void Every_page_can_be_reached()
    {
        var root = RepoRoot();
        if (root is null) return; // not run from a source checkout

        var app = Path.Combine(root, "desktop", "src", "Umbrella.Wallet.App");
        var view = File.ReadAllText(Path.Combine(app, "Views", "MainWindow.axaml"));
        var code = string.Join("\n", Directory.GetFiles(Path.Combine(app, "ViewModels"), "*.cs").Select(File.ReadAllText));

        var pages = Regex.Matches(view, @"Classes=""page"" IsVisible=""\{Binding Is(\w+)\}""")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(pages);

        var unreachable = pages.Where(page =>
                !view.Contains($"CommandParameter=\"{page}\"") &&            // a button
                !code.Contains($"ActiveSection = \"{page}\"") &&             // opened from code
                !code.Contains($"SelectSection(\"{page}\")") &&
                !Regex.IsMatch(code, $@"new\(""[^""]*"", ""[^""]*"", ""[^""]*"", ""{page}""\)"))   // the palette
            .ToList();

        Assert.Empty(unreachable);
    }

    private static string? RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "desktop", "src", "Umbrella.Wallet.App"))) return dir.FullName;
        return null;
    }
}
