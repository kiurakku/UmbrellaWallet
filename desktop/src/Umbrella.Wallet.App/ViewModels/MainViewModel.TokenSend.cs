using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Sending an arbitrary token: ERC-20 on Ethereum (roadmap N.1) and TRC-20 on TRON (N.2).
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

    /// <summary>The same, for a TRC-20 on TRON (roadmap N.2).</summary>
    public const string TronTokenSendPrefix = "TRC20:";

    /// <summary>And for a jetton on TON (roadmap N.3). The key is the jetton MASTER, which identifies
    /// the token; the message goes to the sender's own jetton wallet, carried on the row.</summary>
    public const string JettonSendPrefix = "JETTON:";

    /// <summary>And for an SPL token on Solana (roadmap N.3). The key is the MINT; the transfer moves
    /// it between the two wallets' associated token accounts, derived from the mint.</summary>
    public const string SplSendPrefix = "SPL:";

    /// <summary>The contract a picker key refers to, or null when the key is a native coin.</summary>
    public static string? ContractFromSendKey(string? key)
    {
        if (key is null) return null;
        if (key.StartsWith(TokenSendPrefix, StringComparison.OrdinalIgnoreCase))
            return key[TokenSendPrefix.Length..];
        if (key.StartsWith(TronTokenSendPrefix, StringComparison.OrdinalIgnoreCase))
            return key[TronTokenSendPrefix.Length..];
        if (key.StartsWith(JettonSendPrefix, StringComparison.OrdinalIgnoreCase))
            return key[JettonSendPrefix.Length..];
        if (key.StartsWith(SplSendPrefix, StringComparison.OrdinalIgnoreCase))
            return key[SplSendPrefix.Length..];

        return null;
    }

    /// <summary>True when a picker key names a TRC-20 — the fee comes out of TRX, not ETH, and the
    /// transaction is built by TRON's own API rather than signed locally as an EVM transfer.</summary>
    public static bool IsTronTokenKey(string? key) =>
        key is not null && key.StartsWith(TronTokenSendPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a picker key names a jetton — the message goes to a jetton wallet on TON
    /// with TON attached for gas, which is a different shape again.</summary>
    public static bool IsJettonKey(string? key) =>
        key is not null && key.StartsWith(JettonSendPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a picker key names an SPL token — the fee (and any new account's rent) comes
    /// out of SOL, and the transfer is a TransferChecked between associated token accounts.</summary>
    public static bool IsSplKey(string? key) =>
        key is not null && key.StartsWith(SplSendPrefix, StringComparison.OrdinalIgnoreCase);

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
            // ERC-20, TRC-20 and jettons. A jetton additionally needs its jetton-wallet address:
            // without it the token can be shown but not sent, and offering it would be a promise the
            // send path cannot keep.
            .Where(a => a.Derivation.StartsWith("ERC20", StringComparison.OrdinalIgnoreCase)
                        || a.Derivation.StartsWith("TRC20", StringComparison.OrdinalIgnoreCase)
                        || (a.Derivation.StartsWith("Jetton", StringComparison.OrdinalIgnoreCase)
                            && a.IsSpendableJetton)
                        // An SPL token only when its row says Ready: a Token-2022 mint is shown, not offered.
                        || (a.Derivation.StartsWith("SPL", StringComparison.OrdinalIgnoreCase)
                            && a.SupportStatus == "Ready"))
            .Where(a => a.Amount > 0)
            // An unsolicited airdrop token is usually a lure; it stays visible in Holdings behind the
            // spam fold, but it does not get promoted into the send picker.
            .Where(a => !a.IsSuspectedSpam)
            .GroupBy(a => a.Contract, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(a =>
            {
                var tron = a.Derivation.StartsWith("TRC20", StringComparison.OrdinalIgnoreCase);
                var jetton = a.Derivation.StartsWith("Jetton", StringComparison.OrdinalIgnoreCase);
                var spl = a.Derivation.StartsWith("SPL", StringComparison.OrdinalIgnoreCase);
                var prefix = spl ? SplSendPrefix : jetton ? JettonSendPrefix : tron ? TronTokenSendPrefix : TokenSendPrefix;
                var network = spl ? "send.splNetwork" : jetton ? "send.jettonNetwork" : tron ? "send.trc20Network" : "send.erc20Network";

                return new SendOption(prefix + a.Contract, a.Name, Loc.Instance[network], Ticker: a.Symbol);
            })
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
