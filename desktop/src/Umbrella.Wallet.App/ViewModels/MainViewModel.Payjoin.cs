using System;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Umbrella.Wallet.Core.Payments;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Payment links and PayJoin on the Send screen (roadmap P2.2).
///
/// A pasted <c>bitcoin:</c> link fills in the address and amount and selects Bitcoin. If it offers a
/// PayJoin endpoint the wallet is willing to use, that endpoint is remembered for exactly that address:
/// editing the destination forgets it, so a PayJoin can never be attempted against an address the link
/// did not name.
/// </summary>
public partial class MainViewModel
{
    private Uri? _payjoinEndpoint;
    private string? _payjoinForAddress;
    private bool _applyingPaymentLink;

    /// <summary>Set at Review when the plan qualifies; Confirm only attempts a PayJoin the user was
    /// shown.</summary>
    private bool _payjoinPlanned;

    [ObservableProperty] private string _sendPayjoinNote = string.Empty;

    public bool HasSendPayjoinNote => SendPayjoinNote.Length > 0;

    partial void OnSendPayjoinNoteChanged(string value) => OnPropertyChanged(nameof(HasSendPayjoinNote));

    /// <summary>
    /// Turns a pasted payment link into the fields it describes. Returns true when the destination was
    /// a link and has been handled (successfully or with a stated refusal), so the caller skips
    /// validating the raw link text as if it were an address.
    /// </summary>
    private bool TryApplyPaymentLink(string value)
    {
        if (_applyingPaymentLink || !Bip21Uri.LooksLikeOne(value)) return false;

        if (!Bip21Uri.TryParse(value, out var link, out var error) || link is null)
        {
            // Left in the field on purpose: the user should see what was refused, not an empty box.
            SendError = string.Format(Loc.Instance["send.linkRefused"], error);
            ForgetPayjoin();
            return true;
        }

        _applyingPaymentLink = true;
        try
        {
            var bitcoin = SendableAssets.FirstOrDefault(a =>
                string.Equals(a.Symbol, "BTC", StringComparison.OrdinalIgnoreCase));
            if (bitcoin is not null) SelectedSendAsset = bitcoin;

            SendTo = link.Address;
            if (link.Amount is { } amount)
                SendAmount = amount.ToString("0.########", CultureInfo.InvariantCulture);
        }
        finally
        {
            _applyingPaymentLink = false;
        }

        _payjoinEndpoint = link.PayjoinEndpoint;
        // Tracked whenever a note is shown, so editing the destination clears an "ignored" note too.
        _payjoinForAddress = link.PayjoinEndpoint is null && link.PayjoinIgnoredReason is null ? null : link.Address;
        SendPayjoinNote = link.PayjoinEndpoint is not null
            ? Loc.Instance["send.payjoinAvailable"]
            : link.PayjoinIgnoredReason is not null
                ? Loc.Instance["send.payjoinIgnored"]
                : string.Empty;

        // Address validation and the scam checks already ran when SendTo was set to the address above.
        return true;
    }

    /// <summary>The endpoint belongs to one address. Any other destination drops it.</summary>
    private void ForgetPayjoinIfDestinationChanged(string value)
    {
        if (_applyingPaymentLink || _payjoinForAddress is null) return;
        if (!string.Equals(value.Trim(), _payjoinForAddress, StringComparison.Ordinal)) ForgetPayjoin();
    }

    private void ForgetPayjoin()
    {
        _payjoinEndpoint = null;
        _payjoinForAddress = null;
        _payjoinPlanned = false;
        SendPayjoinNote = string.Empty;
    }

    /// <summary>The endpoint to use for this Review, or null: only for Bitcoin, only for the address the
    /// link named.</summary>
    private Uri? PayjoinEndpointFor(string chain) =>
        chain == "BTC" &&
        _payjoinEndpoint is not null &&
        string.Equals(SendTo?.Trim(), _payjoinForAddress, StringComparison.Ordinal)
            ? _payjoinEndpoint
            : null;
}
