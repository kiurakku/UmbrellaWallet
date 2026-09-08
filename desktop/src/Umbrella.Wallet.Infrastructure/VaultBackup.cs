using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Umbrella.Wallet.Core.Seed;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>Everything a backup carries, so a restore rebuilds the wallet as it was.</summary>
public sealed record VaultBackupContents(
    string Vault,
    string? WatchAddresses,
    string? Exchanges);

/// <summary>Why a backup check ended the way it did, so the UI can say it in the user's language
/// instead of showing this layer's English sentence (roadmap §8.2).</summary>
public enum VaultBackupReason
{
    Verified = 0,
    FileMissing,
    NotABackup,
    NoVault,
    WrongPassword,
    DamagedPhrase,
    CorruptVault,
}

/// <summary>
/// The outcome of verifying a backup — proof that it is actually restorable, without ever exposing
/// the seed. Carries only non-secret metadata.
/// </summary>
public sealed record VaultBackupVerification(
    bool Ok,
    string Message,
    DateTime? ExportedUtc = null,
    bool HasWatchAddresses = false,
    bool HasExchanges = false,
    VaultBackupReason Reason = VaultBackupReason.Verified);

/// <summary>
/// Export and restore of the encrypted vault.
///
/// The backup is a copy of the already-encrypted vault plus the non-secret side files — it is
/// never decrypted on the way out, so the exported file is exactly as safe as the vault itself
/// and is useless without the password. That also means a backup can only be restored with the
/// password that made it; there is no recovery path, by design.
/// </summary>
public static class VaultBackup
{
    private const string Magic = "umbrella-backup-v1";

    /// <summary>Writes a backup bundle. Returns the byte count so the UI can confirm it wrote.</summary>
    public static async Task<(bool Ok, string Message)> ExportAsync(
        string destinationPath, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(AppPaths.VaultFile))
            {
                return (false, "There is no vault on this PC to back up yet.");
            }

            var bundle = new Dictionary<string, string?>
            {
                ["magic"] = Magic,
                ["exportedUtc"] = DateTime.UtcNow.ToString("O"),
                ["vault"] = await File.ReadAllTextAsync(AppPaths.VaultFile, ct),
                ["watchAddresses"] = await ReadIfPresentAsync(AppPaths.WatchAddressesFile, ct),
                // Encrypted with a key derived from the seed, so it stays sealed in the backup too.
                ["exchanges"] = await ReadBytesAsBase64Async(
                    Path.Combine(AppPaths.DataRoot, "exchanges.bin"), ct),
            };

            var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(destinationPath, json, ct);

            var size = new FileInfo(destinationPath).Length;
            return (true, $"Backup written · {size:N0} bytes · still encrypted with your password");
        }
        catch (Exception ex)
        {
            return (false, $"Backup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies that a backup is genuinely restorable: it parses, its embedded vault decrypts with
    /// the given password, and the result is a valid BIP39 recovery phrase. Proves the backup is
    /// usable BEFORE the user relies on it — a backup that cannot be decrypted is worthless. Touches
    /// nothing in the live data directory and never returns or logs the seed.
    /// </summary>
    public static async Task<VaultBackupVerification> VerifyAsync(
        string sourcePath, string password, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
            return new VaultBackupVerification(false, "That backup file does not exist.",
                Reason: VaultBackupReason.FileMissing);

        Dictionary<string, string?>? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<Dictionary<string, string?>>(
                await File.ReadAllTextAsync(sourcePath, ct));
        }
        catch
        {
            return new VaultBackupVerification(false, "That file is not an Umbrella backup.",
                Reason: VaultBackupReason.NotABackup);
        }

        if (bundle is null || !bundle.TryGetValue("magic", out var magic) || magic != Magic)
            return new VaultBackupVerification(false, "That file is not an Umbrella backup.",
                Reason: VaultBackupReason.NotABackup);

        if (!bundle.TryGetValue("vault", out var vault) || string.IsNullOrWhiteSpace(vault))
            return new VaultBackupVerification(false, "The backup does not contain a vault.",
                Reason: VaultBackupReason.NoVault);

        DateTime? exportedUtc = null;
        if (bundle.TryGetValue("exportedUtc", out var exported) &&
            DateTime.TryParse(exported, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            exportedUtc = parsed;

        var hasWatch = bundle.TryGetValue("watchAddresses", out var w) && !string.IsNullOrWhiteSpace(w);
        var hasExchanges = bundle.TryGetValue("exchanges", out var e) && !string.IsNullOrWhiteSpace(e);

        // Decrypt the embedded vault via the real unlock path, against a throwaway temp copy so the
        // live vault is never touched. The mnemonic is validated and then dropped, never surfaced.
        var tempPath = Path.Combine(Path.GetTempPath(), $"umbrella-verify-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempPath, vault, ct);
            var mnemonic = await new EncryptedFileSeedVault(tempPath).UnlockAsync(password, ct);

            var check = new Bip39MnemonicService().Validate(mnemonic);
            if (!check.IsValid)
                return new VaultBackupVerification(false,
                    "The backup decrypted, but its recovery phrase is not valid — the backup is damaged.",
                    exportedUtc, hasWatch, hasExchanges, VaultBackupReason.DamagedPhrase);

            return new VaultBackupVerification(true,
                "Backup verified — it decrypts with this password and holds a valid recovery phrase.",
                exportedUtc, hasWatch, hasExchanges, VaultBackupReason.Verified);
        }
        catch (UnauthorizedAccessException)
        {
            return new VaultBackupVerification(false,
                "Could not decrypt the backup — wrong password, or the backup is damaged.",
                exportedUtc, hasWatch, hasExchanges, VaultBackupReason.WrongPassword);
        }
        catch (ArgumentException)
        {
            // The vault password must be ≥12 chars; a shorter one can never be correct.
            return new VaultBackupVerification(false,
                "Could not decrypt the backup — wrong password, or the backup is damaged.",
                exportedUtc, hasWatch, hasExchanges, VaultBackupReason.WrongPassword);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or FormatException)
        {
            return new VaultBackupVerification(false,
                "The backup's vault is corrupt or from an unsupported version.",
                exportedUtc, hasWatch, hasExchanges, VaultBackupReason.CorruptVault);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Restores a backup over the current data directory. The existing vault is moved aside
    /// rather than deleted, so a mistaken restore is recoverable.
    /// </summary>
    public static async Task<(bool Ok, string Message)> RestoreAsync(
        string sourcePath, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(sourcePath)) return (false, "That backup file does not exist.");

            var json = await File.ReadAllTextAsync(sourcePath, ct);
            Dictionary<string, string?>? bundle;
            try
            {
                bundle = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            }
            catch
            {
                return (false, "That file is not an Umbrella backup.");
            }

            if (bundle is null ||
                !bundle.TryGetValue("magic", out var magic) || magic != Magic)
            {
                return (false, "That file is not an Umbrella backup.");
            }

            if (!bundle.TryGetValue("vault", out var vault) || string.IsNullOrWhiteSpace(vault))
            {
                return (false, "The backup does not contain a vault.");
            }

            // Verify it parses as a vault before touching anything on disk.
            try
            {
                using var probe = JsonDocument.Parse(vault);
                if (!probe.RootElement.TryGetProperty("Version", out _) &&
                    !probe.RootElement.TryGetProperty("version", out _))
                {
                    return (false, "The backup's vault looks corrupt.");
                }
            }
            catch
            {
                return (false, "The backup's vault looks corrupt.");
            }

            Directory.CreateDirectory(AppPaths.DataRoot);

            if (File.Exists(AppPaths.VaultFile))
            {
                var aside = $"{AppPaths.VaultFile}.replaced-{DateTime.UtcNow:yyyyMMddHHmmss}";
                File.Move(AppPaths.VaultFile, aside);
            }

            await File.WriteAllTextAsync(AppPaths.VaultFile, vault, ct);

            if (bundle.TryGetValue("watchAddresses", out var watch) && !string.IsNullOrWhiteSpace(watch))
            {
                await File.WriteAllTextAsync(AppPaths.WatchAddressesFile, watch, ct);
            }

            if (bundle.TryGetValue("exchanges", out var exchanges) && !string.IsNullOrWhiteSpace(exchanges))
            {
                await File.WriteAllBytesAsync(
                    Path.Combine(AppPaths.DataRoot, "exchanges.bin"), Convert.FromBase64String(exchanges), ct);
            }

            return (true, "Backup restored · unlock with the password that made it");
        }
        catch (Exception ex)
        {
            return (false, $"Restore failed: {ex.Message}");
        }
    }

    /// <summary>A default filename that sorts by date and says what it is.</summary>
    public static string SuggestedFileName() =>
        $"umbrella-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.json";

    private static async Task<string?> ReadIfPresentAsync(string path, CancellationToken ct) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;

    private static async Task<string?> ReadBytesAsBase64Async(string path, CancellationToken ct) =>
        File.Exists(path) ? Convert.ToBase64String(await File.ReadAllBytesAsync(path, ct)) : null;
}
