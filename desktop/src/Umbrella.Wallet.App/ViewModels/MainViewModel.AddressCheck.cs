using CommunityToolkit.Mvvm.ComponentModel;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// The Address checker: paste any crypto address to see which network it belongs to and whether it is
/// well-formed. Fully local (no network) and read-only — a "verify, don't trust" convenience that helps
/// avoid sending to a wrong-network or mistyped address. Split out as a partial (roadmap §8.3.1).
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty] private string _addrCheckInput = string.Empty;

    partial void OnAddrCheckInputChanged(string value)
    {
        OnPropertyChanged(nameof(AddrCheckResult));
        OnPropertyChanged(nameof(AddrCheckColor));
        OnPropertyChanged(nameof(HasAddrCheck));
    }

    public bool HasAddrCheck => !string.IsNullOrWhiteSpace(AddrCheckInput);

    private AddressInspectResult AddrCheck => AddressInspector.Inspect(AddrCheckInput);

    /// <summary>One-line verdict: the detected network and how sure we are it is well-formed.</summary>
    public string AddrCheckResult
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AddrCheckInput)) return string.Empty;
            var r = AddrCheck;
            if (!r.Recognised) return Loc.Instance["addrchk.unknown"];
            return r.Validity switch
            {
                AddressValidity.Valid => string.Format(Loc.Instance["addrchk.valid"], r.Network),
                AddressValidity.Invalid => string.Format(Loc.Instance["addrchk.invalid"], r.Network),
                _ => string.Format(Loc.Instance["addrchk.recognized"], r.Network),
            };
        }
    }

    /// <summary>Green when valid, red when the checksum fails, amber for recognised-but-unverified.</summary>
    public string AddrCheckColor
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AddrCheckInput)) return "#8B909A";
            return AddrCheck.Validity switch
            {
                AddressValidity.Valid => "#8FCB9B",
                AddressValidity.Invalid => "#E09A9A",
                _ => "#E7CA83",
            };
        }
    }
}
