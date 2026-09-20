using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Sending an arbitrary ERC-20 (roadmap N.1).
///
/// Until now the wallet could send native coins and exactly one token — USDT on TRON, hardcoded.
/// Every other token it displayed was money the user could see and not move, which is the same
/// complaint as an address the wallet issues and cannot spend from: the balance is real, the wallet
/// is the thing in the way.
///
/// The routing key is the CONTRACT, never the ticker. Two contracts can call themselves USDC, and
/// only one of them is the one you hold; a picker keyed on "USDC" would eventually build a transfer
/// against the wrong one. So a token entry's key is <c>ERC20:0x…</c> and the ticker is only ever
/// shown, never matched on.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The prefix that marks a picker entry as an ERC-20 rather than a native coin.</summary>
    public const string TokenSendPrefix = "ERC20:";

    /// <summary>The contract a picker key refers to, or null when the key is a native coin.</summary>
    public static string? ContractFromSendKey(string? key) =>
        key is not null && key.StartsWith(TokenSendPrefix, StringComparison.OrdinalIgnoreCase)
            ? key[TokenSendPrefix.Length..]
            : null;

    /// <summary>
    /// Everything the Send picker offers: the native coins this build can broadcast, plus every
    /// ERC-20 currently held with a contract and decimals the wallet actually read.
    ///
    /// Rebuilt from the holdings, so a token that arrives becomes sendable and one that is spent to
    /// zero drops off — the picker never offers what is not there, and never hides what is.
    /// </summary>
    public ObservableCollection<SendOption> SendableAssetOptions { get; } = new();

    /// <summary>
    /// Refreshes the picker, keeping whatever was selected selected.
    ///
    /// A rebuilt ItemsSource drops the selection, and a Send screen that silently switches which
    /// asset it is about — between typing an amount and pressing Review — is how somebody sends the
    /// wrong coin. So the selection is matched back by key, and only falls back to the first entry
    /// when the asset it pointed at genuinely went away.
    /// </summary>
    private void RebuildSendableAssets()
    {
        var previouslySelected = SelectedSendAsset?.Symbol;

        var tokens = Accounts
            .Where(a => a.IsSpendableToken)
            // ERC-20 only for now: the TRON and TON token paths are separate work (N.2, N.3), and
            // offering them here would promise a send this build cannot make.
            .Where(a => a.Derivation.StartsWith("ERC20", StringComparison.OrdinalIgnoreCase))
            .Where(a => a.Amount > 0)
            // An unsolicited airdrop token is usually a lure; it stays visible in Holdings behind the
            // spam fold, but it does not get promoted into the send picker.
            .Where(a => !a.IsSuspectedSpam)
            .GroupBy(a => a.Contract, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(a => new SendOption(
                TokenSendPrefix + a.Contract,
                a.Name,
                Loc.Instance["send.erc20Network"],
                Ticker: a.Symbol))
            .ToList();

        SendableAssetOptions.Clear();
        foreach (var option in SendableAssets) SendableAssetOptions.Add(option);
        foreach (var token in tokens) SendableAssetOptions.Add(token);

        var restored = previouslySelected is null
            ? null
            : SendableAssetOptions.FirstOrDefault(
                o => o.Symbol.Equals(previouslySelected, StringComparison.OrdinalIgnoreCase));

        SelectedSendAsset = restored ?? SendableAssetOptions.FirstOrDefault();
    }

    /// <summary>The holdings row for a token picker key, or null when it is no longer held.</summary>
    private WalletAccountViewModel? TokenAccountFor(string sendKey)
    {
        var contract = ContractFromSendKey(sendKey);
        if (contract is null) return null;

        return Accounts.FirstOrDefault(a =>
            a.IsSpendableToken && a.Contract.Equals(contract, StringComparison.OrdinalIgnoreCase));
    }
}
