using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Sign &amp; verify message: prove control of the wallet's Ethereum address (EIP-191 personal_sign)
/// without moving any funds, and check anyone's signed message. Split out of MainViewModel.cs as a
/// partial (roadmap §8.3.1) — same type, no behaviour moved with it.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The 0x address messages are signed with (the wallet's Ethereum account).</summary>
    public string SignMsgAddress
    {
        get
        {
            if (string.IsNullOrEmpty(_unlockedMnemonic)) return string.Empty;
            try { return _deriver.DeriveReceiveAddress(_unlockedMnemonic!, ChainId.Eth).Address; }
            catch { return string.Empty; }
        }
    }

    [ObservableProperty] private string _signMsgInput = string.Empty;
    [ObservableProperty] private string _signMsgSignature = string.Empty;

    /// <summary>Signs the message with the wallet's Ethereum key (derived only for the signing, then
    /// zeroed). A signature proves control of the address; it can never move funds (EIP-191 prefix).</summary>
    [RelayCommand]
    private void SignMessage()
    {
        SignMsgSignature = string.Empty;
        if (string.IsNullOrEmpty(_unlockedMnemonic) || string.IsNullOrWhiteSpace(SignMsgInput)) return;
        byte[]? key = null;
        try
        {
            key = _deriver.DeriveEthereumPrivateKey(_unlockedMnemonic!);
            SignMsgSignature = EthMessageSigner.Sign(key, SignMsgInput);
            ShowToast(Loc.Instance["signmsg.signed"], isError: false);
        }
        catch
        {
            ShowToast(Loc.Instance["signmsg.signFail"], isError: true);
        }
        finally
        {
            if (key is not null) Array.Clear(key, 0, key.Length);
        }
    }

    [RelayCommand]
    private async Task CopySignature()
    {
        if (string.IsNullOrWhiteSpace(SignMsgSignature)) return;
        await CopyTextAsync(SignMsgSignature);
        ShowToast(Loc.Instance["toast.copied"], isError: false);
    }

    // --- Verify anyone's signed message (public: address + message + signature only) ---
    [ObservableProperty] private string _verifyMsgAddress = string.Empty;
    [ObservableProperty] private string _verifyMsgText = string.Empty;
    [ObservableProperty] private string _verifyMsgSignature = string.Empty;
    [ObservableProperty] private string _verifyMsgResult = string.Empty;
    [ObservableProperty] private bool _verifyMsgOk;

    /// <summary>Green when the last check passed, red when it didn't — the string is auto-converted to a brush.</summary>
    public string VerifyMsgColor => VerifyMsgOk ? "#8FCB9B" : "#E09A9A";
    partial void OnVerifyMsgOkChanged(bool value) => OnPropertyChanged(nameof(VerifyMsgColor));

    [RelayCommand]
    private void VerifyMessage()
    {
        VerifyMsgResult = string.Empty;
        if (string.IsNullOrWhiteSpace(VerifyMsgAddress) || string.IsNullOrWhiteSpace(VerifyMsgSignature))
        {
            VerifyMsgOk = false;
            VerifyMsgResult = Loc.Instance["signmsg.verifyNeed"];
            return;
        }
        VerifyMsgOk = EthMessageSigner.Verify(VerifyMsgAddress.Trim(), VerifyMsgText, VerifyMsgSignature.Trim());
        VerifyMsgResult = VerifyMsgOk ? Loc.Instance["signmsg.valid"] : Loc.Instance["signmsg.invalid"];
    }
}
