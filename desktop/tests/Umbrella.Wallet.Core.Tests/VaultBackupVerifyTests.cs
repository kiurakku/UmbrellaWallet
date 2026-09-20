using System.Text.Json;
using System.Text.Json.Nodes;
using Umbrella.Wallet.Core.Seed;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// A backup you cannot restore is worthless, so verification must actually decrypt it. These tests
/// prove a good backup verifies and that a wrong password, a tampered vault, and a non-backup file
/// all fail — the whole point being to catch a bad backup BEFORE the user needs it.
/// </summary>
public sealed class VaultBackupVerifyTests
{
    private const string Mnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string Password = "correct horse battery staple"; // ≥ 12 chars

    private static string Temp() => Path.Combine(Path.GetTempPath(), $"umbrella-bkptest-{Guid.NewGuid():N}");

    /// <summary>A real vault, as bytes — the file is binary since the deniable format arrived, so a
    /// backup carrying it as text would corrupt it on the way through UTF-8.</summary>
    private static async Task<byte[]> MakeVaultBytesAsync()
    {
        var vaultPath = Temp() + ".vault";
        try
        {
            await new EncryptedFileSeedVault(vaultPath).CreateAsync(Mnemonic, Password);
            return await File.ReadAllBytesAsync(vaultPath);
        }
        finally
        {
            try { if (File.Exists(vaultPath)) File.Delete(vaultPath); } catch { }
        }
    }

    /// <summary>The v1 JSON envelope, built the way the previous version of the wallet built it —
    /// so the legacy-backup test exercises the real thing rather than a stand-in.</summary>
    private static string LegacyVaultJson()
    {
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        var plaintext = System.Text.Encoding.UTF8.GetBytes(
            Mnemonic.Normalize(System.Text.NormalizationForm.FormKD).Trim());
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var argon2 = new Konscious.Security.Cryptography.Argon2id(
            System.Text.Encoding.UTF8.GetBytes(Password.Normalize(System.Text.NormalizationForm.FormKC)))
        {
            Salt = salt,
            DegreeOfParallelism = 2,
            Iterations = 4,
            MemorySize = 64 * 1024,
        };
        var key = argon2.GetBytes(32);
        using var aes = new System.Security.Cryptography.AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag,
            System.Text.Encoding.UTF8.GetBytes("UmbrellaWalletVault:v1"));

        return JsonSerializer.Serialize(new
        {
            version = 1,
            salt = Convert.ToBase64String(salt),
            nonce = Convert.ToBase64String(nonce),
            ciphertext = Convert.ToBase64String(ciphertext),
            tag = Convert.ToBase64String(tag),
            memorySizeKb = 64 * 1024,
            iterations = 4,
            parallelism = 2,
        });
    }

    /// <summary>A backup in the OLD shape: the legacy JSON vault as text under "vault".</summary>
    private static string WriteLegacyBackup(string vaultJson)
    {
        var bundle = new Dictionary<string, string?>
        {
            ["magic"] = "umbrella-backup-v1",
            ["exportedUtc"] = DateTime.UtcNow.ToString("O"),
            ["vault"] = vaultJson,
        };
        var path = Temp() + ".json";
        File.WriteAllText(path, JsonSerializer.Serialize(bundle));
        return path;
    }

    private static string WriteBackup(
        byte[]? vault, bool watch = false, bool exchanges = false, string magic = "umbrella-backup-v1")
    {
        var bundle = new Dictionary<string, string?>
        {
            ["magic"] = magic,
            ["exportedUtc"] = DateTime.UtcNow.ToString("O"),
            ["vaultBase64"] = vault is null ? null : Convert.ToBase64String(vault),
            ["watchAddresses"] = watch ? "[{\"address\":\"bc1qexample\"}]" : null,
            ["exchanges"] = exchanges ? Convert.ToBase64String(new byte[] { 1, 2, 3 }) : null,
        };
        var path = Temp() + ".json";
        File.WriteAllText(path, JsonSerializer.Serialize(bundle));
        return path;
    }

    private static void Cleanup(params string[] paths)
    {
        foreach (var p in paths)
            try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    [Fact]
    public async Task A_good_backup_verifies_and_reports_its_contents()
    {
        var backup = WriteBackup(await MakeVaultBytesAsync(), watch: true, exchanges: true);
        try
        {
            var result = await VaultBackup.VerifyAsync(backup, Password);

            Assert.True(result.Ok, result.Message);
            Assert.NotNull(result.ExportedUtc);
            Assert.True(result.HasWatchAddresses);
            Assert.True(result.HasExchanges);
            // The message must never contain the seed.
            Assert.DoesNotContain("abandon", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(backup); }
    }

    [Fact]
    public async Task A_wrong_password_fails_verification()
    {
        var backup = WriteBackup(await MakeVaultBytesAsync());
        try
        {
            var result = await VaultBackup.VerifyAsync(backup, "totally wrong password");
            Assert.False(result.Ok);
            Assert.Contains("decrypt", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(backup); }
    }

    [Fact]
    public async Task A_tampered_vault_is_caught_by_the_auth_tag()
    {
        // Flip a byte inside each slot's ciphertext: AES-GCM authentication must reject both, so the
        // file stays well-formed and still opens nothing — the case worth testing is a backup that
        // looks fine and is not.
        var vault = await MakeVaultBytesAsync();
        vault[^1] ^= 0xFF;
        vault[^(DeniableVaultFormat.FileSize / 2)] ^= 0xFF;
        var backup = WriteBackup(vault);
        try
        {
            var result = await VaultBackup.VerifyAsync(backup, Password);
            Assert.False(result.Ok);
        }
        finally { Cleanup(backup); }
    }

    [Fact]
    public async Task A_file_that_is_not_a_backup_is_rejected()
    {
        var notBackup = WriteBackup(await MakeVaultBytesAsync(), magic: "something-else");
        try
        {
            var result = await VaultBackup.VerifyAsync(notBackup, Password);
            Assert.False(result.Ok);
            Assert.Contains("not an Umbrella backup", result.Message);
        }
        finally { Cleanup(notBackup); }
    }

    [Fact]
    public async Task A_backup_without_a_vault_is_rejected()
    {
        var noVault = WriteBackup(vault: null);
        try
        {
            var result = await VaultBackup.VerifyAsync(noVault, Password);
            Assert.False(result.Ok);
            Assert.Contains("does not contain a vault", result.Message);
        }
        finally { Cleanup(noVault); }
    }

    [Fact]
    public async Task A_missing_file_is_rejected()
    {
        var result = await VaultBackup.VerifyAsync(Temp() + ".json", Password);
        Assert.False(result.Ok);
        Assert.Contains("does not exist", result.Message);
    }

    /// <summary>
    /// A backup taken before the vault format changed still verifies. People restore backups years
    /// after making them — that is what a backup is — and one that quietly stopped being restorable
    /// would be discovered at the worst possible moment.
    /// </summary>
    [Fact]
    public async Task A_backup_made_before_the_format_changed_still_verifies()
    {
        var backup = WriteLegacyBackup(LegacyVaultJson());
        try
        {
            var result = await VaultBackup.VerifyAsync(backup, Password);
            Assert.True(result.Ok, result.Message);
        }
        finally { Cleanup(backup); }
    }
}
