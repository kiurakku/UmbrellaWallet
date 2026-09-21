using System.Security.Cryptography;
using System.Text;

namespace Umbrella.Wallet.Core.Seed;

/// <summary>Derives an encryption key from a password and a salt. Injected so this format stays pure
/// and testable — the real wallet passes Argon2id, tests pass something fast.</summary>
public delegate byte[] VaultKeyDerivation(string password, byte[] salt);

/// <summary>
/// A vault file that can hold more than one wallet, where the file itself does not reveal how many it
/// actually holds.
///
/// This is the storage half of a duress password: a second password that opens a decoy wallet, so
/// somebody forced to unlock reveals only the decoy. That only works if the file cannot be used to
/// prove a second wallet exists — otherwise the coercer simply keeps asking. So:
///
/// <list type="bullet">
/// <item>The file is ALWAYS the same size, with the same number of slots, whether one wallet is stored
/// or two. Adding a second wallet later does not change the file's shape.</item>
/// <item>An unused slot is filled with random bytes. AES-GCM ciphertext is indistinguishable from
/// random, so an unused slot and a real one cannot be told apart without the password.</item>
/// <item>Nothing records which slots are in use, and the real wallet's slot is chosen at random, so
/// "the first slot is the real one" is not a usable assumption.</item>
/// <item>The plaintext is padded to a fixed length, so the ciphertext does not leak how long the
/// seed phrase is — a 12-word and a 24-word wallet look identical.</item>
/// <item>A wrong password and an unused slot produce the same outcome: it did not open. There is no
/// "slot 2 failed" to read anything from.</item>
/// </list>
///
/// <para>
/// What this does NOT defend against is anybody watching you type, a keylogger, or a backup of the
/// file taken at two different times and compared — a slot that was random and is now ciphertext
/// proves a wallet was added. Deniability is about the file at one moment, not about your whole life.
/// </para>
/// </summary>
public static class DeniableVaultFormat
{
    /// <summary>Always two, whether or not a duress wallet is configured. A count that varied would
    /// itself be the giveaway.</summary>
    public const int SlotCount = 2;

    public const int SaltSize = 16;
    public const int NonceSize = 12;
    public const int TagSize = 16;

    /// <summary>Fixed plaintext size, so a phrase's length never shows through the ciphertext.
    /// A 24-word BIP39 phrase is at most ~200 bytes.</summary>
    public const int PayloadSize = 256;

    private const int LengthPrefix = 2;
    private static readonly byte[] Magic = "UMBV"u8.ToArray();
    private const byte FormatVersion = 2;

    private const int HeaderSize = 8;                                  // magic(4) + version(1) + reserved(3)
    private const int SlotSize = SaltSize + NonceSize + TagSize + PayloadSize;
    public const int FileSize = HeaderSize + (SlotCount * SlotSize);

    /// <summary>The largest secret that fits once the length prefix is accounted for.</summary>
    public const int MaxSecretBytes = PayloadSize - LengthPrefix;

    /// <summary>
    /// Builds a fresh vault holding one secret. The other slot is filled with random bytes, which is
    /// what makes "there is no second wallet" and "there is a second wallet you cannot open"
    /// indistinguishable.
    /// </summary>
    public static byte[] Create(string secret, string password, VaultKeyDerivation kdf)
    {
        var file = RandomFile();
        var slot = RandomNumberGenerator.GetInt32(SlotCount);   // never assume slot 0 is the real one
        WriteSlot(file, slot, secret, password, kdf);
        return file;
    }

    /// <summary>
    /// Stores a second secret under a second password, in whichever slot the first one is not using.
    /// The file's size and shape do not change — only bytes that were already random become ciphertext.
    /// </summary>
    /// <exception cref="InvalidOperationException">The existing password does not open this vault.</exception>
    public static byte[] AddSecret(
        byte[] file, string existingPassword, string newSecret, string newPassword, VaultKeyDerivation kdf)
    {
        Validate(file);
        var used = FindSlot(file, existingPassword, kdf)
                   ?? throw new InvalidOperationException("The existing password does not open this vault.");

        if (TryOpen(file, newPassword, kdf, out _))
            throw new InvalidOperationException("That password already opens this vault.");

        var copy = (byte[])file.Clone();
        WriteSlot(copy, used == 0 ? 1 : 0, newSecret, newPassword, kdf);
        return copy;
    }

    /// <summary>
    /// Puts the OTHER slot back to noise — removing a second wallet without changing the file's size
    /// or shape, so "there was never one" and "there was one and it is gone" look identical.
    ///
    /// Called with the password of the wallet being KEPT. It cannot be used to find out whether a
    /// second wallet existed: the result is the same either way, which is the point.
    /// </summary>
    /// <exception cref="InvalidOperationException">The password does not open this vault.</exception>
    public static byte[] RemoveOtherSecret(byte[] file, string password, VaultKeyDerivation kdf)
    {
        Validate(file);
        var keep = FindSlot(file, password, kdf)
                   ?? throw new InvalidOperationException("That password does not open this vault.");

        var copy = (byte[])file.Clone();
        var other = keep == 0 ? 1 : 0;
        RandomNumberGenerator.GetBytes(SlotSize).CopyTo(copy.AsSpan(SlotOffset(other), SlotSize));
        return copy;
    }

    /// <summary>
    /// Opens whichever slot this password unlocks. Every slot is attempted whatever happens, so how
    /// long the call takes does not say which slot succeeded — or whether any did.
    /// </summary>
    public static bool TryOpen(byte[] file, string password, VaultKeyDerivation kdf, out string secret)
    {
        secret = string.Empty;
        if (!IsWellFormed(file)) return false;

        string? found = null;
        for (var i = 0; i < SlotCount; i++)
        {
            // No early exit: returning as soon as slot 0 matches would make a slot-1 wallet
            // measurably slower to open, which is exactly the kind of tell this format exists to avoid.
            if (TryReadSlot(file, i, password, kdf, out var value)) found ??= value;
        }

        if (found is null) return false;
        secret = found;
        return true;
    }

    /// <summary>True when the bytes are a vault of this format and version.</summary>
    public static bool IsWellFormed(byte[]? file) =>
        file is { Length: FileSize }
        && file.AsSpan(0, Magic.Length).SequenceEqual(Magic)
        && file[Magic.Length] == FormatVersion;

    // --- internals -----------------------------------------------------------------------------

    private static void Validate(byte[] file)
    {
        if (!IsWellFormed(file)) throw new ArgumentException("Not a vault file of this format.", nameof(file));
    }

    /// <summary>A file of pure noise with a valid header — the state before anything is stored.</summary>
    private static byte[] RandomFile()
    {
        var file = RandomNumberGenerator.GetBytes(FileSize);
        Magic.CopyTo(file, 0);
        file[Magic.Length] = FormatVersion;
        for (var i = Magic.Length + 1; i < HeaderSize; i++) file[i] = 0;
        return file;
    }

    private static int SlotOffset(int slot) => HeaderSize + (slot * SlotSize);

    private static void WriteSlot(byte[] file, int slot, string secret, string password, VaultKeyDerivation kdf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("A secret is required.", nameof(secret));

        var bytes = Encoding.UTF8.GetBytes(secret.Normalize(NormalizationForm.FormKD).Trim());
        if (bytes.Length > MaxSecretBytes)
            throw new ArgumentException($"A secret may be at most {MaxSecretBytes} bytes.", nameof(secret));

        // Length prefix, then the secret, then random filler — never zero padding, which would show
        // through as a long run of a single byte if the cipher were ever misused.
        var plaintext = RandomNumberGenerator.GetBytes(PayloadSize);
        plaintext[0] = (byte)(bytes.Length >> 8);
        plaintext[1] = (byte)(bytes.Length & 0xFF);
        bytes.CopyTo(plaintext, LengthPrefix);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = kdf(password, salt);
        try
        {
            var ciphertext = new byte[PayloadSize];
            var tag = new byte[TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(slot));

            var o = SlotOffset(slot);
            salt.CopyTo(file, o);
            nonce.CopyTo(file, o + SaltSize);
            tag.CopyTo(file, o + SaltSize + NonceSize);
            ciphertext.CopyTo(file, o + SaltSize + NonceSize + TagSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static bool TryReadSlot(
        byte[] file, int slot, string password, VaultKeyDerivation kdf, out string secret)
    {
        secret = string.Empty;
        var o = SlotOffset(slot);
        var salt = file.AsSpan(o, SaltSize).ToArray();
        var nonce = file.AsSpan(o + SaltSize, NonceSize);
        var tag = file.AsSpan(o + SaltSize + NonceSize, TagSize);
        var ciphertext = file.AsSpan(o + SaltSize + NonceSize + TagSize, PayloadSize);

        var key = kdf(password, salt);
        var plaintext = new byte[PayloadSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(slot));
        }
        catch (CryptographicException)
        {
            // Wrong password, or a slot holding nothing but noise. Deliberately the same answer.
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        try
        {
            var length = (plaintext[0] << 8) | plaintext[1];
            if (length <= 0 || length > MaxSecretBytes) return false;
            secret = Encoding.UTF8.GetString(plaintext, LengthPrefix, length);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static int? FindSlot(byte[] file, string password, VaultKeyDerivation kdf)
    {
        for (var i = 0; i < SlotCount; i++)
        {
            if (TryReadSlot(file, i, password, kdf, out _)) return i;
        }

        return null;
    }

    /// <summary>Binds a slot's ciphertext to its position and the format version, so a slot cannot be
    /// moved or replayed into another position and still authenticate.</summary>
    private static byte[] AssociatedData(int slot) => [.. Magic, FormatVersion, (byte)slot];
}
