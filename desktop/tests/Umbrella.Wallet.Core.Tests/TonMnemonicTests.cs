using Umbrella.Wallet.Core.Seed;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins TON-native mnemonic import (Telegram Wallet / Tonkeeper) to a reference vector produced by
/// @ton/crypto + @ton/ton (WalletContractV4). If this passes, an imported TON address matches exactly
/// what those wallets display.
/// </summary>
public sealed class TonMnemonicTests
{
    // Freshly generated with @ton/crypto mnemonicNew(); address from @ton/ton WalletContractV4 (UQ form).
    private const string ReferenceMnemonic =
        "derive earn future trumpet gallery nation antique cabin seat object rival wrong thank humor " +
        "glide mobile reduce often scale beauty youth base horn brisk";
    private const string ReferencePublicKeyHex =
        "1660ae94eee3f409c0f395c6fc753dca1a8be152a42baf04bb9f23e34f8e2294";
    private const string ReferenceAddress = "UQCLhuuBbuTzBY7oDkmYAF8vhnB7c2f0XDDWUflQUvHdLYFm";

    [Fact]
    public void IsTonMnemonic_AcceptsAValidTonPhrase()
    {
        Assert.True(TonMnemonic.IsTonMnemonic(ReferenceMnemonic));
    }

    [Fact]
    public void DeriveWallet_MatchesTonkeeperReferenceVector()
    {
        var (address, publicKey) = TonMnemonic.DeriveWallet(ReferenceMnemonic);

        Assert.Equal(ReferencePublicKeyHex, Convert.ToHexString(publicKey).ToLowerInvariant());
        Assert.Equal(ReferenceAddress, address);
    }

    [Fact]
    public void IsTonMnemonic_RejectsABip39Phrase()
    {
        // Canonical BIP39 12-word phrase — valid BIP39, but NOT a TON mnemonic.
        Assert.False(TonMnemonic.IsTonMnemonic(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about"));
    }

    [Fact]
    public void IsTonMnemonic_RejectsWrongWordCount()
    {
        Assert.False(TonMnemonic.IsTonMnemonic("derive earn future trumpet gallery nation"));
    }
}
