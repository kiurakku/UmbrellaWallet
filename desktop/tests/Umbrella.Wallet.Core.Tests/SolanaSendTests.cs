using System.Globalization;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Reading a Solana node's answers for a send, on the shapes api.mainnet-beta.solana.com gives today.
/// The point of each: a transaction is final only once a supermajority has voted on its block, and
/// "not seen" is only final once its blockhash can no longer be used.
/// </summary>
public sealed class SolanaSendTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Theory]
    [InlineData("1", 1_000_000_000UL)]
    [InlineData("0.000000001", 1UL)]
    [InlineData("12.5", 12_500_000_000UL)]
    public void Amounts_become_whole_lamports(string sol, ulong lamports)
    {
        Assert.True(SolanaRpc.TryToLamports(decimal.Parse(sol, CultureInfo.InvariantCulture), out var l));
        Assert.Equal(lamports, l);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0000000001")]   // a tenth of a lamport: it used to be truncated away silently
    public void Amounts_that_are_not_whole_lamports_are_refused(string sol) =>
        Assert.False(SolanaRpc.TryToLamports(decimal.Parse(sol, CultureInfo.InvariantCulture), out _));

    [Fact]
    public void A_result_or_an_error_is_unwrapped_and_nothing_else_is_invented()
    {
        var (result, error) = SolanaRpc.Unwrap(Json("""{"jsonrpc":"2.0","result":{"context":{"slot":1},"value":42},"id":1}"""));
        Assert.Equal(42UL, SolanaRpc.ParseLamports(result!.Value));
        Assert.Null(error);

        (result, error) = SolanaRpc.Unwrap(Json("""
            {"jsonrpc":"2.0","error":{"code":-32002,"message":"Transaction simulation failed: Attempt to debit an account but found no record of a prior credit."},"id":1}
            """));
        Assert.Null(result);
        Assert.StartsWith("Transaction simulation failed", error);

        Assert.Equal((null, null), SolanaRpc.Unwrap(Json("""{"jsonrpc":"2.0","id":1}""")));
        Assert.Equal(890880UL, SolanaRpc.ParseLamports(Json("890880")));
        Assert.Null(SolanaRpc.ParseLamports(Json("""{"value":"42"}""")));
    }

    [Fact]
    public void A_blockhash_comes_with_the_last_height_it_is_valid_for()
    {
        var parsed = SolanaRpc.ParseBlockhash(Json("""
            {"context":{"slot":360000000},"value":{"blockhash":"EkSnNWid2cvwEVnVx9aBqawnmiCNiDgp3gUdkDPTKN1N","lastValidBlockHeight":340000150}}
            """));
        Assert.NotNull(parsed);
        Assert.Equal(32, parsed.Value.Blockhash.Length);
        Assert.Equal(340000150UL, parsed.Value.LastValidBlockHeight);
        Assert.Null(SolanaRpc.ParseBlockhash(Json("""{"value":{"blockhash":"EkSnNWid2cvwEVnVx9aBqawnmiCNiDgp3gUdkDPTKN1N"}}""")));
    }

    [Theory]
    [InlineData("""{"context":{"slot":1},"value":[null]}""", SolSubmitOutcome.Pending)]
    [InlineData("""{"context":{"slot":1},"value":[{"slot":1,"confirmations":0,"err":null,"status":{"Ok":null},"confirmationStatus":"processed"}]}""", SolSubmitOutcome.Pending)]
    [InlineData("""{"context":{"slot":1},"value":[{"slot":1,"confirmations":3,"err":null,"status":{"Ok":null},"confirmationStatus":"confirmed"}]}""", SolSubmitOutcome.Included)]
    [InlineData("""{"context":{"slot":1},"value":[{"slot":1,"confirmations":null,"err":null,"status":{"Ok":null},"confirmationStatus":"finalized"}]}""", SolSubmitOutcome.Included)]
    [InlineData("""{"context":{"slot":1},"value":[{"slot":1,"confirmations":5,"err":{"InstructionError":[0,{"Custom":1}]},"status":{"Err":{}},"confirmationStatus":"confirmed"}]}""", SolSubmitOutcome.FailedFeeCharged)]
    [InlineData("""{"context":{"slot":1},"value":[]}""", SolSubmitOutcome.Unknown)]
    public void Only_a_voted_on_block_settles_a_transaction(string json, SolSubmitOutcome expected) =>
        Assert.Equal(expected, SolanaRpc.ParseSignatureStatus(Json(json)).Outcome);
}
