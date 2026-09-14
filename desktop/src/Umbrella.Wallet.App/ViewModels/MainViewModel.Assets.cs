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
/// The asset-details screen: one coin's holding, capabilities and movements.
///
/// Split out of MainViewModel.cs (roadmap §8.3.1) as a partial class: the code is unchanged
/// and still one type, so nothing about behaviour moved with it — only the file it lives in.
/// </summary>
public partial class MainViewModel
{
    // ================= ASSET DETAILS =================
    // One coin, end to end: what you hold, what it is worth, which address it lives on, what this
    // wallet can and cannot do with it, and the movements that touched it. The capability lines come
    // from ChainCatalog — the same source the Send picker reads — so this page cannot promise a
    // capability the wallet does not have (roadmap §5.1).

    [ObservableProperty] private string _assetSymbol = string.Empty;
    [ObservableProperty] private string _assetName = string.Empty;
    [ObservableProperty] private string _assetNetwork = string.Empty;
    [ObservableProperty] private string _assetAddress = string.Empty;
    [ObservableProperty] private string _assetAmountLabel = string.Empty;
    [ObservableProperty] private string _assetValueLabel = string.Empty;
    [ObservableProperty] private string _assetPriceLabel = string.Empty;
    [ObservableProperty] private string _assetChangeLabel = string.Empty;
    [ObservableProperty] private string _assetChangeColor = "#8A9099";
    [ObservableProperty] private string _assetStatusLabel = string.Empty;
    [ObservableProperty] private string _assetStatusColor = "#8A9099";
    [ObservableProperty] private string _assetDerivation = string.Empty;
    [ObservableProperty] private string _assetPrivacyNote = string.Empty;
    [ObservableProperty] private string _assetBadgeColor = "#5B4BD6";
    [ObservableProperty] private string _assetBadgeBg = "Transparent";
    [ObservableProperty] private string _assetBadgeGlyph = string.Empty;
    [ObservableProperty] private Bitmap? _assetBadgeLogo;
    [ObservableProperty] private bool _assetHasBadgeLogo;
    [ObservableProperty] private bool _assetCanSend;
    [ObservableProperty] private bool _assetCanSwap;
    [ObservableProperty] private bool _assetHasHistory;
    [ObservableProperty] private string _assetCapabilityLine = string.Empty;
    [ObservableProperty] private bool _assetHasAddress;

    /// <summary>The movements that touched this asset — the same rows as Transactions, narrowed.</summary>
    public ObservableCollection<ActivityRowViewModel> AssetActivity { get; } = [];

    public bool HasAssetActivity => AssetActivity.Count > 0;

    /// <summary>
    /// Opens the detail page for one coin. Everything shown is read from state the wallet already
    /// trusts — the account row (address + balance), the live price row, and ChainCatalog for what is
    /// actually supported. A coin with no derived account still opens: it shows the honest
    /// "not available in this wallet" status rather than a blank page.
    /// </summary>
    [RelayCommand]
    private void OpenAssetDetails(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;
        var sym = symbol.Trim().ToUpperInvariant();

        // Snapshot collections — market/holdings refresh can mutate ObservableCollections on another
        // thread while we enumerate, which throws InvalidOperationException.
        var holdings = Holdings.ToArray();
        var accounts = Accounts.ToArray();
        var market = Market.ToArray();

        var holding = holdings.FirstOrDefault(h => string.Equals(h.Symbol, sym, StringComparison.OrdinalIgnoreCase));
        var account = accounts.FirstOrDefault(a => string.Equals(a.Symbol, sym, StringComparison.OrdinalIgnoreCase));
        var priceRow = market.FirstOrDefault(m => string.Equals(m.Symbol, sym, StringComparison.OrdinalIgnoreCase));

        AssetSymbol = sym;
        AssetName = holding?.Name ?? account?.Name ?? priceRow?.Name ?? sym;
        AssetNetwork = holding?.NetworkLabel ?? account?.NetworkLabel ?? CoinNetworks.For(sym, sym);
        AssetAddress = account?.Address ?? string.Empty;
        AssetHasAddress = IsRealAddress(AssetAddress);
        AssetAmountLabel = holding?.AmountLabel ?? $"0 {sym}";
        AssetValueLabel = holding?.ValueLabel ?? Fx.Money(0);
        AssetPriceLabel = holding?.PriceLabel ?? priceRow?.PriceLabel ?? "—";
        AssetChangeLabel = holding?.ChangeLabel ?? priceRow?.ChangeLabel ?? "·";
        AssetChangeColor = holding?.ChangeColor ?? priceRow?.ChangeColor ?? "#8A9099";
        AssetStatusLabel = account?.StatusLabel ?? "Not in this wallet";
        AssetStatusColor = account?.StatusColor ?? "#8A9099";
        AssetDerivation = account?.Derivation ?? string.Empty;
        AssetBadgeColor = CoinBadge.Color(sym);
        AssetBadgeGlyph = CoinGlyphs.For(sym);
        AssetBadgeLogo = CoinBadge.Logo(sym);
        AssetHasBadgeLogo = CoinBadge.HasLogo(sym);
        AssetBadgeBg = AssetHasBadgeLogo ? "Transparent" : AssetBadgeColor;

        // Capabilities and the privacy note come from the catalog, never from this page's own opinion.
        var chain = ParseChain(sym);
        var info = chain is not null ? ChainCatalog.Get(chain.Value) : null;
        // An action is only offered where there is a REAL derived address behind it: while the vault is
        // locked the account rows are placeholders ("Unlock wallet to derive address"), and offering
        // Send from one of those would be a promise the wallet cannot keep.
        AssetCanSend = info?.CanSend == true && AssetHasAddress;
        AssetCanSwap = info?.CanSwap == true && AssetHasAddress;
        AssetHasHistory = info?.HasHistory == true;
        AssetPrivacyNote = info?.PrivacyNote ?? string.Empty;
        AssetCapabilityLine = info is null
            ? Loc.Instance["asset.capNone"]
            : string.Join(" · ", new[]
            {
                info.CanReceive ? Loc.Instance["asset.capReceive"] : null,
                info.CanSend ? Loc.Instance["asset.capSend"] : null,
                info.CanSyncBalance ? Loc.Instance["asset.capBalance"] : null,
                info.HasHistory ? Loc.Instance["asset.capHistory"] : null,
                info.CanSwap ? Loc.Instance["asset.capSwap"] : null,
            }.Where(x => !string.IsNullOrEmpty(x)));

        RebuildAssetActivity(sym);
        SelectSection("Asset");
    }

    /// <summary>Narrows the merged movement list to this asset. Empty is a normal answer, not an error.</summary>
    private void RebuildAssetActivity(string symbol)
    {
        AssetActivity.Clear();
        foreach (var row in Transactions.ToArray()
            .Where(t => string.Equals(t.Asset, symbol, StringComparison.OrdinalIgnoreCase))
            .Take(12))
        {
            AssetActivity.Add(row);
        }

        OnPropertyChanged(nameof(HasAssetActivity));
    }

    /// <summary>Receive this exact coin — jumps to Receive with the asset already selected.</summary>
    [RelayCommand]
    private void AssetReceive()
    {
        var account = Accounts.FirstOrDefault(a =>
            string.Equals(a.Symbol, AssetSymbol, StringComparison.OrdinalIgnoreCase) && IsRealAddress(a.Address));
        if (account is null) return;
        SelectSection("Receive");
        SelectReceiveAccountCommand.Execute(account);
    }

    /// <summary>Send this exact coin — jumps to Send with the asset already picked.</summary>
    [RelayCommand]
    private void AssetSend()
    {
        var option = SendableAssets.FirstOrDefault(o =>
            string.Equals(o.Symbol, AssetSymbol, StringComparison.OrdinalIgnoreCase));
        SelectSection("Send");
        if (option is not null) SelectedSendAsset = option;
    }

    /// <summary>Opens this coin's chart on the Market page.</summary>
    [RelayCommand]
    private async Task AssetChartAsync() => await OpenAssetChartAsync(AssetSymbol);
}
