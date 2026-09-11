using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>One pickable Monero node row.</summary>
public sealed partial class MoneroNodeOptionVm : ObservableObject
{
    public MoneroNodeOptionVm(MoneroNode node) => Node = node;

    public MoneroNode Node { get; }
    public string Label => Node.Label;
    public string Address => Node.Address;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// Choosing which machine answers "what is on the chain" for Monero.
///
/// This wallet used to pick silently — one hardcoded hostname, the same for everybody, with no way to
/// see it or change it. That is the wrong default for the one coin people choose specifically because
/// they do not want to be watched: a remote node sees the IP that connected, that it belongs to a
/// Monero wallet, roughly which blocks it asked for, when it is online, and which connection a
/// transaction entered the network through. It does not see the keys, the balance, the addresses or
/// the amounts — those stay here, and Monero encrypts amounts on the chain itself.
///
/// Both halves are shown on the setting, for the same reason the private-send card shows both: a
/// reassurance that arrives without its limits is how somebody ends up trusting more than they should.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The nodes shipped with this build, as a pick-list.</summary>
    public ObservableCollection<MoneroNodeOptionVm> MoneroNodes { get; } =
        new(MoneroNodeCatalog.Public.Select(n => new MoneroNodeOptionVm(n)));

    /// <summary>The custom host:port field. Accepts a .onion address, which is then only ever used
    /// with Tor on — an .onion node without Tor cannot resolve at all.</summary>
    [ObservableProperty] private string _customMoneroNode = string.Empty;

    [ObservableProperty] private string _moneroNodeStatus = string.Empty;
    [ObservableProperty] private string _moneroNodeStatusColor = "#8B909A";

    /// <summary>The node address the daemon will be told to use: what the user saved, or the default.</summary>
    public string ActiveMoneroNode =>
        MoneroNode.TryParse(_uiSettings.MoneroNode, out var saved)
            ? saved.Address
            : MoneroNodeCatalog.Default.Address;

    /// <summary>True when the saved choice is not one of the shipped nodes — i.e. the user's own.</summary>
    public bool UsesCustomMoneroNode => !MoneroNodeCatalog.IsKnown(ActiveMoneroNode);

    /// <summary>Reads the saved choice into the pickers. Called once the settings are loaded.</summary>
    private void LoadMoneroNodeChoice()
    {
        var active = ActiveMoneroNode;
        MarkSelected(active);
        if (UsesCustomMoneroNode) CustomMoneroNode = active;
        RefreshMoneroNodeStatus();
    }

    private void MarkSelected(string address)
    {
        foreach (var option in MoneroNodes)
            option.IsSelected = option.Address.Equals(address, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Picking a shipped node saves it immediately — one click, no second confirmation.</summary>
    [RelayCommand]
    private void SelectMoneroNode(MoneroNodeOptionVm? option)
    {
        if (option is null) return;
        SaveMoneroNode(option.Node);
    }

    /// <summary>Applies whatever is typed in the custom field.</summary>
    [RelayCommand]
    private void ApplyCustomMoneroNode()
    {
        if (!MoneroNode.TryParse(CustomMoneroNode, out var node))
        {
            MoneroNodeStatus = Loc.Instance["xmr.node.badAddress"];
            MoneroNodeStatusColor = "#E09A9A";
            return;
        }

        SaveMoneroNode(node);
    }

    private void SaveMoneroNode(MoneroNode node)
    {
        _uiSettings.MoneroNode = node.Address;
        _uiSettings.Save();
        _monero.NodeAddress = node.Address;

        MarkSelected(node.Address);
        OnPropertyChanged(nameof(ActiveMoneroNode));
        OnPropertyChanged(nameof(UsesCustomMoneroNode));
        RefreshMoneroNodeStatus();

        if (IsUnlocked)
            PushActivity("Security", "Monero node", node.Label, node.Address, "now");

        // The daemon is already talking to the old node, and a running connection is not moved out
        // from under a scan in progress — that would leave the balance half-read with no explanation.
        // The switch takes effect on the next start, and the status line says so rather than letting
        // the user believe the change already happened.
        if (_monero.IsRunning)
        {
            MoneroNodeStatus = Loc.Instance["xmr.node.restartNeeded"];
            MoneroNodeStatusColor = "#E7CA83";
        }
    }

    /// <summary>Says whether the chosen node can be used right now, and if not, exactly why.</summary>
    public void RefreshMoneroNodeStatus()
    {
        if (!MoneroNode.TryParse(ActiveMoneroNode, out var node)) return;

        var block = node.BlockedBecause(torConnected: TorEnabled, killSwitchArmed: TorOnly);
        (MoneroNodeStatus, MoneroNodeStatusColor) = block switch
        {
            MoneroNodeBlock.OnionNeedsTor =>
                (Loc.Instance["xmr.node.onionNeedsTor"], "#E09A9A"),
            MoneroNodeBlock.ClearnetBlockedByKillSwitch =>
                (Loc.Instance["xmr.node.blocked"], "#E09A9A"),
            _ => ($"{Loc.Instance["xmr.node.active"]} {node.Address}", "#8FCB9B"),
        };
    }
}
