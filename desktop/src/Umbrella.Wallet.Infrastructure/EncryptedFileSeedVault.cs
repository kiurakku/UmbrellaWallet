using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;
using Umbrella.Wallet.Core.Seed;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// Password-encrypted, local-only seed storage. The mnemonic is never logged or transmitted.
///
/// Two formats live here. The one written today is <see cref="DeniableVaultFormat"/>: a fixed-size
/// binary file with two slots, the unused one full of random bytes, so the file cannot be used to
/// prove whether a second wallet exists (roadmap P1.1 — the storage half of a duress password). The
/// older JSON envelope is still READ, and rewritten to the new shape the first time its password
/// opens it — a wallet that only gained the new format when the user asked for a duress password
/// would announce, by its own file size, that they had asked.
///
/// The migration is fail-closed: the replacement is written to a temporary file, opened again with
/// the same password, and compared against the seed that was just decrypted. Only an exact match
/// replaces the original. Anything else leaves the original untouched — a vault is the one file in
/// this program where "mostly worked" is indistinguishable from total loss.
/// </summary>
public sealed class EncryptedFileSeedVault
{
    private const int CurrentVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int MemorySizeKb = 64 * 1024;
    private const int Iterations = 4;
    private const int Parallelism = 2;

    private readonly string _vaultPath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public EncryptedFileSeedVault(string? vaultPath = null)
    {
        _vaultPath = vaultPath ?? GetDefaultVaultPath();
    }

    public string VaultPath => _vaultPath;

    public bool Exists => File.Exists(_vaultPath);

    public async Task CreateAsync(string mnemonic, string password, CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        if (string.IsNullOrWhiteSpace(mnemonic))
        {
            throw new ArgumentException("Seed phrase is required.", nameof(mnemonic));
        }

        // Every new vault gets the deniable shape, whether or not a duress password is ever set. A
        // file that only became two-slot when the user configured one would be the giveaway.
        var file = await Task.Run(
            () => DeniableVaultFormat.Create(mnemonic, password, DeriveKey), cancellationToken);
        await WriteAtomicAsync(file, cancellationToken);
    }

    /// <summary>
    /// Adds a DECOY wallet under a second password: whoever is made to unlock this vault with that
    /// password gets a real, working wallet that is not this one (roadmap P1.1).
    ///
    /// What it does not do is hide that other wallets exist elsewhere on this computer, or help
    /// against somebody watching the password being typed. Those limits are stated in the UI beside
    /// the feature, because a protection believed to cover more than it does is worse than none.
    /// </summary>
    public async Task AddDuressWalletAsync(
        string password, string duressMnemonic, string duressPassword,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        ValidatePassword(duressPassword);
        if (string.IsNullOrWhiteSpace(duressMnemonic))
        {
            throw new ArgumentException("Seed phrase is required.", nameof(duressMnemonic));
        }

        var file = await ReadDeniableOrMigrateAsync(password, cancellationToken);
        var updated = await Task.Run(
            () => DeniableVaultFormat.AddSecret(file, password, duressMnemonic, duressPassword, DeriveKey),
            cancellationToken);

        // Prove the real wallet still opens from the new bytes BEFORE they replace the old ones.
        await VerifyOpensAsync(updated, password, cancellationToken);
        await WriteAtomicAsync(updated, cancellationToken);
    }

    /// <summary>
    /// Puts the second slot back to noise. Called with the password of the wallet being kept, and it
    /// cannot report whether there was anything there — "there never was one" and "there was one and
    /// it is gone" are the same answer, which is what makes the feature worth having.
    /// </summary>
    public async Task RemoveDuressWalletAsync(string password, CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        var file = await ReadDeniableOrMigrateAsync(password, cancellationToken);
        var updated = await Task.Run(
            () => DeniableVaultFormat.RemoveOtherSecret(file, password, DeriveKey), cancellationToken);

        await VerifyOpensAsync(updated, password, cancellationToken);
        await WriteAtomicAsync(updated, cancellationToken);
    }

    /// <summary>The legacy JSON envelope, kept for reading and for the one-time migration.</summary>
    private async Task CreateLegacyAsync(string mnemonic, string password, CancellationToken cancellationToken)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintext = Encoding.UTF8.GetBytes(mnemonic.Normalize(NormalizationForm.FormKD).Trim());
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        byte[]? key = null;

        try
        {
            key = await DeriveKeyAsync(password, salt, MemorySizeKb, Iterations, Parallelism);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, BuildAssociatedData(CurrentVersion));

            var envelope = new VaultEnvelope(
                CurrentVersion,
                Convert.ToBase64String(salt),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(tag),
                MemorySizeKb,
                Iterations,
                Parallelism);

            var directory = Path.GetDirectoryName(_vaultPath)
                ?? throw new InvalidOperationException("Vault directory cannot be resolved.");
            Directory.CreateDirectory(directory);

            var tempPath = $"{_vaultPath}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(
                tempPath,
                JsonSerializer.Serialize(envelope, JsonOptions),
                Encoding.UTF8,
                cancellationToken);

            File.Move(tempPath, _vaultPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    public async Task<string> UnlockAsync(string password, CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        if (!Exists)
        {
            throw new FileNotFoundException("Local wallet vault does not exist.", _vaultPath);
        }

        var raw = await File.ReadAllBytesAsync(_vaultPath, cancellationToken);
        if (DeniableVaultFormat.IsWellFormed(raw))
        {
            var opened = await Task.Run(
                () =>
                {
                    var ok = DeniableVaultFormat.TryOpen(raw, password, DeriveKey, out var secret);
                    return ok ? secret : null;
                },
                cancellationToken);

            return opened
                ?? throw new UnauthorizedAccessException("Incorrect password or damaged vault.");
        }

        // Legacy JSON envelope: open it, then rewrite in the deniable shape so this wallet is not
        // distinguishable later by the format of its file.
        var mnemonic = await UnlockLegacyAsync(password, cancellationToken);
        await TryMigrateToDeniableAsync(mnemonic, password, cancellationToken);
        return mnemonic;
    }

    private async Task<string> UnlockLegacyAsync(string password, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(_vaultPath, cancellationToken);
        var envelope = JsonSerializer.Deserialize<VaultEnvelope>(json, JsonOptions)
            ?? throw new InvalidDataException("Vault envelope is invalid.");
        if (envelope.Version != CurrentVersion)
        {
            throw new NotSupportedException($"Unsupported vault version: {envelope.Version}.");
        }

        // Defence-in-depth: the KDF parameters are read from the file, so a tampered or foreign
        // vault could ask for gigabytes of memory or millions of passes and hang/OOM the app the
        // moment someone tries to unlock it. Legitimate vaults only ever use 64 MiB / t=4 / p=2, so
        // reject anything outside a sane envelope instead of feeding it to Argon2.
        if (envelope.MemorySizeKb is < 8 * 1024 or > 1024 * 1024 ||
            envelope.Iterations is < 1 or > 64 ||
            envelope.Parallelism is < 1 or > 16)
        {
            throw new InvalidDataException("Vault KDF parameters are out of the supported range.");
        }

        var salt = Convert.FromBase64String(envelope.Salt);
        var nonce = Convert.FromBase64String(envelope.Nonce);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var tag = Convert.FromBase64String(envelope.Tag);
        var plaintext = new byte[ciphertext.Length];
        byte[]? key = null;

        try
        {
            key = await DeriveKeyAsync(
                password,
                salt,
                envelope.MemorySizeKb,
                envelope.Iterations,
                envelope.Parallelism);

            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, BuildAssociatedData(envelope.Version));
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException error)
        {
            throw new UnauthorizedAccessException("Incorrect password or damaged vault.", error);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    /// <summary>
    /// Rewrites a legacy vault in the deniable format, or leaves it exactly as it was.
    ///
    /// Never fatal: a wallet that unlocked correctly must not fail to open because a background
    /// upgrade could not be written. The migration proves the new bytes give back the SAME seed
    /// before it replaces anything, so the failure modes are "migrated" or "unchanged" — never
    /// "replaced with something that does not open".
    /// </summary>
    private async Task TryMigrateToDeniableAsync(
        string mnemonic, string password, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Task.Run(
                () => DeniableVaultFormat.Create(mnemonic, password, DeriveKey), cancellationToken);

            var check = await VerifyOpensAsync(file, password, cancellationToken);
            if (!string.Equals(check, mnemonic.Normalize(NormalizationForm.FormKD).Trim(), StringComparison.Ordinal))
            {
                return;   // would not round-trip: leave the original alone
            }

            await WriteAtomicAsync(file, cancellationToken);
        }
        catch
        {
            // A read-only directory, a full disk, a locked file: the wallet still works on the old
            // format, and losing the upgrade costs nothing. Losing the vault would cost everything.
        }
    }

    /// <summary>Opens the given bytes with the given password, or throws. Used before any write
    /// replaces a working vault.</summary>
    private async Task<string> VerifyOpensAsync(
        byte[] file, string password, CancellationToken cancellationToken)
    {
        var secret = await Task.Run(
            () =>
            {
                var ok = DeniableVaultFormat.TryOpen(file, password, DeriveKey, out var value);
                return ok ? value : null;
            },
            cancellationToken);

        return secret ?? throw new InvalidOperationException(
            "The rewritten vault did not open with the password it was written for — refusing to replace the existing one.");
    }

    /// <summary>The deniable bytes for this vault, migrating a legacy file first if that is what is
    /// on disk. The password must open it, or nothing is returned.</summary>
    private async Task<byte[]> ReadDeniableOrMigrateAsync(string password, CancellationToken cancellationToken)
    {
        if (!Exists) throw new FileNotFoundException("Local wallet vault does not exist.", _vaultPath);

        var raw = await File.ReadAllBytesAsync(_vaultPath, cancellationToken);
        if (DeniableVaultFormat.IsWellFormed(raw))
        {
            // A wrong password here is the user's mistake, not a broken invariant — it has to arrive
            // as "incorrect password", the same answer every other wrong password gets.
            var opened = await Task.Run(
                () => DeniableVaultFormat.TryOpen(raw, password, DeriveKey, out var value) ? value : null,
                cancellationToken);
            if (opened is null)
            {
                throw new UnauthorizedAccessException("Incorrect password or damaged vault.");
            }

            return raw;
        }

        // Legacy: unlocking migrates it, and then the file on disk is the format we need.
        await UnlockAsync(password, cancellationToken);
        raw = await File.ReadAllBytesAsync(_vaultPath, cancellationToken);
        if (!DeniableVaultFormat.IsWellFormed(raw))
        {
            throw new InvalidOperationException(
                "This vault could not be upgraded to the format a duress password needs. Check that its folder is writable.");
        }

        return raw;
    }

    private async Task WriteAtomicAsync(byte[] file, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_vaultPath)
            ?? throw new InvalidOperationException("Vault directory cannot be resolved.");
        Directory.CreateDirectory(directory);

        var tempPath = $"{_vaultPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(tempPath, file, cancellationToken);
        File.Move(tempPath, _vaultPath, overwrite: true);
    }

    /// <summary>The wallet's real KDF, in the synchronous shape the vault format asks for.</summary>
    private static byte[] DeriveKey(string password, byte[] salt) =>
        DeriveKeyAsync(password, salt, MemorySizeKb, Iterations, Parallelism).GetAwaiter().GetResult();

    public void Delete()
    {
        if (Exists)
        {
            File.Delete(_vaultPath);
        }
    }

    private static async Task<byte[]> DeriveKeyAsync(
        string password,
        byte[] salt,
        int memorySizeKb,
        int iterations,
        int parallelism)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormKC));
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = parallelism,
                Iterations = iterations,
                MemorySize = memorySizeKb,
            };
            return await argon2.GetBytesAsync(KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static byte[] BuildAssociatedData(int version) =>
        Encoding.UTF8.GetBytes($"UmbrellaWalletVault:v{version}");

    private static void ValidatePassword(string password)
    {
        if (password.Length < 12)
        {
            throw new ArgumentException("Vault password must contain at least 12 characters.", nameof(password));
        }
    }

    // Lives beside the app (see AppPaths) so the vault follows the drive it was installed to
    // instead of always landing on the system drive.
    private static string GetDefaultVaultPath() => AppPaths.VaultFile;

    private sealed record VaultEnvelope(
        int Version,
        string Salt,
        string Nonce,
        string Ciphertext,
        string Tag,
        int MemorySizeKb,
        int Iterations,
        int Parallelism);
}
