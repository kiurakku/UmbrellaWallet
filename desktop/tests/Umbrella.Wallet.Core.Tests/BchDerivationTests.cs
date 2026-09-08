using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Bitcoin Cash derivation, pinned so it can never drift. BCH is BIP44 (m/44'/145'), P2PKH, encoded
/// as CashAddr — derived through NBitcoin.Altcoins' BCash network, the same reference path already
/// trusted for Litecoin and Dogecoin. The receive address for the standard test mnemonic matches the
/// value every BIP44 tool produces, and the change/index deriver agrees with the simple deriver at #0.
/// </summary>
public sealed class BchDerivationTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static readonly HdAddressDeriver Deriver = new();

    [Fact]
    public void Bch_receive_address_matches_the_standard_vector()
    {
        var acct = Deriver.DeriveReceiveAddress(Phrase, ChainId.Bch, addressIndex: 0);

        Assert.Equal("bitcoincash:qqyx49mu0kkn9ftfj6hje6g2wfer34yfnq5tahq3q6", acct.Address);
        Assert.StartsWith("bitcoincash:q", acct.Address);
    }

    [Fact]
    public void The_change_index_deriver_agrees_with_the_simple_one_at_zero()
    {
        var simple = Deriver.DeriveReceiveAddress(Phrase, ChainId.Bch, addressIndex: 0).Address;
        var atZero = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Bch, change: 0, index: 0).Address;

        Assert.Equal(simple, atZero);
    }

    [Fact]
    public void Different_indices_give_different_addresses_all_cashaddr()
    {
        var a0 = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Bch, 0, 0).Address;
        var a1 = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Bch, 0, 1).Address;
        var change0 = Deriver.DeriveBitcoinLikeAt(Phrase, ChainId.Bch, 1, 0).Address;

        Assert.NotEqual(a0, a1);
        Assert.NotEqual(a0, change0);
        Assert.All(new[] { a0, a1, change0 }, a => Assert.StartsWith("bitcoincash:q", a));
    }
}
