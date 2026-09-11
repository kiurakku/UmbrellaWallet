using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Reading Jetton balances out of a toncenter v3 response.
///
/// The payload is awkward in a way that costs money if it is read carelessly. The balance is on the
/// wallet row, but the DECIMALS are on the jetton master in a separate metadata block — and both
/// arrive as strings. USD₮ on TON has 6 decimals while most jettons have 9, so joining the two parts
/// wrongly is off by a factor of a thousand.
///
/// The fixtures below are trimmed from real responses, including the real Tether-on-TON master, so
/// the field names and types are the ones the API actually sends rather than the ones this code hoped
/// for.
/// </summary>
public sealed class TonJettonParsingTests
{
    private const string UsdtMaster = "0:B113A994B5024A16719F69139328EB759596C38A25F59028B146FECDC3621DFE";
    private const string NineDecimalMaster = "0:4F7086269775AF29E22A57F1A534257B9145851F369E8BDC46FCB107A04446EF";

    /// <summary>Two holdings: 12.5 USD₮ (6 decimals) and 3 of a 9-decimal jetton.</summary>
    private const string RealShape =
        """
        {
          "jetton_wallets": [
            { "address": "0:AAA", "balance": "12500000", "owner": "0:OWNER", "jetton": "0:B113A994B5024A16719F69139328EB759596C38A25F59028B146FECDC3621DFE" },
            { "address": "0:BBB", "balance": "3000000000", "owner": "0:OWNER", "jetton": "0:4F7086269775AF29E22A57F1A534257B9145851F369E8BDC46FCB107A04446EF" }
          ],
          "address_book": {
            "0:B113A994B5024A16719F69139328EB759596C38A25F59028B146FECDC3621DFE": { "user_friendly": "EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs" }
          },
          "metadata": {
            "0:B113A994B5024A16719F69139328EB759596C38A25F59028B146FECDC3621DFE": {
              "token_info": [ { "type": "jetton_masters", "name": "Tether USD", "symbol": "USD₮", "extra": { "decimals": "6" } } ]
            },
            "0:4F7086269775AF29E22A57F1A534257B9145851F369E8BDC46FCB107A04446EF": {
              "token_info": [ { "type": "jetton_masters", "name": "FECK TOKEN", "symbol": "FECK", "extra": { "decimals": "9" } } ]
            }
          }
        }
        """;

    private static TokenBalance Find(IReadOnlyList<TokenBalance> tokens, string symbol) =>
        Assert.Single(tokens.Where(t => t.Symbol == symbol));

    [Fact]
    public void Decimals_come_from_the_master_not_from_a_guess()
    {
        var tokens = PublicChainBalanceClient.ParseTonJettons(RealShape);

        Assert.Equal(2, tokens.Count);
        Assert.Equal(12.5m, Find(tokens, "USDT").Amount);   // 12_500_000 at 6 decimals
        Assert.Equal(3m, Find(tokens, "FECK").Amount);      // 3_000_000_000 at 9 decimals
    }

    [Fact]
    public void Tethers_tugrik_sign_is_folded_back_to_a_plain_T()
    {
        // Tether on TON calls itself "USD₮". Left alone it is a symbol no price feed recognises, so a
        // real dollar balance renders at $0 — the single most reported "my USDT is missing" case.
        var tokens = PublicChainBalanceClient.ParseTonJettons(RealShape);

        Assert.Contains(tokens, t => t.Symbol == "USDT");
        Assert.DoesNotContain(tokens, t => t.Symbol.Contains('₮'));

        // …while the display name keeps what the token actually calls itself.
        Assert.Equal("Tether USD", Find(tokens, "USDT").Name);
    }

    [Fact]
    public void The_contract_shown_is_the_one_an_explorer_understands()
    {
        // Users paste this into tonviewer to check a token is real, so it has to be the user-friendly
        // EQ… form, not the raw 0:… workchain form.
        Assert.Equal("EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs", Find(PublicChainBalanceClient.ParseTonJettons(RealShape), "USDT").Contract);
    }

    [Fact]
    public void A_jetton_with_no_metadata_is_skipped_rather_than_guessed_at()
    {
        // Without the master's metadata there is no symbol and no decimals — any number shown would be
        // invented, and an invented balance is worse than a missing one.
        const string orphan =
            """
            {
              "jetton_wallets": [ { "balance": "1000000000", "jetton": "0:UNKNOWN" } ],
              "metadata": {}
            }
            """;

        Assert.Empty(PublicChainBalanceClient.ParseTonJettons(orphan));
    }

    [Fact]
    public void A_zero_balance_is_not_a_holding()
    {
        // Jetton wallets linger after the balance is spent; showing them as holdings would fill the
        // list with rows that are all zero.
        const string spent =
            """
            {
              "jetton_wallets": [ { "balance": "0", "jetton": "0:M" } ],
              "metadata": { "0:M": { "token_info": [ { "type": "jetton_masters", "symbol": "X", "extra": { "decimals": "9" } } ] } }
            }
            """;

        Assert.Empty(PublicChainBalanceClient.ParseTonJettons(spent));
    }

    [Fact]
    public void Missing_decimals_fall_back_to_tons_default_of_nine()
    {
        const string noDecimals =
            """
            {
              "jetton_wallets": [ { "balance": "2500000000", "jetton": "0:M" } ],
              "metadata": { "0:M": { "token_info": [ { "type": "jetton_masters", "symbol": "PLAIN", "extra": {} } ] } }
            }
            """;

        Assert.Equal(2.5m, Assert.Single(PublicChainBalanceClient.ParseTonJettons(noDecimals)).Amount);
    }

    [Fact]
    public void Decimals_sent_as_a_number_are_read_the_same_as_a_string()
    {
        // The API sends them as strings today. Being strict about that would turn a harmless upstream
        // change into every TON balance reading a billion times too large.
        const string numeric =
            """
            {
              "jetton_wallets": [ { "balance": "12500000", "jetton": "0:M" } ],
              "metadata": { "0:M": { "token_info": [ { "type": "jetton_masters", "symbol": "N", "extra": { "decimals": 6 } } ] } }
            }
            """;

        Assert.Equal(12.5m, Assert.Single(PublicChainBalanceClient.ParseTonJettons(numeric)).Amount);
    }

    [Fact]
    public void Unparseable_decimals_do_not_become_zero()
    {
        // The dangerous failure: decimals read as 0 means no division at all, so 12_500_000 units of a
        // six-decimal token would display as twelve and a half MILLION dollars.
        const string broken =
            """
            {
              "jetton_wallets": [ { "balance": "12500000", "jetton": "0:M" } ],
              "metadata": { "0:M": { "token_info": [ { "type": "jetton_masters", "symbol": "B", "extra": { "decimals": "six" } } ] } }
            }
            """;

        var token = Assert.Single(PublicChainBalanceClient.ParseTonJettons(broken));
        Assert.Equal(9, token.Decimals);
        Assert.Equal(0.0125m, token.Amount);
        Assert.True(token.Amount < 1m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"jetton_wallets": null}""")]
    [InlineData("""{"error": "rate limited"}""")]
    public void A_response_that_is_not_a_jetton_list_yields_nothing(string payload)
    {
        // A failed lookup must read as "no answer", never as "you have nothing".
        Assert.Empty(PublicChainBalanceClient.ParseTonJettons(payload));
    }

    [Fact]
    public void The_list_is_capped_so_an_airdrop_flood_cannot_fill_the_wallet()
    {
        var wallets = string.Join(",", Enumerable.Range(0, 60)
            .Select(i => $$"""{ "balance": "1000000000", "jetton": "0:M{{i}}" }"""));
        var metas = string.Join(",", Enumerable.Range(0, 60)
            .Select(i => $$"""
                "0:M{{i}}": { "token_info": [ { "type": "jetton_masters", "symbol": "S{{i}}", "extra": { "decimals": "9" } } ] }
                """));

        var tokens = PublicChainBalanceClient.ParseTonJettons(
            $$"""{ "jetton_wallets": [{{wallets}}], "metadata": { {{metas}} } }""");

        Assert.Equal(40, tokens.Count);
    }
}
