using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// The first screen anybody sees, before a wallet exists or is unlocked (roadmap L.1 / L.3 / L.8).
///
/// It states the three things that cannot be said usefully later — nobody here can recover a lost
/// phrase, a public chain is not made private by any wallet, and this is software rather than a
/// financial institution — plus the 18+ statement the stores require, and links to the terms and the
/// privacy policy that are already in the repository.
///
/// It gates everything: no create, no import, no unlock until it is acknowledged. A disclaimer the
/// user can walk around is a disclaimer for the developer's benefit rather than theirs.
/// </summary>
public partial class MainViewModel
{
    /// <summary>True until this install has accepted the current wording.</summary>
    public bool NeedsDisclaimer => FirstRunConsent.NeedsAcceptance(_uiSettings.AcceptedTermsVersion);

    /// <summary>The full-screen first-run page. Shown ahead of welcome AND unlock.</summary>
    public bool IsDisclaimerStage => NeedsDisclaimer;

    [ObservableProperty] private bool _disclaimerAgeConfirmed;
    [ObservableProperty] private bool _disclaimerTermsAccepted;

    /// <summary>Both boxes have to be ticked, because they are two different statements.</summary>
    public bool CanAcceptDisclaimer =>
        FirstRunConsent.CanAccept(DisclaimerAgeConfirmed, DisclaimerTermsAccepted);

    partial void OnDisclaimerAgeConfirmedChanged(bool value) =>
        OnPropertyChanged(nameof(CanAcceptDisclaimer));

    partial void OnDisclaimerTermsAcceptedChanged(bool value) =>
        OnPropertyChanged(nameof(CanAcceptDisclaimer));

    /// <summary>Records the acceptance and hands the user on to create / import / unlock.</summary>
    [RelayCommand]
    private void AcceptDisclaimer()
    {
        if (!CanAcceptDisclaimer) return;

        _uiSettings.AcceptedTermsVersion = FirstRunConsent.CurrentVersion;
        _uiSettings.Save();

        OnPropertyChanged(nameof(NeedsDisclaimer));
        OnPropertyChanged(nameof(IsDisclaimerStage));
        NotifySectionFlags();
    }

    /// <summary>The documents the acknowledgement refers to, at the canonical repository.</summary>
    public string TermsUrl => "https://github.com/kiurakku/UmbrellaWallet/blob/main/TERMS_OF_SERVICE.md";

    public string PrivacyPolicyUrl =>
        "https://github.com/kiurakku/UmbrellaWallet/blob/main/PRIVACY_POLICY.md";
}
