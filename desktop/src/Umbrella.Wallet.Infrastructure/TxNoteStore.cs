using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// Encrypted-at-rest private notes on transactions — the user's own bookkeeping ("salary", "sold the
/// car", "coffee"), keyed by transaction id. Unlike the address book (which holds only public
/// addresses), a note can quietly link an on-chain payment to a real-world identity, so it is NEVER
/// written in the clear: notes are AES-256-GCM encrypted with a key derived from the wallet seed —
/// readable exactly while the wallet is unlocked, and unreadable to anything that copies the file.
///
/// Mirrors <see cref="ExchangeCredentialStore"/>: same crypto, same fail-closed handling (a corrupt or
/// foreign file reads as "no notes", never a crash). Per wallet, so each wallet's notes are separate and
/// each is sealed under its own seed-derived key.
/// </summary>
public sealed class TxNoteStore
{
    private const string DerivationDomain = "umbrella-tx-notes-v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _path;

    public TxNoteStore(string? walletId = null, string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataRoot, FileName(walletId));
    }

    /// <summary>Per-wallet file so a second wallet's notes never overwrite the first's.</summary>
    private static string FileName(string? walletId)
    {
        var id = string.IsNullOrWhiteSpace(walletId) ? "default" : walletId;
        // Keep the filename filesystem-safe regardless of the wallet id.
        var safe = new string(id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return $"tx-notes-{safe}.bin";
    }

    public bool Exists => File.Exists(_path);

    /// <summary>Decrypts the notes for this wallet: a map of transaction id → note text.</summary>
    public async Task<Dictionary<string, string>> LoadAsync(string mnemonic, CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return new();

        try
        {
            var blob = await File.ReadAllBytesAsync(_path, ct);
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
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch
        {
            // Wrong seed, tampered tag, or a foreign/corrupt file → treat as "no notes", never crash.
            return new();
        }
    }

    /// <summary>Encrypts and persists the whole note map for this wallet (atomic replace).</summary>
    public async Task SaveAsync(
        IReadOnlyDictionary<string, string> notes, string mnemonic, CancellationToken ct = default)
    {
        // Drop empties so clearing a note removes it rather than storing a blank.
        var trimmed = notes
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim());

        var json = JsonSerializer.Serialize(trimmed);
        var plaintext = Encoding.UTF8.GetBytes(json);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

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
        Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, blob, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, blob, nonce.Length + tag.Length, ciphertext.Length);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = $"{_path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temp, blob, ct);
        File.Move(temp, _path, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static byte[] DeriveKey(string mnemonic) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(DerivationDomain + ":" + mnemonic));
}
