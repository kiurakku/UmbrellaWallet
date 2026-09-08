using NBitcoin;
using NBitcoin.DataEncoders;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Zcash TRANSPARENT (t-addr) derivation, pinned so it can never drift. A t-addr is an ordinary P2PKH
/// (BIP44 m/44'/133') differing from Bitcoin only in Zcash's two-byte mainnet version prefix 0x1CB8
/// (the "t1" leader), Base58Check with a double-SHA256 checksum.
///
/// The correctness proof is non-circular: the address's decoded key-hash must equal the Hash160 that
/// NBitcoin independently computes for the SAME derived key's Bitcoin P2PKH address. That pins the
/// derivation path AND the key, while the prefix/length checks pin the Zcash-specific encoding — so a
/// t-addr shown here is exactly what any BIP44 wallet (coin type 133) would recover the funds under.
/// </summary>
public sealed class ZecDerivationTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static readonly HdAddressDeriver Deriver = new();

    [Fact]
    public void Zec_transparent_address_matches_the_standard_vector()
    {
        var acct = Deriver.DeriveReceiveAddress(Phrase, ChainId.Zec, addressIndex: 0);

        Assert.StartsWith("t1", acct.Address);
        Assert.Equal(35, acct.Address.Length);
        Assert.Equal("t1XVXWCvpMgBvUaed4XDqWtgQgJSu1Ghz7F", acct.Address);
    }

    [Fact]
    public void The_t_addr_encodes_the_same_key_hash_as_the_bitcoin_p2pkh_for_that_key()
    {
        var acct = Deriver.DeriveReceiveAddress(Phrase, ChainId.Zec, addressIndex: 0);

        // Independent derivation of the SAME leaf key via NBitcoin, then its Bitcoin mainnet P2PKH —
        // whose Hash160 is computed by NBitcoin's own address code, not by the deriver under test.
        var root = new Mnemonic(Phrase, Wordlist.English).DeriveExtKey();
        var key = root.Derive(new KeyPath("44'/133'/0'/0/0"));
        var btcP2pkh = (BitcoinPubKeyAddress)key.PrivateKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main);
        var expectedHash160 = btcP2pkh.Hash.ToBytes();

        // Decode the t-addr: [0x1C,0xB8] ++ hash160 ++ 4-byte checksum.
        var decoded = Encoders.Base58Check.DecodeData(acct.Address);
        Assert.Equal(22, decoded.Length);           // 2 version bytes + 20-byte hash
        Assert.Equal(0x1C, decoded[0]);
        Assert.Equal(0xB8, decoded[1]);
        Assert.Equal(expectedHash160, decoded[2..]);
    }

    [Fact]
    public void Different_indices_give_different_t_addresses()
    {
        var a0 = Deriver.DeriveReceiveAddress(Phrase, ChainId.Zec, addressIndex: 0).Address;
        var a1 = Deriver.DeriveReceiveAddress(Phrase, ChainId.Zec, addressIndex: 1).Address;

        Assert.NotEqual(a0, a1);
        Assert.All(new[] { a0, a1 }, a => Assert.StartsWith("t1", a));
    }
}
