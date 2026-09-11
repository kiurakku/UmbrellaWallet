using NBitcoin;
using NBitcoin.Altcoins;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Bitcoin Cash inherited Bitcoin's legacy address format byte-for-byte: a P2PKH address beginning
/// with "1" encodes the same key hash on both chains. That is why CashAddr exists, and it is a real
/// way to lose money — coins sent to the Bitcoin Cash side of an address whose owner is watching only
/// the Bitcoin side are usually unrecoverable.
///
/// This wallet is protected from it, but NOT by anything this wallet wrote: NBitcoin's BCash network
/// accepts CashAddr only and refuses the legacy form outright. The protection is therefore a property
/// of a dependency, which is exactly the kind of thing that disappears in a library upgrade without
/// anybody noticing.
///
/// So it is pinned here. If a future NBitcoin starts accepting legacy addresses on BCash, this fails,
/// and the wallet needs its own guard before that version ships.
/// </summary>
public sealed class BchAddressAmbiguityTests
{
    /// <summary>A well-known Bitcoin P2PKH address (the genesis coinbase).</summary>
    private const string BitcoinLegacy = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa";

    /// <summary>The same key hash in CashAddr — unambiguous, and what BCH wallets hand out.</summary>
    private const string BchCashAddr = "bitcoincash:qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a";

    [Fact]
    public void A_bitcoin_address_is_refused_as_a_bitcoin_cash_destination()
    {
        // The load-bearing assertion. The two chains agree on this address format, so only the
        // library declining to parse it keeps a Bitcoin deposit address out of a BCH send.
        Assert.Throws<System.FormatException>(
            () => BitcoinAddress.Create(BitcoinLegacy, BCash.Instance.Mainnet));
    }

    [Fact]
    public void The_same_address_is_perfectly_valid_on_bitcoin()
    {
        // Establishes that the refusal above is about the CHAIN, not about a malformed address.
        Assert.NotNull(BitcoinAddress.Create(BitcoinLegacy, Network.Main));
    }

    [Fact]
    public void A_cashaddr_destination_is_accepted()
    {
        Assert.NotNull(BitcoinAddress.Create(BchCashAddr, BCash.Instance.Mainnet));
    }

    [Fact]
    public void The_address_checker_calls_a_legacy_address_bitcoin()
    {
        // The two halves must agree. If the checker said "BCH" while the send path refused it, the
        // user would be told the address was fine and then watch the send fail for no stated reason.
        Assert.Equal("BTC", AddressInspector.Inspect(BitcoinLegacy).Network);
    }
}
