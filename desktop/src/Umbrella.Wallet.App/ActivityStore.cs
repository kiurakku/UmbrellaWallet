using System.IO;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>
/// Persists the activity / transaction feed to the local data folder so it survives closing the app —
/// on this device only, never a server. Best-effort: a read/write failure just means an empty history,
/// never a crash.
/// </summary>
public sealed class ActivityStore
{
    private readonly string _path;

    public ActivityStore(string? path = null) =>
        _path = path ?? Path.Combine(AppPaths.DataRoot, "activity.json");

    /// <summary>A stored activity row (the view-model's core fields, without the computed display bits).
    /// <c>Status</c> is optional so history written by older versions still deserializes (defaults to
    /// "Confirmed"). Retry context is deliberately not persisted — a stale destination must not be resent.</summary>
    public sealed record Entry(string Kind, string Asset, string Amount, string Counterparty, string When,
        string? Explorer, string Status = "Confirmed");

    public IReadOnlyList<Entry> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path)) ?? [];
        }
        catch
        {
            // corrupt/unreadable → start fresh
        }

        return [];
    }

    public void Save(IEnumerable<Entry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(entries.ToList()));
        }
        catch
        {
            // non-fatal: history just won't persist this time
        }
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch { /* non-fatal */ }
    }
}
