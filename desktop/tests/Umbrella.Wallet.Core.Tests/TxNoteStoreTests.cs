using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Private transaction notes are the user's own bookkeeping and can link an on-chain payment to a real
/// identity, so they must be encrypted at rest and fail closed. These pin: a clean round-trip, that a
/// wrong seed can't read them, that a tampered file reads as empty (never a crash), and that clearing a
/// note removes it.
/// </summary>
public sealed class TxNoteStoreTests : IDisposable
{
    private const string Mnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private readonly string _dir = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"umbrella-notes-{Guid.NewGuid():N}");

    private string At(string name) => System.IO.Path.Combine(_dir, name);

    [Fact]
    public async Task Round_trips_notes_for_the_right_seed()
    {
        var store = new TxNoteStore(path: At("a.bin"));
        var notes = new Dictionary<string, string>
        {
            ["txid-1"] = "salary from Acme",
            ["txid-2"] = "sold the bike",
        };

        await store.SaveAsync(notes, Mnemonic);
        var loaded = await new TxNoteStore(path: At("a.bin")).LoadAsync(Mnemonic);

        Assert.Equal(2, loaded.Count);
        Assert.Equal("salary from Acme", loaded["txid-1"]);
        Assert.Equal("sold the bike", loaded["txid-2"]);
    }

    [Fact]
    public async Task Is_encrypted_on_disk_not_plaintext()
    {
        var store = new TxNoteStore(path: At("b.bin"));
        await store.SaveAsync(new Dictionary<string, string> { ["t"] = "TOP-SECRET-NOTE" }, Mnemonic);

        var bytes = await File.ReadAllBytesAsync(At("b.bin"));
        var asText = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("TOP-SECRET-NOTE", asText); // ciphertext, not the note in the clear
    }

    [Fact]
    public async Task A_wrong_seed_cannot_read_the_notes()
    {
        var store = new TxNoteStore(path: At("c.bin"));
        await store.SaveAsync(new Dictionary<string, string> { ["t"] = "private" }, Mnemonic);

        const string otherSeed =
            "legal winner thank year wave sausage worth useful legal winner thank yellow";
        var loaded = await new TxNoteStore(path: At("c.bin")).LoadAsync(otherSeed);

        Assert.Empty(loaded); // fail closed — no exception, no leak
    }

    [Fact]
    public async Task A_tampered_file_reads_as_empty_not_a_crash()
    {
        var store = new TxNoteStore(path: At("d.bin"));
        await store.SaveAsync(new Dictionary<string, string> { ["t"] = "note" }, Mnemonic);

        var bytes = await File.ReadAllBytesAsync(At("d.bin"));
        bytes[^1] ^= 0xFF; // flip a ciphertext byte → GCM tag check must fail
        await File.WriteAllBytesAsync(At("d.bin"), bytes);

        var loaded = await new TxNoteStore(path: At("d.bin")).LoadAsync(Mnemonic);
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task Clearing_a_note_removes_it()
    {
        var store = new TxNoteStore(path: At("e.bin"));
        await store.SaveAsync(new Dictionary<string, string> { ["t1"] = "keep", ["t2"] = "  " }, Mnemonic);

        var loaded = await new TxNoteStore(path: At("e.bin")).LoadAsync(Mnemonic);
        Assert.True(loaded.ContainsKey("t1"));
        Assert.False(loaded.ContainsKey("t2")); // blank/whitespace note is dropped, not stored
    }

    [Fact]
    public async Task Missing_file_is_no_notes()
    {
        var loaded = await new TxNoteStore(path: At("nope.bin")).LoadAsync(Mnemonic);
        Assert.Empty(loaded);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }
}
