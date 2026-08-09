using System.IO;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>
/// Caches the last-seen balances/prices per wallet on this device, so the portfolio total shows
/// instantly on unlock or wallet-switch instead of flashing $0 while the network catches up. It is a
/// display cache only (device-local, never a server); the authoritative amounts always come from the
/// live refresh, which overwrites this a moment later.
/// </summary>
public sealed class BalanceStore
{
    private readonly string _path;

    public BalanceStore(string? path = null) =>
        _path = path ?? Path.Combine(AppPaths.DataRoot, "balances.json");

    /// <summary>One cached holding: matched back to an account by symbol + address.</summary>
    public sealed record Entry(string Symbol, string Address, double Amount, double Price, double Change);

    private sealed record Wrapper(Dictionary<string, List<Entry>> Wallets);

    public IReadOnlyList<Entry> Load(string walletId)
    {
        try
        {
            if (File.Exists(_path))
            {
                var w = JsonSerializer.Deserialize<Wrapper>(File.ReadAllText(_path));
                if (w?.Wallets is not null && w.Wallets.TryGetValue(walletId, out var list)) return list;
            }
        }
        catch
        {
            // corrupt/unreadable → no cache, harmless
        }

        return [];
    }

    public void Save(string walletId, IEnumerable<Entry> entries)
    {
        try
        {
            var wrapper = new Wrapper(new Dictionary<string, List<Entry>>());
            if (File.Exists(_path))
            {
                var existing = JsonSerializer.Deserialize<Wrapper>(File.ReadAllText(_path));
                if (existing?.Wallets is not null) wrapper = existing;
            }

            wrapper.Wallets[walletId] = entries.ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(wrapper));
        }
        catch
        {
            // non-fatal: the cache just won't persist this time
        }
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch { /* non-fatal */ }
    }
}
