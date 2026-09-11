using System.Text.RegularExpressions;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// "Delete everything" has to actually delete everything.
///
/// It did not. The address book — the list of people the user transacts with, written in plain text —
/// survived, along with their private transaction notes and the count of addresses ever issued. A
/// person deletes their wallet believing it is gone; what remains on disk is precisely the part that
/// names who they were dealing with. For a wallet whose whole premise is somebody under pressure,
/// that is the most consequential thing the wiper could get wrong.
///
/// It happened the ordinary way: files were added to the app over time and nobody went back to the
/// wiper. So this test scans the source for every path written under the data root and fails if the
/// wiper does not handle it. A new store cannot be added without either being wiped or being named
/// here as a deliberate exception.
/// </summary>
public sealed class DataWiperCoverageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"umbrella-wipe-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Paths under the data root that are deliberately NOT wiped, with the reason. Both are bundled
    /// programs rather than user data: deleting them would leave the wallet unable to start Tor or
    /// Monero, and a reinstall would be needed to use it again.
    /// </summary>
    private static readonly HashSet<string> NotUserData = new(StringComparer.OrdinalIgnoreCase)
    {
        "tor",       // the bundled Tor client shipped with the build
    };

    private static string? FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "desktop", "src");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>Every literal written under AppPaths.DataRoot anywhere in the app.</summary>
    private static List<string> PathsWrittenUnderDataRoot(string sourceRoot)
    {
        var pattern = new Regex(@"DataRoot,\s*""([^""]+)""", RegexOptions.Compiled);
        var found = new List<string>();

        foreach (var file in Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            foreach (Match m in pattern.Matches(File.ReadAllText(file)))
                found.Add(m.Groups[1].Value);
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [Fact]
    public void Everything_the_wallet_writes_is_either_wiped_or_a_named_exception()
    {
        var sourceRoot = FindSourceRoot();
        if (sourceRoot is null) return; // not a source checkout

        var wiperSource = File.ReadAllText(
            Path.Combine(sourceRoot, "Umbrella.Wallet.Infrastructure", "DataWiper.cs"));

        var unhandled = PathsWrittenUnderDataRoot(sourceRoot)
            .Where(p => !NotUserData.Contains(p))
            .Where(p => !wiperSource.Contains($"\"{p}\"", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(unhandled);
    }

    // --- behaviour, against a real directory --------------------------------------------------------

    private void Seed(params string[] names)
    {
        Directory.CreateDirectory(_root);
        foreach (var n in names) File.WriteAllText(Path.Combine(_root, n), "x");
    }

    [Fact]
    public void The_address_book_and_the_private_notes_are_gone_afterwards()
    {
        // The two that matter most: one names the user's counterparties, the other is what they wrote
        // about their own money.
        Seed("vault.json", "address-book.json", "tx-notes-main.bin", "tx-notes-second.bin");

        DataWiper.WipeAll(_root);

        Assert.False(File.Exists(Path.Combine(_root, "address-book.json")));
        Assert.False(File.Exists(Path.Combine(_root, "tx-notes-main.bin")));
        Assert.False(File.Exists(Path.Combine(_root, "tx-notes-second.bin")));
        Assert.False(File.Exists(Path.Combine(_root, "vault.json")));
    }

    [Fact]
    public void Notes_for_every_wallet_go_not_just_the_main_one()
    {
        // Per-wallet files cannot be named individually, so they are matched by pattern — and a
        // pattern that only caught the first wallet would leave every other one behind.
        Seed("tx-notes-main.bin", "tx-notes-abc123.bin", "tx-notes-.bin");

        DataWiper.WipeAll(_root);

        Assert.Empty(Directory.GetFiles(_root, "tx-notes-*.bin"));
    }

    [Fact]
    public void The_issued_address_count_and_the_price_cache_go_too()
    {
        Seed("addr-indexes.json", "market.json");
        DataWiper.WipeAll(_root);

        Assert.False(File.Exists(Path.Combine(_root, "addr-indexes.json")));
        Assert.False(File.Exists(Path.Combine(_root, "market.json")));
    }

    [Fact]
    public void The_bundled_tor_client_is_left_alone()
    {
        // Deleting it would leave the wallet unable to start Tor at all, and a reinstall needed to fix
        // it. It is a program that shipped with the build, not something the user created.
        Directory.CreateDirectory(Path.Combine(_root, "tor"));
        File.WriteAllText(Path.Combine(_root, "tor", "tor.exe"), "binary");

        DataWiper.WipeAll(_root);

        Assert.True(File.Exists(Path.Combine(_root, "tor", "tor.exe")));
    }

    [Fact]
    public void Wiping_an_empty_directory_reports_nothing_removed_rather_than_failing()
    {
        Directory.CreateDirectory(_root);
        var result = DataWiper.WipeAll(_root);

        Assert.Equal(0, result.Removed);
        Assert.Empty(result.Failed);
    }

    [Fact]
    public void A_directory_that_does_not_exist_is_survivable()
    {
        var result = DataWiper.WipeAll(Path.Combine(_root, "nope"));
        Assert.Empty(result.Failed);
    }
}
