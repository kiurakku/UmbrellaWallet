using System.Reflection;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// A coin badge either has a real logo or it has a letter glyph. What it must never have is a
/// registration with no file behind it: <c>CoinBadge.Logo</c> then returns null while
/// <c>HasLogo</c> says true, the disc behind it is set to transparent on that basis, and the row
/// renders an empty square where a brand mark should be.
///
/// That is not hypothetical — it is why Zcash shipped drawing a letter beside eleven real logos, and
/// why the note in the code said "do not add to the list without a PNG". A note is not a mechanism.
/// </summary>
public sealed class CoinLogoAssetTests
{
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

    private static string CoinsFolder() =>
        Path.Combine(RepoRoot(), "desktop", "src", "Umbrella.Wallet.App", "Assets", "coins");

    /// <summary>The registry, read from the private set the badge actually consults.</summary>
    private static IReadOnlyCollection<string> RegisteredSymbols()
    {
        var field = typeof(CoinBadge).GetField("LogoSymbols", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var set = field!.GetValue(null) as HashSet<string>;
        Assert.NotNull(set);
        return set!;
    }

    [Fact]
    public void Every_registered_logo_has_a_file()
    {
        var folder = CoinsFolder();
        var missing = RegisteredSymbols()
            .Where(s => !File.Exists(Path.Combine(folder, s.ToUpperInvariant() + ".png")))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_logo_file_is_registered()
    {
        // The other direction: a file nobody looks up is dead weight in the binary, and usually means
        // a symbol was renamed and the registry was not.
        var registered = new HashSet<string>(RegisteredSymbols(), StringComparer.OrdinalIgnoreCase);
        var orphans = Directory.GetFiles(CoinsFolder(), "*.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && !registered.Contains(name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(orphans);
    }

    /// <summary>
    /// The coins the wallet actually derives an address for are the ones a user sees most, so a
    /// missing brand mark there is the most visible. Not every supported chain has to have a logo —
    /// this reports the state rather than forcing it — but the two Zcash-shaped surprises (a chain
    /// added without one) show up as a failure the moment somebody adds a chain with a logo file and
    /// forgets the registration, and as a green test otherwise.
    /// </summary>
    [Fact]
    public void A_supported_chain_with_a_logo_file_is_always_registered()
    {
        var folder = CoinsFolder();
        var unregistered = ChainCatalog.Supported
            .Where(c => File.Exists(Path.Combine(folder, c.Symbol.ToUpperInvariant() + ".png")))
            .Where(c => !CoinBadge.HasLogo(c.Symbol))
            .Select(c => c.Symbol)
            .ToList();

        Assert.Empty(unregistered);
    }
}
