using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// Password-encrypted, local-only seed storage. The mnemonic is never logged or transmitted.
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
