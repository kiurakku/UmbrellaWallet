using System.Collections.Generic;
using System.IO;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// Completely removes every piece of wallet data this app keeps on the PC: the encrypted seed vault,
/// the UI settings, the profile images, the watch-address list, the encrypted exchange credentials,
/// every encrypted backup left aside, and the Monero wallet (keys + cache).
///
/// The bundled runtime binaries — Tor and monero-wallet-rpc — are deliberately left intact, so the
/// app still runs after a wipe and can create a fresh wallet. Each step is independent: a locked or
/// missing file is recorded and skipped rather than aborting the whole wipe.
/// </summary>
public static class DataWiper
{
    public sealed record WipeResult(int Removed, IReadOnlyList<string> Failed);

    public static WipeResult WipeAll() => WipeAll(AppPaths.DataRoot);

    /// <summary>Wipes wallet data under <paramref name="root"/>. Root is a parameter so the safety
    /// test can run against a temp folder instead of the user's real data directory.</summary>
    public static WipeResult WipeAll(string root)
    {
        var removed = 0;
        var failed = new List<string>();

        void DeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) { File.Delete(path); removed++; }
            }
            catch
            {
                failed.Add(path);
            }
        }

        void DeleteDir(string path)
        {
            try
            {
                if (Directory.Exists(path)) { Directory.Delete(path, recursive: true); removed++; }
            }
            catch
            {
                failed.Add(path);
            }
        }

        DeleteFile(Path.Combine(root, "vault.json"));           // the encrypted seed (Main wallet)
        DeleteFile(Path.Combine(root, "wallets.json"));         // multi-wallet index
        DeleteDir(Path.Combine(root, "wallets"));               // every additional wallet's encrypted vault
        DeleteFile(Path.Combine(root, "watch-addresses.json")); // watch-only addresses
        DeleteFile(Path.Combine(root, "activity.json"));        // local activity / transaction log
        DeleteFile(Path.Combine(root, "balances.json"));        // cached balances (device-only display cache)
        DeleteFile(Path.Combine(root, "exchanges.bin"));        // encrypted exchange API keys
        DeleteFile(Path.Combine(root, "ui-settings.json"));     // theme, language, profile paths
        DeleteDir(Path.Combine(root, "profile"));               // avatar / banner / background images
        DeleteDir(Path.Combine(root, "monero"));                // Monero wallet keys + cache (not the binary)

        // Every encrypted backup that a restore left beside the vault (vault.json.replaced-*).
        try
        {
            if (Directory.Exists(root))
            {
                foreach (var backup in Directory.EnumerateFiles(root, "vault.json.replaced-*"))
                    DeleteFile(backup);
            }
        }
        catch
        {
            // enumeration failure is non-fatal
        }

        return new WipeResult(removed, failed);
    }
}
