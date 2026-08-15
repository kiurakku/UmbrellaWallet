using System.Text.Json;
using System.Text.Json.Nodes;
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

    private static async Task<string> MakeVaultJsonAsync()
    {
        var vaultPath = Temp() + ".vault";
        try
        {
            await new EncryptedFileSeedVault(vaultPath).CreateAsync(Mnemonic, Password);
            return await File.ReadAllTextAsync(vaultPath);
        }
        finally
        {
            try { if (File.Exists(vaultPath)) File.Delete(vaultPath); } catch { }
        }
    }

    private static string WriteBackup(string? vault, bool watch = false, bool exchanges = false, string magic = "umbrella-backup-v1")
    {
        var bundle = new Dictionary<string, string?>
        {
            ["magic"] = magic,
            ["exportedUtc"] = DateTime.UtcNow.ToString("O"),
            ["vault"] = vault,
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
        var backup = WriteBackup(await MakeVaultJsonAsync(), watch: true, exchanges: true);
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
        var backup = WriteBackup(await MakeVaultJsonAsync());
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
        // Flip one byte of the ciphertext: AES-GCM authentication must reject it.
        var vaultJson = await MakeVaultJsonAsync();
        var node = JsonNode.Parse(vaultJson)!;
        var ct = Convert.FromBase64String(node["ciphertext"]!.GetValue<string>());
        ct[0] ^= 0xFF;
        node["ciphertext"] = Convert.ToBase64String(ct);
        var backup = WriteBackup(node.ToJsonString());
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
        var notBackup = WriteBackup(await MakeVaultJsonAsync(), magic: "something-else");
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
}
