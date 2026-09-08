using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

public sealed class EncryptedFileSeedVaultTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"umbrella-wallet-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateAndUnlock_RoundTripsMnemonic()
    {
        var path = Path.Combine(_directory, "vault.json");
        var vault = new EncryptedFileSeedVault(path);
        const string phrase =
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

        await vault.CreateAsync(phrase, "correct horse battery staple");
        var unlocked = await vault.UnlockAsync("correct horse battery staple");

        Assert.True(vault.Exists);
        Assert.Equal(phrase, unlocked);
        Assert.DoesNotContain("abandon", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Unlock_WithWrongPassword_FailsClosed()
    {
        var vault = new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json"));
        await vault.CreateAsync(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
            "correct horse battery staple");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => vault.UnlockAsync("this password is incorrect"));
    }

    [Fact]
    public async Task Unlock_WithOutOfRangeKdfParams_IsRejectedBeforeDerivation()
    {
        // A tampered / foreign vault that asks for an absurd amount of Argon2 memory must be
        // refused up front rather than being fed to the KDF and hanging/OOM-ing the app.
        var path = Path.Combine(_directory, "vault.json");
        Directory.CreateDirectory(_directory);
        const string b64 = "AAAAAAAAAAAAAAAAAAAAAA==";
        var poisoned =
            "{" +
            "\"version\":1," +
            $"\"salt\":\"{b64}\"," +
            $"\"nonce\":\"{b64}\"," +
            $"\"ciphertext\":\"{b64}\"," +
            $"\"tag\":\"{b64}\"," +
            "\"memorySizeKb\":5000000," +   // ~5 GiB — well over the 1 GiB cap
            "\"iterations\":4," +
            "\"parallelism\":2" +
            "}";
        await File.WriteAllTextAsync(path, poisoned);

        var vault = new EncryptedFileSeedVault(path);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => vault.UnlockAsync("correct horse battery staple"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
