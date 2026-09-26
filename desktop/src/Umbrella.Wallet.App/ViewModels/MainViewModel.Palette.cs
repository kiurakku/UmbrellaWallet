using Avalonia.Controls;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Seed;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// The Ctrl+K command palette: search, keyboard selection, and running a command.
///
/// Split out of MainViewModel.cs (roadmap §8.3.1) as a partial class: the code is unchanged
/// and still one type, so nothing about behaviour moved with it — only the file it lives in.
/// </summary>
public partial class MainViewModel
{
    // ---- Command palette (Ctrl+K): a pro-desktop quick launcher — jump to any screen or coin. ----
    [ObservableProperty] private bool _isCommandPaletteOpen;
    [ObservableProperty] private string _commandQuery = string.Empty;
    public System.Collections.ObjectModel.ObservableCollection<PaletteCommand> CommandResults { get; } = [];

    private static readonly PaletteCommand[] StaticCommands =
    [
        new("⌂", "Wallet", "Home · portfolio", "Portfolio"),
        new("↧", "Receive", "Get a receive address", "Receive"),
        new("↥", "Send", "Send crypto", "Send"),
        new("⇄", "Swap", "Cross-chain swap", "Swap"),
        new("◷", "Activity", "Transaction history", "Activity"),
        new("▤", "Market", "Prices & charts", "Market"),
        new("◇", "Discover", "Buy · P2P · news", "Discover"),
        new("⌁", "Connect", "Watch-only addresses · exchange balances", "Connect"),
        new("◈", "NFTs", "ERC-721 / 1155 collections", "Nfts"),
        new("⛁", "Staking", "Stakeable coins · typical rewards", "Staking"),
        new("🛡", "Security", "Security Center · what protects this wallet", "Security"),
        new("⚙", "Settings", "Preferences", "Settings"),
        new("⤓", "Lock wallet", "Lock now · Ctrl+L", "lock"),
    ];

    /// <summary>The keyboard-highlighted row; ↑/↓ move it and Enter runs it.</summary>
    [ObservableProperty] private int _paletteSelectedIndex;

    [RelayCommand]
    private void OpenCommandPalette()
    {
        CommandQuery = string.Empty;
        RebuildCommandResults();
        IsCommandPaletteOpen = true;
    }

    [RelayCommand]
    private void CloseCommandPalette() => IsCommandPaletteOpen = false;

    partial void OnCommandQueryChanged(string value) => RebuildCommandResults();

    private void RebuildCommandResults()
    {
        // The static rows are shared instances, so clear their highlight before re-listing them —
        // otherwise a row stays lit from a previous query.
        foreach (var prev in CommandResults) prev.IsSelected = false;
        CommandResults.Clear();
        var q = CommandQuery.Trim();
        bool Match(string s) => q.Length == 0 || s.Contains(q, StringComparison.OrdinalIgnoreCase);
        foreach (var c in StaticCommands)
            if (Match(c.Label) || Match(c.Hint)) CommandResults.Add(c);
        // Every other wallet, one Enter away — by name, or by typing "wallet".
        if (IsWorkspace && _registry.Wallets.Count > 1)
        {
            var hint = Loc.Instance["wallets.paletteHint"];
            foreach (var w in _registry.Wallets.Where(w => w.Id != _registry.Active?.Id))
                if (Match(w.Label) || Match(hint) || Match("wallet"))
                    CommandResults.Add(new PaletteCommand("⇄", w.Label, hint, "wallet:" + w.Id));
        }

        // Type a ticker/name to jump straight to that coin's chart.
        // Snapshot first: the market refresh mutates Market on its own schedule, and enumerating it
        // mid-update threw "collection was modified" — the same race already fixed in Assets.
        if (q.Length > 0)
            foreach (var m in Market.ToList().Where(m => Match(m.Symbol) || Match(m.Name)).Take(8))
                CommandResults.Add(new PaletteCommand("◎", m.Name, $"{m.Symbol} · open chart", "coin:" + m.Symbol));

        // A new query always re-homes the highlight on the best match.
        SetPaletteSelection(0);
    }

    /// <summary>Moves the highlight, wrapping at both ends, and lights exactly one row.</summary>
    private void SetPaletteSelection(int index)
    {
        if (CommandResults.Count == 0)
        {
            PaletteSelectedIndex = 0;
            return;
        }

        var count = CommandResults.Count;
        index = ((index % count) + count) % count; // wrap in both directions
        for (var i = 0; i < count; i++) CommandResults[i].IsSelected = i == index;
        PaletteSelectedIndex = index;
    }

    /// <summary>↓ in the palette — walk to the next result (wraps back to the first).</summary>
    [RelayCommand]
    private void PaletteMoveDown() => SetPaletteSelection(PaletteSelectedIndex + 1);

    /// <summary>↑ in the palette — walk to the previous result (wraps to the last).</summary>
    [RelayCommand]
    private void PaletteMoveUp() => SetPaletteSelection(PaletteSelectedIndex - 1);

    /// <summary>Enter in the palette runs the highlighted result (the top one until ↑/↓ moves it).</summary>
    [RelayCommand]
    private async Task RunTopPaletteCommandAsync()
    {
        if (CommandResults.Count == 0) return;
        var index = Math.Clamp(PaletteSelectedIndex, 0, CommandResults.Count - 1);
        await RunPaletteCommandAsync(CommandResults[index]);
    }

    [RelayCommand]
    private async Task RunPaletteCommandAsync(PaletteCommand? cmd)
    {
        if (cmd is null) return;
        IsCommandPaletteOpen = false;
        if (cmd.Target == "lock") { LockCommand.Execute(null); return; }
        if (cmd.Target.StartsWith("wallet:", StringComparison.Ordinal))
        {
            await SwitchWalletAsync(cmd.Target["wallet:".Length..]);
            return;
        }
        if (cmd.Target.StartsWith("coin:", StringComparison.Ordinal))
        {
            await OpenAssetChartAsync(cmd.Target["coin:".Length..]);
            return;
        }
        SelectSection(cmd.Target);
    }
}
