using System.Numerics;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// SPL token balances (roadmap N.3). The fixtures are trimmed from a live mainnet answer, so the shape
/// being parsed is the one the RPC really sends.
/// </summary>
public sealed class SolanaTokenTests
{
    private const string Usdc = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";

    private static string Account(string mint, string amount, int decimals) => $$$"""
        {"pubkey":"x","account":{"lamports":2039280,"data":{"program":"spl-token","parsed":{"info":{
          "isNative":false,"mint":"{{{mint}}}","owner":"o","state":"initialized",
          "tokenAmount":{"amount":"{{{amount}}}","decimals":{{{decimals}}},"uiAmountString":"?"}},"type":"account"},"space":165},
          "owner":"TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA","executable":false}}
        """;

    private static IReadOnlyList<SplHolding>? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return SolanaTokens.ParseTokenAccounts(doc.RootElement);
    }

    private static string Response(params string[] accounts) =>
        $$"""{"jsonrpc":"2.0","result":{"context":{"slot":1},"value":[{{string.Join(",", accounts)}}]},"id":1}""";

    [Fact]
    public void Token_accounts_read_by_mint_with_the_decimals_the_chain_reports()
    {
        var holdings = Parse(Response(Account(Usdc, "1000000", 6)))!;

        var usdc = Assert.Single(holdings);
        Assert.Equal(Usdc, usdc.Mint);
        Assert.Equal(1m, usdc.Amount);
    }

    [Fact]
    public void Several_accounts_of_one_mint_are_added_and_empty_ones_left_out()
    {
        // One owner can hold a mint in more than one account; showing only the first would under-report.
        var holdings = Parse(Response(
            Account(Usdc, "1500000", 6),
            Account(Usdc, "250000", 6),
            Account("DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263", "0", 5)))!;

        var usdc = Assert.Single(holdings);
        Assert.Equal(1.75m, usdc.Amount);
    }

    [Fact]
    public void An_address_with_no_token_accounts_is_an_empty_list_not_unknown()
    {
        Assert.Empty(Parse(Response())!);
    }

    [Fact]
    public void Large_amounts_keep_their_precision()
    {
        var holdings = Parse(Response(Account("DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263", "123456789012345678", 5)))!;
        Assert.Equal(1234567890123.45678m, Assert.Single(holdings).Amount);
        Assert.Equal(BigInteger.Parse("123456789012345678"), holdings[0].Units);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","error":{"code":-32005,"message":"Node is behind"},"id":1}""")]
    [InlineData("""{"jsonrpc":"2.0","result":{"value":"nope"},"id":1}""")]
    [InlineData("""{"jsonrpc":"2.0","result":{"value":[{"account":{"data":"base64stuff"}}]},"id":1}""")]
    [InlineData("""[]""")]
    public void An_answer_that_is_not_a_token_list_is_unknown(string json)
    {
        Assert.Null(Parse(json));
    }

    [Fact]
    public void Two_accounts_of_one_mint_that_disagree_on_decimals_are_refused()
    {
        Assert.Null(Parse(Response(Account(Usdc, "1", 6), Account(Usdc, "1", 9))));
    }

    [Fact]
    public void A_fractional_or_negative_amount_is_refused()
    {
        Assert.Null(Parse(Response(Account(Usdc, "1.5", 6))));
        Assert.Null(Parse(Response(Account(Usdc, "-1", 6))));
    }

    [Fact]
    public void The_known_mints_include_both_token_programs_majors()
    {
        // PayPal USD lives under Token-2022 — the reason both programs are read.
        Assert.Equal("USDC", SolanaTokens.KnownMints[Usdc].Symbol);
        Assert.Equal("PYUSD", SolanaTokens.KnownMints["2b1kV6DkPAnxd5ixfnxCpjxmKwqjjaYmCZfHsFu24GXo"].Symbol);
        Assert.NotEqual(SolanaTokens.TokenProgram, SolanaTokens.Token2022Program);
    }
}
