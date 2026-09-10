using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Private, encrypted notes on transactions — the user's own bookkeeping, attached by transaction id and
/// shown in the Activity feed. Notes are sealed at rest with a seed-derived key (<see cref="TxNoteStore"/>),
/// so they are readable only while this wallet is unlocked and never touch the network.
/// </summary>
public partial class MainViewModel
{
    // The decrypted note map for the active wallet (txid -> note), loaded on unlock. Empty when locked.
    private Dictionary<string, string> _txNotes = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>The transaction currently being annotated (its id), or empty when the editor is closed.</summary>
    [ObservableProperty] private string _noteEditingTxId = string.Empty;

    /// <summary>The note text in the inline editor.</summary>
    [ObservableProperty] private string _noteDraft = string.Empty;

    public bool IsEditingNote => !string.IsNullOrEmpty(NoteEditingTxId);

    partial void OnNoteEditingTxIdChanged(string value) => OnPropertyChanged(nameof(IsEditingNote));

    private TxNoteStore NoteStore() => new(_registry.Active?.Id ?? "default");

    /// <summary>The saved note for a transaction, or null. Used when building activity rows.</summary>
    private string? TxNoteFor(string? txId) =>
        !string.IsNullOrWhiteSpace(txId) && _txNotes.TryGetValue(txId, out var note) ? note : null;

    /// <summary>Decrypts this wallet's notes into memory (called on unlock / before the feed is built).</summary>
    private async Task LoadTxNotesAsync()
    {
        if (_unlockedMnemonic is null) { _txNotes = new(System.StringComparer.OrdinalIgnoreCase); return; }
        try
        {
            var loaded = await NoteStore().LoadAsync(_unlockedMnemonic);
            _txNotes = new Dictionary<string, string>(loaded, System.StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _txNotes = new(System.StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Opens the inline note editor for a transaction row.</summary>
    [RelayCommand]
    private void BeginEditNote(ActivityRowViewModel? row)
    {
        if (row?.TxId is not { } txId || string.IsNullOrWhiteSpace(txId)) return;
        NoteEditingTxId = txId;
        NoteDraft = TxNoteFor(txId) ?? string.Empty;
    }

    [RelayCommand]
    private void CancelEditNote()
    {
        NoteEditingTxId = string.Empty;
        NoteDraft = string.Empty;
    }

    /// <summary>Saves (or clears) the note for the transaction under edit, then updates the visible rows
    /// in place — no network refetch. A blank note removes it.</summary>
    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        var txId = NoteEditingTxId;
        if (string.IsNullOrWhiteSpace(txId) || _unlockedMnemonic is null) { CancelEditNote(); return; }

        var text = (NoteDraft ?? string.Empty).Trim();
        if (text.Length == 0) _txNotes.Remove(txId);
        else _txNotes[txId] = text;

        try { await NoteStore().SaveAsync(_txNotes, _unlockedMnemonic); }
        catch { /* non-fatal: the note just won't persist this time */ }

        ApplyNoteToFeeds(txId, text.Length == 0 ? null : text);
        CancelEditNote();
    }

    /// <summary>Replaces the note on every visible copy of a transaction row (records are immutable, so we
    /// swap in <c>row with { Note = … }</c> rather than mutate).</summary>
    private void ApplyNoteToFeeds(string txId, string? note)
    {
        foreach (var feed in new[] { Activity, Transactions, FilteredActivity, RecentActivity, AssetActivity })
        {
            for (var i = 0; i < feed.Count; i++)
            {
                if (string.Equals(feed[i].TxId, txId, System.StringComparison.OrdinalIgnoreCase))
                    feed[i] = feed[i] with { Note = note };
            }
        }
    }
}
