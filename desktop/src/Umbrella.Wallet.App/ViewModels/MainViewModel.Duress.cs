using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// The duress password (roadmap P1.1): a second password that opens a different, real wallet.
///
/// The situation it is for is somebody standing over you while the wallet is unlocked. What it buys
/// is that the password you hand over works — a wallet opens, with its own addresses and its own
/// history — and it is not the one holding your money.
///
/// What it does not buy is stated beside it, every time. It does not hide that other wallets exist on
/// this computer, it does nothing about somebody watching you type, and it cannot help if the same
/// person compares a backup of this file taken before and after. A protection believed to cover more
/// than it does is worse than no protection, because it is acted on.
///
/// The wallet can never tell you whether a duress password is set: that is the entire point of the
/// storage format, and an "enabled" badge in Settings would undo it. So the screen offers to set one
/// and to remove one, and reports neither state.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty] private string _duressVaultPassword = string.Empty;
    [ObservableProperty] private string _duressNewPassword = string.Empty;
    [ObservableProperty] private string _duressConfirmPassword = string.Empty;
    [ObservableProperty] private string _duressStatus = string.Empty;
    [ObservableProperty] private string _duressError = string.Empty;

    /// <summary>The decoy's recovery phrase, shown once so it can be written down — it is a real
    /// wallet, and a wallet whose phrase nobody has is a wallet that cannot be restored.</summary>
    [ObservableProperty] private string _duressRevealedPhrase = string.Empty;

    public bool HasDuressPhrase => DuressRevealedPhrase.Length > 0;

    partial void OnDuressRevealedPhraseChanged(string value) => OnPropertyChanged(nameof(HasDuressPhrase));

    private void ClearDuressFields()
    {
        DuressVaultPassword = string.Empty;
        DuressNewPassword = string.Empty;
        DuressConfirmPassword = string.Empty;
    }

    /// <summary>
    /// Creates the decoy wallet: a fresh 24-word phrase of its own, stored in this vault's second
    /// slot under the duress password.
    ///
    /// A fresh seed rather than one of the user's existing wallets, deliberately. A decoy that is
    /// another wallet of theirs is not a decoy — handing it over hands over real money.
    /// </summary>
    [RelayCommand]
    private async Task SetDuressWalletAsync()
    {
        DuressError = string.Empty;
        DuressStatus = string.Empty;
        DuressRevealedPhrase = string.Empty;

        if (DuressNewPassword.Length < MinPasswordLength)
        {
            DuressError = string.Format(Loc.Instance["duress.errShort"], MinPasswordLength);
            return;
        }

        if (!string.Equals(DuressNewPassword, DuressConfirmPassword, StringComparison.Ordinal))
        {
            DuressError = Loc.Instance["duress.errMismatch"];
            return;
        }

        if (string.Equals(DuressNewPassword, DuressVaultPassword, StringComparison.Ordinal))
        {
            // Caught here as well as in the vault, so the user reads a sentence rather than an
            // exception — and because a duress password equal to the real one does nothing at all
            // while looking like it worked.
            DuressError = Loc.Instance["duress.errSame"];
            return;
        }

        await RunBusyAsync(async () =>
        {
            var decoy = _mnemonics.Generate();
            try
            {
                await _vault.AddDuressWalletAsync(DuressVaultPassword, decoy, DuressNewPassword);
            }
            catch (UnauthorizedAccessException)
            {
                DuressError = Loc.Instance["duress.errWrongPw"];
                return;
            }
            catch (InvalidOperationException ex)
            {
                DuressError = ex.Message;
                return;
            }

            // Shown once. It is a real wallet: if the user ever puts anything in it — and a decoy
            // with nothing in it is not convincing — losing this phrase loses that too.
            DuressRevealedPhrase = decoy;
            DuressStatus = Loc.Instance["duress.set"];
            ClearDuressFields();
            if (IsUnlocked) PushActivity("Security", "Duress", "set", string.Empty, "now");
        });
    }

    /// <summary>
    /// Puts the second slot back to noise. It cannot report whether anything was there — "there never
    /// was one" and "there was one and it is gone" are the same answer, which is what the format is
    /// for.
    /// </summary>
    [RelayCommand]
    private async Task RemoveDuressWalletAsync()
    {
        DuressError = string.Empty;
        DuressStatus = string.Empty;
        DuressRevealedPhrase = string.Empty;

        await RunBusyAsync(async () =>
        {
            try
            {
                await _vault.RemoveDuressWalletAsync(DuressVaultPassword);
            }
            catch (UnauthorizedAccessException)
            {
                DuressError = Loc.Instance["duress.errWrongPw"];
                return;
            }
            catch (InvalidOperationException ex)
            {
                DuressError = ex.Message;
                return;
            }

            DuressStatus = Loc.Instance["duress.removed"];
            ClearDuressFields();
            if (IsUnlocked) PushActivity("Security", "Duress", "removed", string.Empty, "now");
        });
    }

    /// <summary>Clears the decoy phrase from the screen once it has been written down.</summary>
    [RelayCommand]
    private void HideDuressPhrase() => DuressRevealedPhrase = string.Empty;
}
