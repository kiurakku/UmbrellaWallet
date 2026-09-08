using System.IO;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>One saved contact: a label, the destination address and the network it belongs to.
/// A top-level type (not nested) so XAML <c>x:DataType</c> can bind it directly.</summary>
public sealed record AddressBookEntry(string Label, string Address, string Chain);

/// <summary>
/// A small, local address book for the Send screen (roadmap §4): saved destination addresses the user
/// can reuse instead of re-pasting. Stored on this device only (never a server); best-effort, so a
/// read/write failure just means an empty book, never a crash. These are public destination addresses —
/// no keys or secrets are ever kept here.
/// </summary>
public sealed class AddressBookStore
{
    private readonly string _path;

    public AddressBookStore(string? path = null) =>
        _path = path ?? Path.Combine(AppPaths.DataRoot, "address-book.json");

    public List<AddressBookEntry> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<List<AddressBookEntry>>(File.ReadAllText(_path)) ?? new();
        }
        catch
        {
            // corrupt/unreadable → start fresh
        }

        return new();
    }

    public void Save(IEnumerable<AddressBookEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(entries.ToList()));
        }
        catch
        {
            // non-fatal: the book just won't persist this time
        }
    }
}
