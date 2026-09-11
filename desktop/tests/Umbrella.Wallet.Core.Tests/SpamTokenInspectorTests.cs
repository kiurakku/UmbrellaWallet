using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Unsolicited airdrop tokens land in every TRON and Ethereum account. The name is the attack — it
/// lures the reader to a site that asks for a seed phrase — so the wallet must not present one as an
/// asset the user owns.
///
/// The tests that matter most here are the negative ones. Wrongly hiding somebody's money is far worse
/// than showing spam, so a token with a real price is never flagged, and ordinary token names must
/// survive every heuristic.
/// </summary>
public sealed class SpamTokenInspectorTests
{
    [Theory]
    // The real one that appeared in a user's wallet.
    [InlineData("Hash gambling at Ha138Com", "HA138 COM")]
    [InlineData("Visit x.io to claim 5000 USDT", "USDT-CLAIM")]
    [InlineData("www.free-airdrop.top", "FREE")]
    [InlineData("Congratulations! You won 1 BTC", "PRIZE")]
    [InlineData("Join t.me/somechannel for rewards", "TG")]
    [InlineData("$ 5000 USDC voucher inside", "VOUCHER")]
    public void Airdrop_lures_are_flagged(string name, string symbol)
    {
        var verdict = SpamTokenInspector.Inspect(name, symbol, hasMarketPrice: false);
        Assert.True(verdict.IsSuspected, $"'{name}' should have been flagged");
        Assert.NotEqual(SpamSignal.None, verdict.Signal);
    }

    [Theory]
    [InlineData("Tether USD", "USDT")]
    [InlineData("USD Coin", "USDC")]
    [InlineData("Chainlink", "LINK")]
    [InlineData("Uniswap", "UNI")]
    [InlineData("Wrapped Bitcoin", "WBTC")]
    [InlineData("Shiba Inu", "SHIB")]
    [InlineData("Dai Stablecoin", "DAI")]
    [InlineData("Lido Staked Ether", "STETH")]
    [InlineData("APENFT", "NFT")]
    [InlineData("JUST GOV", "JST")]
    public void Ordinary_token_names_are_never_flagged(string name, string symbol)
    {
        var verdict = SpamTokenInspector.Inspect(name, symbol, hasMarketPrice: false);
        Assert.False(verdict.IsSuspected, $"'{name}' was wrongly flagged as spam ({verdict.Signal})");
    }

    [Fact]
    public void A_token_with_a_real_price_is_never_flagged_however_it_is_named()
    {
        // Value is the strongest evidence that something is a genuine asset. This rule is what makes
        // the feature safe to act on in the UI.
        var verdict = SpamTokenInspector.Inspect("Hash gambling at Ha138Com", "HA138", hasMarketPrice: true);
        Assert.False(verdict.IsSuspected);
        Assert.Equal(SpamSignal.None, verdict.Signal);
    }

    [Fact]
    public void The_signal_says_which_rule_fired()
    {
        Assert.Equal(SpamSignal.ContainsWebAddress,
            SpamTokenInspector.Inspect("claimhere.xyz", "X", false).Signal);
        Assert.Equal(SpamSignal.ReadsAsAdvert,
            SpamTokenInspector.Inspect("Big casino bonus", "B", false).Signal);
        Assert.Equal(SpamSignal.NameIsASentence,
            SpamTokenInspector.Inspect("this token is a long message for you", "T", false).Signal);
    }

    [Fact]
    public void Missing_or_empty_names_are_not_flagged()
    {
        // An explorer that returns no metadata is a data gap, not evidence of spam.
        Assert.False(SpamTokenInspector.Inspect(null, null, false).IsSuspected);
        Assert.False(SpamTokenInspector.Inspect("", "", false).IsSuspected);
        Assert.False(SpamTokenInspector.Inspect("   ", null, false).IsSuspected);
    }

    [Fact]
    public void A_four_word_name_is_still_allowed()
    {
        // The sentence rule starts at five words, so legitimately long names stay visible.
        Assert.False(SpamTokenInspector.Inspect("Wrapped Liquid Staked Ether", "WLSE", false).IsSuspected);
    }
}
