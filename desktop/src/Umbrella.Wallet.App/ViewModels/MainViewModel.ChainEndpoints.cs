using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
// Aliased: an unqualified 'Core' would bind to the Umbrella.Wallet.Core NAMESPACE from inside
// Umbrella.Wallet.App.ViewModels, shadowing the alias.
using Endpoints = global::Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>One server offered for the chain currently being configured.</summary>
public sealed partial class EndpointOptionVm : ObservableObject
{
    public EndpointOptionVm(Endpoints.ChainEndpointOption option, bool isDefault)
    {
        Option = option;
        IsDefault = isDefault;
    }

    public Endpoints.ChainEndpointOption Option { get; }
    public string Label => Option.Label;
    public string Host => Option.Host;
    public string BaseUrl => Option.BaseUrl;

    /// <summary>True for the one this build ships with, so "back to normal" is findable.</summary>
    public bool IsDefault { get; }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// Which server answers for each chain, as something the user can change.
///
/// The Monero node picker made this visible for one coin. On a transparent chain the same choice is
/// heavier, not lighter: to ask "what is the balance of bc1q…", the wallet has to SAY the address, so
/// whoever answers learns it belongs to a wallet and can tie together every address asked about in one
/// session. Tor hides the IP. It does not un-send the address. The only real fix is pointing the
/// wallet at a server you trust — your own Esplora or Electrum instance, a friend's, or simply a
/// different company than the one this build happened to ship with.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The chains whose server can be redirected.</summary>
    public IReadOnlyList<string> EndpointChains { get; } = Endpoints.ChainEndpoints.Configurable;

    /// <summary>The chain being configured. One editor at a time keeps the screen readable.</summary>
    [ObservableProperty] private string _selectedEndpointChain = Endpoints.ChainEndpoints.Configurable[0];

    public ObservableCollection<EndpointOptionVm> EndpointOptions { get; } = new();

    [ObservableProperty] private string _customEndpoint = string.Empty;
    [ObservableProperty] private string _endpointStatus = string.Empty;
    [ObservableProperty] private string _endpointStatusColor = "#8B909A";

    /// <summary>True when the selected chain is pointed somewhere other than the shipped default.</summary>
    public bool SelectedEndpointIsCustom => Endpoints.ChainEndpoints.IsCustomised(SelectedEndpointChain);

    /// <summary>How many chains the user has redirected — worth surfacing, because it is their own
    /// decision and they should be able to see it at a glance.</summary>
    public int CustomEndpointCount => EndpointChains.Count(Endpoints.ChainEndpoints.IsCustomised);

    /// <summary>Applies the saved overrides. Called before any network request can go out.</summary>
    private void LoadChainEndpoints()
    {
        Endpoints.ChainEndpoints.Restore(_uiSettings.ChainEndpoints);
        RebuildEndpointOptions();
    }

    partial void OnSelectedEndpointChainChanged(string value) => RebuildEndpointOptions();

    private void RebuildEndpointOptions()
    {
        var symbol = SelectedEndpointChain;
        var active = Endpoints.ChainEndpoints.OverrideFor(symbol);
        var known = Endpoints.ChainEndpoints.Known.TryGetValue(symbol, out var list)
            ? list
            : Array.Empty<Endpoints.ChainEndpointOption>();

        EndpointOptions.Clear();
        for (var i = 0; i < known.Count; i++)
        {
            var option = new EndpointOptionVm(known[i], isDefault: i == 0)
            {
                // With no override, the shipped default is what is in use.
                IsSelected = active is null
                    ? i == 0
                    : string.Equals(active, known[i].BaseUrl, StringComparison.OrdinalIgnoreCase),
            };
            EndpointOptions.Add(option);
        }

        CustomEndpoint = EndpointOptions.Any(o => o.IsSelected) ? string.Empty : active ?? string.Empty;
        RefreshEndpointStatus();
    }

    [RelayCommand]
    private void SelectEndpoint(EndpointOptionVm? option)
    {
        if (option is null) return;

        // Choosing the shipped default CLEARS the override rather than pinning it. Otherwise a user
        // who went back to the default would still be frozen on that URL if a later build moved it.
        SaveEndpoint(option.IsDefault ? null : option.BaseUrl);
    }

    [RelayCommand]
    private void ApplyCustomEndpoint()
    {
        var rejection = Endpoints.ChainEndpoints.Validate(CustomEndpoint, out _);
        if (rejection != Endpoints.EndpointRejection.None)
        {
            EndpointStatus = Loc.Instance[RejectionKey(rejection)];
            EndpointStatusColor = "#E09A9A";
            return;
        }

        SaveEndpoint(CustomEndpoint);
    }

    /// <summary>Puts this chain back on the server the build ships with.</summary>
    [RelayCommand]
    private void ResetEndpoint()
    {
        CustomEndpoint = string.Empty;
        SaveEndpoint(null);
    }

    private void SaveEndpoint(string? baseUrl)
    {
        var symbol = SelectedEndpointChain;
        try
        {
            Endpoints.ChainEndpoints.SetOverride(symbol, baseUrl);
        }
        catch (ArgumentException)
        {
            EndpointStatus = Loc.Instance["endpoint.badUrl"];
            EndpointStatusColor = "#E09A9A";
            return;
        }

        _uiSettings.ChainEndpoints = Endpoints.ChainEndpoints.Serialise();
        _uiSettings.Save();

        // A cached scan was answered by the PREVIOUS server. Dropping it means the next refresh reads
        // the chain through the one the user just chose, rather than showing them a number the old
        // server gave and letting them believe the change took effect.
        _utxoScans.Remove(symbol);
        _lastUtxoScan.Remove(symbol);

        RebuildEndpointOptions();
        OnPropertyChanged(nameof(SelectedEndpointIsCustom));
        OnPropertyChanged(nameof(CustomEndpointCount));
        BuildCounterparties();   // the disclosure list names hosts, and one just changed

        if (IsUnlocked)
            PushActivity("Security", "Endpoint", symbol, baseUrl ?? Loc.Instance["endpoint.default"], "now");

        if (IsUnlocked) _ = RefreshLiveDataAsync();
    }

    private void RefreshEndpointStatus()
    {
        var symbol = SelectedEndpointChain;
        var active = Endpoints.ChainEndpoints.OverrideFor(symbol);

        (EndpointStatus, EndpointStatusColor) = active is null
            ? ($"{Loc.Instance["endpoint.using"]} {DefaultHostFor(symbol)} · {Loc.Instance["endpoint.default"]}", "#8A9099")
            : ($"{Loc.Instance["endpoint.using"]} {HostOf(active)}", "#8FCB9B");
    }

    private static string DefaultHostFor(string symbol) =>
        Endpoints.ChainEndpoints.Known.TryGetValue(symbol, out var list) && list.Count > 0 ? list[0].Host : symbol;

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    private static string RejectionKey(Endpoints.EndpointRejection rejection) => rejection switch
    {
        Endpoints.EndpointRejection.InsecureScheme => "endpoint.insecure",
        Endpoints.EndpointRejection.CredentialsInUrl => "endpoint.credentials",
        Endpoints.EndpointRejection.HasQuery => "endpoint.query",
        _ => "endpoint.badUrl",
    };
}
