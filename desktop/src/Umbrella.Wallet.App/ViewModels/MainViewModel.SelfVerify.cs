using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>One chain's watch-only key: what it is, where it came from, and what it exposes.</summary>
public sealed record XpubRowVm(string Symbol, string Path, string Key);

/// <summary>
/// Check this wallet against something that is not this wallet (roadmap P1.20).
///
/// Every number on screen here is produced by the same program that tells the user their money is
/// safe. That is not a reason to distrust it; it is a reason the wallet should hand them the means
/// to ask somebody else. An account xpub does exactly that: an independent scanner derives the same
/// addresses and reports the same balance, or it does not, and either answer is worth more than this
/// wallet's own reassurance.
///
/// The cost is stated before anything is shown. An xpub reveals EVERY address on that account, past
/// and future, to whoever receives it — it cannot spend a satoshi, and it can deanonymise a lifetime
/// of transactions. Both halves, together, always (MANIFESTO §2).
/// </summary>
public partial class MainViewModel
{
    /// <summary>The watch-only keys, once the password has been entered. Empty until then.</summary>
    public ObservableCollection<XpubRowVm> ExportedXpubs { get; } = new();

    [ObservableProperty] private string _xpubPassword = string.Empty;
    [ObservableProperty] private string _xpubError = string.Empty;

    public bool HasExportedXpubs => ExportedXpubs.Count > 0;

    /// <summary>
    /// Re-derives the watch-only keys from the vault by re-entering the password, rather than
    /// keeping them reachable from a button — the same rule the recovery-phrase reveal follows.
    /// </summary>
    [RelayCommand]
    private async Task RevealXpubsAsync()
    {
        XpubError = string.Empty;
        ExportedXpubs.Clear();
        OnPropertyChanged(nameof(HasExportedXpubs));

        if (XpubPassword.Length < MinPasswordLength)
        {
            XpubError = string.Format(Loc.Instance["verify.errPw"], MinPasswordLength);
            return;
        }

        await RunBusyAsync(async () =>
        {
            string mnemonic;
            try
            {
                mnemonic = await _vault.UnlockAsync(XpubPassword);
            }
            catch (Exception)
            {
                // Wrong password and a damaged vault answer the same way here, as everywhere else.
                XpubError = Loc.Instance["verify.errWrongPw"];
                return;
            }

            try
            {
                // Only the chains whose addresses hang off a BIP32 account — the ones a third-party
                // scanner can actually walk from a key. Account-based chains (ETH, TRON, SOL…) have
                // a single address, which the Receive screen already shows in full.
                foreach (var symbol in UtxoScanChains)
                {
                    var chain = ParseChain(symbol);
                    if (chain is null) continue;

                    ExportedXpubs.Add(new XpubRowVm(
                        symbol,
                        Umbrella.Wallet.Core.Derivation.HdAddressDeriver.AccountXpubPath(chain.Value),
                        _deriver.DeriveAccountXpub(mnemonic, chain.Value)));

                    // Bitcoin's balance here includes the Taproot branch (P2.1). A scanner handed only
                    // the SegWit key would report less than the wallet shows, and the check would
                    // "fail" for a reason that is not a discrepancy at all.
                    if (Umbrella.Wallet.Core.Utxo.UtxoAccountScanner.ScansTaproot(chain.Value))
                    {
                        const Umbrella.Wallet.Core.Derivation.UtxoScriptKind taproot =
                            Umbrella.Wallet.Core.Derivation.UtxoScriptKind.Taproot;
                        ExportedXpubs.Add(new XpubRowVm(
                            symbol + " · Taproot",
                            Umbrella.Wallet.Core.Derivation.HdAddressDeriver.AccountXpubPath(chain.Value, taproot),
                            _deriver.DeriveAccountXpub(mnemonic, chain.Value, kind: taproot)));
                    }
                }
            }
            finally
            {
                // The phrase itself never stays in a field on this screen.
                XpubPassword = string.Empty;
            }

            OnPropertyChanged(nameof(HasExportedXpubs));
            if (IsUnlocked) PushActivity("Security", "Watch-only keys", "exported", string.Empty, "now");
        });
    }

    /// <summary>Clears the keys from the screen once they have been copied.</summary>
    [RelayCommand]
    private void HideXpubs()
    {
        ExportedXpubs.Clear();
        OnPropertyChanged(nameof(HasExportedXpubs));
    }

    /// <summary>Copies one key. Clipboard auto-clear applies here like everywhere else.</summary>
    [RelayCommand]
    private async Task CopyXpubAsync(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        await CopyTextAsync(key!);
        ShowToast(Loc.Instance["verify.copied"], isError: false);
    }

    /// <summary>The guide that explains what to do with these, and how to check the rest.</summary>
    public string VerifyGuideUrl =>
        "https://github.com/thefear078/UmbrellaWallet/blob/main/docs/VERIFY_YOUR_WALLET.md";
}
