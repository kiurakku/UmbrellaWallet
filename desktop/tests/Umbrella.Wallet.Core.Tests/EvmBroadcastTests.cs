using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// What an EVM node's answer to a broadcast means, and the hash a signed transaction already has.
///
/// The hashes are two real Ethereum transactions — one legacy, one EIP-1559 — taken with their raw
/// bytes from a mainnet node, so the hash this wallet computes before sending is checked against the
/// hash the chain itself gave them.
///
/// The classification matters for money: a node that refuses on its own terms means nothing was sent
/// and the user may safely try again, while anything unclear must NOT be offered as a retry — the
/// transaction may be in the mempool with that nonce already.
/// </summary>
public sealed class EvmBroadcastTests
{
    [Theory]
    [InlineData(
        "0x02f8b101228477359400852e90edd0008303a98094ae7ab96520de3a18e5e111b5eaab095312d7fe8480b844a9059cbb00000000000000000000000062425cd6bdcb6bfe51558ea465b063486b70dc9f00000000000000000000000000000000000000000000000000b1a2bc2ec50000c080a05e3d3eb7010e5ca41cedafb66be621f06ce0b2a16c6fb596fdaa22006f3f91cca079bdbf8ffbb26c4a2e73c4435d801596176f9b4d060e2f793fa97f3da3028a09",
        "0xf4824e24cc54acc9494098af408e9bba7a1608d045bc3d2f9d6b6bca090d9ab1")]
    [InlineData(
        "0xf8ac83064ca884c376f23783016b3694dac17f958d2ee523a2206206994597c13d831ec780b844a9059cbb000000000000000000000000ecfca4b67bbda3b595dad4fbf402e8a640d762d4000000000000000000000000000000000000000000000000000000000a0eebb025a0b2dfd980ed3d6dac2e029e5dfdcf583e2c6c70ac3d0b092dd363a91c5f852af7a048cdf6feb7fa6d4a4e3b42fda9b3f3a631549f646c4ced946f3bcac892ce0a5f",
        "0x593906a9b3ff11880dfa706c128999f02e6139072ce051c70093522382426a8f")]
    public void A_signed_transactions_hash_is_the_one_the_chain_gives_it(string raw, string hash)
    {
        Assert.Equal(hash, EvmBroadcast.Hash(raw));
        Assert.Equal(hash, EvmBroadcast.Hash(raw[2..]));   // with or without the 0x
    }

    [Theory]
    [InlineData("already known")]
    [InlineData("ALREADY_EXISTS: already known")]
    [InlineData("known transaction: 0xabc")]
    [InlineData("Transaction already in pool")]
    public void A_node_that_already_has_it_has_taken_it(string message) =>
        Assert.Equal(EvmBroadcastAnswer.Accepted, EvmBroadcast.Classify(message));

    [Theory]
    [InlineData("insufficient funds for gas * price + value")]
    [InlineData("intrinsic gas too low")]
    [InlineData("transaction underpriced")]
    [InlineData("invalid sender")]
    [InlineData("max fee per gas less than block base fee")]
    [InlineData("exceeds block gas limit")]
    public void A_refusal_on_the_nodes_own_terms_means_nothing_was_sent(string message) =>
        Assert.Equal(EvmBroadcastAnswer.Rejected, EvmBroadcast.Classify(message));

    [Theory]
    [InlineData("nonce too low")]                    // this account's earlier transaction may be mined
    [InlineData("replacement transaction underpriced")]
    [InlineData("some error no wallet has seen before")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_unclear_and_never_a_retry(string? message) =>
        Assert.Equal(EvmBroadcastAnswer.Unclear, EvmBroadcast.Classify(message));
}
