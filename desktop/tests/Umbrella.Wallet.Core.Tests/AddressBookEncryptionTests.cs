using System.Text.Json;
using Umbrella.Wallet.App;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The address book, sealed under the wallet seed.
///
/// This is the most revealing thing the wallet stores after the seed itself. A vault file tells an
/// attacker that somebody owns crypto; an address book tells them WHO that person pays, under labels
/// the user wrote in their own words — "landlord", "mum", "the lawyer" — tied to addresses anyone can
/// look up on a public chain. It was plain JSON until 4.7, and it survived a wallet delete.
///
/// The migration matters as much as the encryption. A user who already has a plaintext book gains
/// nothing from a wallet that merely starts writing a new encrypted one beside it.
/// </summary>
public sealed class AddressBookEncryptionTests : IDisposable
{
    private const string Seed =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string OtherSeed =
        "legal winner thank year wave sausage worth useful legal winner thank yellow";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"umbrella-book-{Guid.NewGuid():N}");
    private readonly string _sealed_;
    private readonly string _plain;

    public AddressBookEncryptionTests()
    {
        Directory.CreateDirectory(_dir);
        _sealed_ = Path.Combine(_dir, "address-book.bin");
        _plain = Path.Combine(_dir, "address-book.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private AddressBookStore Store() => new(_sealed_, _plain);

    private static readonly AddressBookEntry[] Book =
    [
        new("landlord", "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4", "BTC"),
        new("mum", "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045", "ETH"),
    ];

    [Fact]
    public void A_saved_book_comes_back_intact()
    {
        Store().Save(Seed, Book);
        var loaded = Store().Load(Seed);

        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, e => e.Label == "landlord" && e.Chain == "BTC");
        Assert.Contains(loaded, e => e.Label == "mum" && e.Chain == "ETH");
    }

    [Fact]
    public void The_file_on_disk_does_not_contain_the_labels_or_the_addresses()
    {
        // The whole point. Anything that copies this file gets bytes, not a list of who you pay.
        Store().Save(Seed, Book);

        var bytes = File.ReadAllBytes(_sealed_);
        var asText = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("landlord", asText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mum", asText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bc1q", asText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0xd8dA", asText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Another_seed_cannot_read_it()
    {
        // Two wallets on one machine must not see each other's contacts, and a stolen file is useless
        // without the seed that sealed it.
        Store().Save(Seed, Book);

        Assert.Empty(Store().Load(OtherSeed));
    }

    [Fact]
    public void A_corrupt_file_reads_as_an_empty_book_rather_than_throwing()
    {
        // This is a convenience list. Losing it must never be able to stop somebody opening a wallet
        // that still holds their money.
        File.WriteAllBytes(_sealed_, new byte[] { 1, 2, 3, 4, 5 });
        Assert.Empty(Store().Load(Seed));

        File.WriteAllBytes(_sealed_, Array.Empty<byte>());
        Assert.Empty(Store().Load(Seed));
    }

    // --- migration off the plaintext file -----------------------------------------------------------

    [Fact]
    public void An_existing_plaintext_book_is_migrated_and_the_readable_copy_removed()
    {
        File.WriteAllText(_plain, JsonSerializer.Serialize(Book));

        var loaded = Store().Load(Seed);

        Assert.Equal(2, loaded.Count);
        Assert.True(File.Exists(_sealed_));
        Assert.False(File.Exists(_plain));
    }

    [Fact]
    public void Migration_merges_rather_than_overwriting_an_existing_sealed_book()
    {
        // The case where a previous migration wrote the sealed file and then failed to delete the
        // plaintext: running again must not throw away whichever half is newer.
        Store().Save(Seed, [new("exchange", "0xabc0000000000000000000000000000000000001", "ETH")]);
        File.WriteAllText(_plain, JsonSerializer.Serialize(Book));

        var loaded = Store().Load(Seed);

        Assert.Equal(3, loaded.Count);
        Assert.Contains(loaded, e => e.Label == "exchange");
        Assert.Contains(loaded, e => e.Label == "landlord");
    }

    [Fact]
    public void A_corrupt_plaintext_file_leaves_the_wallet_working()
    {
        File.WriteAllText(_plain, "{ not json at all");

        var loaded = Store().Load(Seed);

        Assert.Empty(loaded);
        // The unreadable file is left alone rather than deleted on a failure path - deleting on
        // failure is the one thing that could lose a book outright.
        Assert.True(File.Exists(_plain));
    }

    [Fact]
    public void Nothing_is_left_readable_after_a_migration()
    {
        // End to end, stated as the property that actually matters to the user: start with a readable
        // file, end with nothing readable anywhere in the directory.
        File.WriteAllText(_plain, JsonSerializer.Serialize(Book));
        Store().Load(Seed);

        foreach (var file in Directory.GetFiles(_dir))
        {
            var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file));
            Assert.DoesNotContain("landlord", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void An_empty_book_saves_and_loads_without_a_special_case()
    {
        Store().Save(Seed, Array.Empty<AddressBookEntry>());
        Assert.Empty(Store().Load(Seed));
    }
}
