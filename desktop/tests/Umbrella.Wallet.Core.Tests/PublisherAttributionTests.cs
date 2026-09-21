using System.Reflection;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The build has to say whose it is.
///
/// Users of a self-custody wallet are told — correctly — to check what they downloaded. That check
/// only means something if the real build states its publisher somewhere the user can read it: the
/// file properties, the installer, the About screen. A look-alike that keeps the name "Umbrella" and
/// changes the money path is the attack the LICENSE and TRADEMARK_POLICY exist to make actionable,
/// and attribution that lives only in a markdown file is attribution the user never sees.
///
/// It is also a licence term (LICENSE §2(e): attribution may not be removed), so this pins it rather
/// than trusting that nobody trims the csproj metadata during a refactor.
/// </summary>
public sealed class PublisherAttributionTests
{
    private static readonly Assembly App = typeof(MainViewModel).Assembly;

    [Fact]
    public void The_assembly_names_the_fear_as_publisher()
    {
        var company = App.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
        Assert.Equal("the fear", company);

        // The view model reads the same metadata rather than hardcoding a second copy that could drift.
        Assert.Equal(company, MainViewModel.Publisher);
    }

    [Fact]
    public void The_assembly_carries_the_copyright_line()
    {
        var copyright = App.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
        Assert.Contains("the fear", copyright);
        Assert.Contains("thefear078", copyright);
    }

    [Fact]
    public void The_product_is_still_umbrella_wallet()
    {
        var product = App.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
        Assert.Equal("Umbrella Wallet", product);
    }

    /// <summary>
    /// The welcome screen's maker's mark is the one piece of attribution a user sees before they
    /// have a wallet at all — the moment they are deciding whether this build is the real one.
    /// </summary>
    [Fact]
    public void The_welcome_screen_still_carries_the_makers_mark()
    {
        var xaml = File.ReadAllText(Path.Combine(
            RepoRoot(), "desktop", "src", "Umbrella.Wallet.App", "Views", "MainWindow.axaml"));

        Assert.Contains("the fear", xaml, StringComparison.Ordinal);
        Assert.Contains("FearMark", xaml, StringComparison.Ordinal);

        // And Settings carries the About card, which states the publisher from assembly metadata
        // rather than from a literal somebody could edit in one place and forget in the other.
        Assert.Contains("AboutPublisherLine", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// The project's GitHub account was renamed (kiurakku → thefear078, matching the brand and the
    /// TikTok handle). GitHub redirects the old URLs, which is exactly why a stale one survives
    /// unnoticed: every link still works, and every link is wrong — including the ones the app opens
    /// from the About card and the first-run screen, where "is this the real project?" is the
    /// question being asked.
    /// </summary>
    [Fact]
    public void No_file_still_points_at_the_old_account_name()
    {
        var root = RepoRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.Split(Path.DirectorySeparatorChar)
                .Any(part => part is ".git" or "bin" or "obj" or "node_modules")) continue;

            // This file is ABOUT the old name, so it says it out loud. Named rather than pattern-
            // matched around, so the exception is visible instead of hidden in a cleverer rule.
            if (relative.EndsWith("PublisherAttributionTests.cs", StringComparison.Ordinal)) continue;

            string text;
            try { text = File.ReadAllText(file); }
            catch { continue; }   // binary or locked: not a place a URL hides

            if (text.Contains("kiurakku", StringComparison.OrdinalIgnoreCase)) offenders.Add(relative);
        }

        Assert.Empty(offenders);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "desktop", "src"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root not found from " + AppContext.BaseDirectory);
    }
}
