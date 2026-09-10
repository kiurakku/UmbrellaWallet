using Nethereum.Signer;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins Ethereum message signing (EIP-191): a genuine signature recovers to the signer's address and
/// verifies (case-insensitively), while a tampered message, a wrong address, or a malformed signature
/// all fail — and Verify never throws on bad input.
/// </summary>
public sealed class EthMessageSignerTests
{
    private static readonly byte[] Priv = MakeKey();
    private static byte[] MakeKey() { var k = new byte[32]; k[31] = 42; return k; } // any fixed non-zero key
    private static string Address => new EthECKey(Priv, true).GetPublicAddress();

    [Fact]
    public void Sign_then_recover_returns_the_signer_address()
    {
        var sig = EthMessageSigner.Sign(Priv, "gm");
        Assert.Equal(Address.ToLowerInvariant(), EthMessageSigner.RecoverAddress("gm", sig).ToLowerInvariant());
    }

    [Fact]
    public void Verify_is_true_for_a_genuine_signature_regardless_of_address_case()
    {
        var sig = EthMessageSigner.Sign(Priv, "I own this address");
        Assert.True(EthMessageSigner.Verify(Address, "I own this address", sig));
        Assert.True(EthMessageSigner.Verify(Address.ToLowerInvariant(), "I own this address", sig));
    }

    [Fact]
    public void Verify_is_false_on_a_tampered_message_wrong_address_or_garbage_signature()
    {
        var sig = EthMessageSigner.Sign(Priv, "hello");
        Assert.False(EthMessageSigner.Verify(Address, "hello!", sig));                                   // message changed
        Assert.False(EthMessageSigner.Verify("0x0000000000000000000000000000000000000001", "hello", sig)); // wrong address
        Assert.False(EthMessageSigner.Verify(Address, "hello", "0xdeadbeef"));                            // malformed sig, no throw
        Assert.False(EthMessageSigner.Verify("", "hello", sig));                                          // no address
    }
}
