using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// Remembers the current receive-address index per wallet + chain, so a UTXO wallet can hand out a
/// fresh address each time instead of reusing index 0 (which links every deposit on-chain). Stored
/// device-locally in data/addr-indexes.json — indices are not secret (addresses are public), they
/// just let the same address be re-derived after a restart.
/// </summary>
public sealed class AddressIndexStore
{
    private readonly string _path;
    private Dictionary<string, int> _map = new(StringComparer.OrdinalIgnoreCase);

    public AddressIndexStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataRoot, "addr-indexes.json");
        Load();
    }

    /// <summary>The current (highest handed-out) receive index for a wallet + chain; 0 by default.</summary>
    public int Get(string walletId, string chain) =>
        _map.TryGetValue(Key(walletId, chain), out var v) ? v : 0;

    /// <summary>Advances to the next receive index and returns it. Persisted.</summary>
    public int Increment(string walletId, string chain)
    {
        var k = Key(walletId, chain);
        var next = (_map.TryGetValue(k, out var v) ? v : 0) + 1;
        _map[k] = next;
        Save();
        return next;
    }

    private static string Key(string walletId, string chain) => $"{walletId}:{chain.ToUpperInvariant()}";

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                _map = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(_path))
                       ?? new(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            _map = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_map));
        }
        catch
        {
            // Best-effort: a fresh address still works this session even if the index isn't persisted.
        }
    }
}
