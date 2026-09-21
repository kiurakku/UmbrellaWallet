using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Seed;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap P1.1 + P1.13 — the duress password, and the scenario that decides whether it is a feature
/// or a verified solution.
///
/// The situation this is for: somebody is standing over the user and the wallet is going to be
/// unlocked. What must happen is that the password they are given opens a real, working wallet that
/// is not the one holding the money — and that nothing about the file, or about what the coercer can
/// do with it, says there is another.
///
/// So the test is written the way that goes wrong rather than the way it goes right: the coercer has
/// the decoy password, full access to the file, and the wallet's own source code. They must still be
/// unable to reach the real seed, and unable to prove it is there.
/// </summary>
public sealed class DuressWalletScenarioTests : IDisposable
{
    private const string RealPassword = "umbrella-real-vault-2026";
    private const string DuressPassword = "umbrella-duress-vault-2026";

    private const string RealPhrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private const string DecoyPhrase =
        "legal winner thank year wave sausage worth useful legal winner thank yellow";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"umbrella-duress-{Guid.NewGuid():N}");

    private EncryptedFileSeedVault NewVault() =>
        new(Path.Combine(_directory, "vault.json"));

    private string VaultPath => Path.Combine(_directory, "vault.json");

    /// <summary>
    /// The scenario itself: the wallet is unlocked under coercion with the second password, and what
    /// opens is the decoy. The real phrase is not reachable with that password by any route the
    /// coercer has — and the two wallets do not even share an address, so the funds are not merely
    /// hidden from the screen, they are somewhere the decoy cannot spend from.
    /// </summary>
    [Fact]
    public async Task Under_coercion_the_second_password_opens_a_different_wallet()
    {
        var vault = NewVault();
        await vault.CreateAsync(RealPhrase, RealPassword);
        await vault.AddDuressWalletAsync(RealPassword, DecoyPhrase, DuressPassword);

        // What the coercer gets.
        var opened = await vault.UnlockAsync(DuressPassword);
        Assert.Equal(DecoyPhrase, opened);
        Assert.NotEqual(RealPhrase, opened);

        // And the owner still gets their own wallet.
        Assert.Equal(RealPhrase, await vault.UnlockAsync(RealPassword));

        // The decoy is a REAL wallet — it derives its own addresses, so it survives being looked at.
        var deriver = new HdAddressDeriver();
        var decoyBtc = deriver.DeriveReceiveAddress(opened, Chains.ChainId.Btc).Address;
        var realBtc = deriver.DeriveReceiveAddress(RealPhrase, Chains.ChainId.Btc).Address;

        Assert.False(string.IsNullOrWhiteSpace(decoyBtc));
        Assert.NotEqual(realBtc, decoyBtc);
    }

    /// <summary>
    /// The file may not admit that a second wallet exists. Same size, same shape, whether one wallet
    /// is stored or two — otherwise the coercer simply keeps asking, and a duress password makes
    /// things worse rather than better.
    /// </summary>
    [Fact]
    public async Task The_file_does_not_reveal_that_a_second_wallet_exists()
    {
        var plain = NewVault();
        await plain.CreateAsync(RealPhrase, RealPassword);
        var withoutDuress = await File.ReadAllBytesAsync(VaultPath);

        await plain.AddDuressWalletAsync(RealPassword, DecoyPhrase, DuressPassword);
        var withDuress = await File.ReadAllBytesAsync(VaultPath);

        Assert.Equal(withoutDuress.Length, withDuress.Length);
        Assert.Equal(DeniableVaultFormat.FileSize, withDuress.Length);

        // The header is the only fixed part; everything after it is ciphertext or noise, and the two
        // are not distinguishable. If the bytes after the header were identical, the second wallet
        // would not have been written at all.
        Assert.NotEqual(withoutDuress[8..], withDuress[8..]);
    }

    /// <summary>
    /// Removing the decoy is as deniable as adding it: the slot goes back to noise, the file keeps
    /// its size, and nothing reports whether there had been anything there.
    /// </summary>
    [Fact]
    public async Task Removing_the_decoy_leaves_a_file_that_looks_untouched()
    {
        var vault = NewVault();
        await vault.CreateAsync(RealPhrase, RealPassword);
        await vault.AddDuressWalletAsync(RealPassword, DecoyPhrase, DuressPassword);
        await vault.RemoveDuressWalletAsync(RealPassword);

        Assert.Equal(DeniableVaultFormat.FileSize, new FileInfo(VaultPath).Length);
        Assert.Equal(RealPhrase, await vault.UnlockAsync(RealPassword));

        // The decoy password no longer opens anything — and fails exactly like any wrong password.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => vault.UnlockAsync(DuressPassword));
    }

    /// <summary>
    /// A duress password identical to the real one would quietly do nothing while the user believed
    /// they were protected — the worst outcome available here.
    /// </summary>
    [Fact]
    public async Task The_duress_password_cannot_be_the_real_one()
    {
        var vault = NewVault();
        await vault.CreateAsync(RealPhrase, RealPassword);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => vault.AddDuressWalletAsync(RealPassword, DecoyPhrase, RealPassword));

        // And the real wallet is untouched by the refusal.
        Assert.Equal(RealPhrase, await vault.UnlockAsync(RealPassword));
    }

    /// <summary>
    /// A wrong password is refused whether or not a decoy is configured, and it takes the same route
    /// either way: there is no "slot 2 failed" for anybody to read anything from.
    /// </summary>
    [Fact]
    public async Task A_wrong_password_fails_the_same_way_with_or_without_a_decoy()
    {
        var vault = NewVault();
        await vault.CreateAsync(RealPhrase, RealPassword);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => vault.UnlockAsync("umbrella-not-the-password"));

        await vault.AddDuressWalletAsync(RealPassword, DecoyPhrase, DuressPassword);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => vault.UnlockAsync("umbrella-not-the-password"));
    }

    /// <summary>
    /// Wallets created before this format existed must keep working, and must end up in the new
    /// shape — a vault that only became two-slot when its owner asked for a duress password would
    /// announce, by its own file size, that they had asked.
    /// </summary>
    [Fact]
    public async Task A_legacy_vault_still_opens_and_is_migrated_in_place()
    {
        // The v1 envelope, written exactly as the previous version of the wallet wrote it.
        var legacy = LegacyVaultJson(RealPhrase, RealPassword);
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(VaultPath, legacy);

        var vault = NewVault();
        Assert.Equal(RealPhrase, await vault.UnlockAsync(RealPassword));

        // Migrated on that unlock, and still opening afterwards.
        var bytes = await File.ReadAllBytesAsync(VaultPath);
        Assert.True(DeniableVaultFormat.IsWellFormed(bytes));
        Assert.Equal(RealPhrase, await vault.UnlockAsync(RealPassword));

        // And a duress wallet can now be added to it like any other.
        await vault.AddDuressWalletAsync(RealPassword, DecoyPhrase, DuressPassword);
        Assert.Equal(DecoyPhrase, await vault.UnlockAsync(DuressPassword));
    }

    /// <summary>
    /// Builds a v1 JSON vault with the same Argon2id + AES-GCM parameters the old code used, so the
    /// migration is exercised against the real thing rather than against a stand-in.
    /// </summary>
    private static string LegacyVaultJson(string mnemonic, string password)
    {
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        var plaintext = System.Text.Encoding.UTF8.GetBytes(
            mnemonic.Normalize(System.Text.NormalizationForm.FormKD).Trim());
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var argon2 = new Konscious.Security.Cryptography.Argon2id(
            System.Text.Encoding.UTF8.GetBytes(password.Normalize(System.Text.NormalizationForm.FormKC)))
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

        return System.Text.Json.JsonSerializer.Serialize(new
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

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
        catch { /* best effort */ }
    }
}
