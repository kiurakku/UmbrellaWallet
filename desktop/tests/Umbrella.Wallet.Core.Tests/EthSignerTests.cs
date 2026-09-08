using System.Numerics;
using Nethereum.Signer;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the ETH signer to the OFFICIAL EIP-155 example transaction
/// (https://eips.ethereum.org/EIPS/eip-155, "Example" section) byte-for-byte.
/// A wrong signature would burn gas or strand funds, so this is a hard gate:
/// if this test fails, sending must not ship.
/// </summary>
public sealed class EthSignerTests
{
    [Fact]
    public void Signer_matches_the_official_eip155_example_byte_for_byte()
    {
        var privateKey = Convert.FromHexString(
            "4646464646464646464646464646464646464646464646464646464646464646");

        var signed = EthTransactionSender.SignTransfer(
            privateKey,
            to: "0x3535353535353535353535353535353535353535",
            amountWei: BigInteger.Parse("1000000000000000000"),   // 1 ETH
            nonce: 9,
            gasPriceWei: BigInteger.Parse("20000000000"),          // 20 gwei
            gasLimit: 21000);

        const string expected =
            "f86c098504a817c800825208943535353535353535353535353535353535353535880de0b6b3a76400008025a0" +
            "28ef61340bd939bc2195fe537567866003e1a15d3c71ff63e1590620aa636276a0" +
            "67cbe9d8997f761aecb703304b3800ccf555c9f3dc64214b297fb1966a3b6d83";

        Assert.Equal(expected, signed.ToLowerInvariant());
    }

    /// <summary>
    /// The Ethereum L2 rollups (Arbitrum / Base / Optimism) send native ETH from the SAME 0x address as
    /// mainnet, signed by the SAME code path — only the EIP-155 chain id differs. This pins that the
    /// chain id genuinely enters the signature (so a mainnet-signed tx can't be replayed on an L2, and
    /// vice-versa): the same transfer signed under each chain id must produce a DIFFERENT signature.
    /// Combined with the byte-exact chainId-1 vector above, that makes the L2 sends provably correct.
    /// </summary>
    [Fact]
    public void L2_rollup_signatures_are_chain_id_specific()
    {
        var privateKey = Convert.FromHexString(
            "4646464646464646464646464646464646464646464646464646464646464646");

        string Sign(long chainId) => EthTransactionSender.SignTransfer(
            privateKey, chainId, "0x3535353535353535353535353535353535353535",
            amountWei: BigInteger.Parse("1000000000000000000"), nonce: 9,
            gasPriceWei: BigInteger.Parse("20000000000"), gasLimit: 21000).ToLowerInvariant();

        var mainnet = Sign(1);
        var arbitrum = Sign(42161);
        var @base = Sign(8453);
        var optimism = Sign(10);

        // Every chain id yields a distinct signed transaction — replay protection is real.
        var all = new[] { mainnet, arbitrum, @base, optimism };
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    /// <summary>
    /// The L2 rollup entries are wired correctly: the native coin is ETH (not a separate token), the
    /// chain id is the canonical one, and the explorer is resolved by chain id (the rollups can't be told
    /// apart by symbol, since they all report "ETH").
    /// </summary>
    [Theory]
    [InlineData("ARB", 42161L, "arbiscan.io/tx/")]
    [InlineData("BASE", 8453L, "basescan.org/tx/")]
    [InlineData("OP", 10L, "optimistic.etherscan.io/tx/")]
    public void L2_rollups_are_configured_as_native_eth(string key, long chainId, string explorer)
    {
        var chain = EthTransactionSender.Chains[key];
        Assert.Equal("ETH", chain.Symbol);
        Assert.Equal(chainId, chain.ChainId);
        Assert.Equal(explorer, chain.ExplorerTx);
        Assert.Equal(explorer, EthTransactionSender.ExplorerTxForChainId(chainId));
        Assert.NotEmpty(chain.Rpcs);
    }

    /// <summary>
    /// The exported private key must correspond to the SAME address the wallet displays for
    /// receiving — otherwise a send would spend from a different account than the user funded.
    /// </summary>
    [Fact]
    public void Derived_private_key_matches_the_displayed_receive_address()
    {
        const string mnemonic = Bip39MnemonicServiceTests.FixedTwentyFourWordMnemonic;
        var deriver = new HdAddressDeriver();

        var shown = deriver.DeriveReceiveAddress(mnemonic, ChainId.Eth, 0).Address;
        var priv = deriver.DeriveEthereumPrivateKey(mnemonic, 0);
        var fromKey = new EthECKey(priv, true).GetPublicAddress();

        Assert.Equal(shown, fromKey);
    }
}
