using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap P1.20 — the export that makes "verify, don't trust" mean something.
///
/// A user who has to take this wallet's word for their balance is trusting the same program that
/// tells them it is safe. An account xpub lets them ask an independent scanner the same question, so
/// the answer stops depending on this code being honest.
///
/// That only works if the exported key really is the one the wallet's own addresses hang off. An
/// xpub for a slightly different path would look perfectly valid, show a balance of zero in every
/// scanner, and teach the user that the check is broken — which is worse than not offering it.
/// </summary>
public sealed class AccountXpubTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static readonly HdAddressDeriver Deriver = new();

    /// <summary>
    /// The one that matters: addresses derived FROM THE XPUB, by somebody else's code path, are the
    /// addresses this wallet shows and spends from.
    /// </summary>
    [Theory]
    [InlineData(ChainId.Btc)]
    [InlineData(ChainId.Ltc)]
    [InlineData(ChainId.Doge)]
    [InlineData(ChainId.Bch)]
    public void Addresses_derived_from_the_xpub_are_the_wallets_own(ChainId chain)
    {
        var (_, _, network, scriptType) = HdAddressDeriver.BitcoinLikeParams(chain);
        var xpub = Deriver.DeriveAccountXpub(Phrase, chain);

        var account = ExtPubKey.Parse(xpub, network);

        for (uint index = 0; index < 3; index++)
        {
            // External chain (change = 0), the addresses a scanner walks first.
            var fromXpub = account
                .Derive(new KeyPath($"0/{index}"))
                .PubKey.GetAddress(scriptType, network).ToString();

            var fromWallet = Deriver.DeriveBitcoinLikeAt(Phrase, chain, change: 0, index).Address;

            Assert.Equal(fromWallet, fromXpub);
        }

        // And the internal (change) chain too — money that came back from a spend lives there, and a
        // scanner that only saw the external chain would report a balance short by exactly that.
        var change = account.Derive(new KeyPath("1/0")).PubKey.GetAddress(scriptType, network).ToString();
        Assert.Equal(Deriver.DeriveBitcoinLikeAt(Phrase, chain, change: 1, 0).Address, change);
    }

    [Fact]
    public void The_exported_path_is_the_account_level_the_addresses_hang_off()
    {
        Assert.Equal("m/84'/0'/0'", HdAddressDeriver.AccountXpubPath(ChainId.Btc));
        Assert.Equal("m/84'/2'/0'", HdAddressDeriver.AccountXpubPath(ChainId.Ltc));
        Assert.Equal("m/44'/3'/0'", HdAddressDeriver.AccountXpubPath(ChainId.Doge));
        Assert.Equal("m/44'/145'/0'", HdAddressDeriver.AccountXpubPath(ChainId.Bch));
    }

    /// <summary>
    /// A watch-only key must be watch-only. If an export ever carried private material it would turn
    /// a privacy trade-off into handing somebody the wallet.
    /// </summary>
    [Fact]
    public void The_export_cannot_spend()
    {
        var xpub = Deriver.DeriveAccountXpub(Phrase, ChainId.Btc);

        Assert.StartsWith("xpub", xpub, StringComparison.Ordinal);
        Assert.DoesNotContain("xprv", xpub, StringComparison.Ordinal);

        // Parsing it as a private key is not merely unwise; it is impossible.
        Assert.ThrowsAny<Exception>(() => ExtKey.Parse(xpub, Network.Main));
    }

    /// <summary>
    /// A hidden wallet is a different wallet, and its xpub has to be different too — otherwise the
    /// export would quietly point a third-party scanner at the wrong set of addresses.
    /// </summary>
    [Fact]
    public void A_passphrase_yields_a_different_account()
    {
        var plain = Deriver.DeriveAccountXpub(Phrase, ChainId.Btc);
        var hidden = Deriver.DeriveAccountXpub(Phrase, ChainId.Btc, passphrase: "second wallet");

        Assert.NotEqual(plain, hidden);
    }

    [Fact]
    public void The_same_phrase_always_exports_the_same_key()
    {
        Assert.Equal(
            Deriver.DeriveAccountXpub(Phrase, ChainId.Btc),
            Deriver.DeriveAccountXpub(Phrase, ChainId.Btc));
    }
}
