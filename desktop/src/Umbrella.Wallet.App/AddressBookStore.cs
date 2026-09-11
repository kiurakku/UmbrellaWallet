using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>One saved destination: a label the user chose, the address, and which chain it is on.</summary>
public sealed record AddressBookEntry(string Label, string Address, string Chain);

/// <summary>
/// The saved Send destinations, encrypted at rest.
///
/// This file used to be plain JSON, and it is the single most revealing thing the wallet stores after
/// the seed itself. A vault file tells an attacker that somebody owns crypto. An address book tells
/// them WHO that person pays, under labels the user wrote in their own words — "landlord", "mum",
/// "the lawyer" — tied to addresses that can be looked up on a public chain. It survived a wallet
/// delete until 4.7, and it sat in the clear the whole time.
///
/// So it is now sealed the same way private transaction notes are: AES-256-GCM under a key derived
/// from the wallet seed, readable exactly while the wallet is unlocked and meaningless to anything
/// that copies the file. Same crypto, same fail-closed handling — a corrupt or foreign file reads as
/// an empty book, never a crash.
///
/// An existing plaintext book is migrated on the first unlock and the plaintext is then deleted. The
/// migration is one-way on purpose: there is no path back to the readable file.
/// </summary>
public sealed class AddressBookStore
{
    private const string DerivationDomain = "umbrella-address-book-v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _path;
    private readonly string _legacyPath;

    public AddressBookStore(string? path = null, string? legacyPath = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataRoot, "address-book.bin");
        _legacyPath = legacyPath ?? Path.Combine(AppPaths.DataRoot, "address-book.json");
    }

    /// <summary>True while a readable copy still exists on disk — used only by the migration test.</summary>
    public bool HasLegacyPlaintext => File.Exists(_legacyPath);

    /// <summary>
    /// Decrypts the book. A wrong seed, a corrupt file or no file at all all read as "no saved
    /// addresses" rather than throwing: this is a convenience list, and losing it must never be able
    /// to stop somebody opening their wallet.
    /// </summary>
    public List<AddressBookEntry> Load(string mnemonic)
    {
        MigrateLegacy(mnemonic);
        return LoadSealed(mnemonic);
    }

    /// <summary>
    /// Reads the encrypted file and nothing else.
    ///
    /// Separate from <see cref="Load"/> because the migration needs to read the existing sealed book
    /// before merging into it — and calling Load from the migration made the two call each other until
    /// the stack ran out. The test caught it on the first run.
    /// </summary>
    private List<AddressBookEntry> LoadSealed(string mnemonic)
    {
        if (!File.Exists(_path)) return new();

        try
        {
            var blob = File.ReadAllBytes(_path);
            if (blob.Length < NonceSize + TagSize) return new();

            var nonce = blob[..NonceSize];
            var tag = blob[NonceSize..(NonceSize + TagSize)];
            var ciphertext = blob[(NonceSize + TagSize)..];
            var plaintext = new byte[ciphertext.Length];

            var key = DeriveKey(mnemonic);
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                var json = Encoding.UTF8.GetString(plaintext);
                return JsonSerializer.Deserialize<List<AddressBookEntry>>(json) ?? new();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch
        {
            // Wrong seed, truncated file, or somebody else's book — all "no entries".
            return new();
        }
    }

    public void Save(string mnemonic, IEnumerable<AddressBookEntry> entries)
    {
        try
        {
            var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entries.ToList()));
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            var ciphertext = new byte[plaintext.Length];

            var key = DeriveKey(mnemonic);
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Encrypt(nonce, plaintext, ciphertext, tag);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
            }

            var blob = new byte[nonce.Length + tag.Length + ciphertext.Length];
            nonce.CopyTo(blob, 0);
            tag.CopyTo(blob, nonce.Length);
            ciphertext.CopyTo(blob, nonce.Length + tag.Length);

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(_path, blob);
        }
        catch
        {
            // non-fatal: the book just won't persist this time
        }
    }

    /// <summary>
    /// Turns an existing plaintext book into the encrypted one, then removes the readable copy.
    ///
    /// Runs on every load rather than once, because the plaintext file is what matters: if a write
    /// failed last time it is still sitting there, and trying again costs nothing. The plaintext is
    /// deleted only after the encrypted file is confirmed written — losing the book to a failed
    /// migration would be a worse outcome than a few more seconds in the clear.
    /// </summary>
    private void MigrateLegacy(string mnemonic)
    {
        if (!File.Exists(_legacyPath)) return;

        try
        {
            var legacy = JsonSerializer.Deserialize<List<AddressBookEntry>>(
                File.ReadAllText(_legacyPath)) ?? new();

            // Merge rather than overwrite: an encrypted book may already exist if a previous migration
            // wrote it and then failed to delete the plaintext. LoadSealed, not Load — Load would come
            // straight back here.
            var merged = LoadSealed(mnemonic);
            foreach (var entry in legacy)
            {
                if (!merged.Any(e => e.Address.Equals(entry.Address, StringComparison.OrdinalIgnoreCase)
                                     && e.Chain.Equals(entry.Chain, StringComparison.OrdinalIgnoreCase)))
                {
                    merged.Add(entry);
                }
            }

            Save(mnemonic, merged);

            // Only now, and only if the sealed copy really landed.
            if (File.Exists(_path)) File.Delete(_legacyPath);
        }
        catch
        {
            // A failed migration leaves the plaintext alone and tries again next unlock. Deleting it
            // on a failure path is the one thing that could lose the book outright.
        }
    }

    private static byte[] DeriveKey(string mnemonic) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(DerivationDomain + ":" + mnemonic));
}
