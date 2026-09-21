using Umbrella.Wallet.Core.Payments;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// BIP-21 payment links, read strictly (roadmap P2.2). A link is an instruction to move money, so the
/// tests are mostly about what must be REFUSED rather than guessed at.
/// </summary>
public sealed class Bip21UriTests
{
    private const string Addr = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4";

    [Fact]
    public void A_plain_link_gives_the_address_and_nothing_else()
    {
        Assert.True(Bip21Uri.TryParse($"bitcoin:{Addr}", out var p, out var error), error);
        Assert.Equal(Addr, p!.Address);
        Assert.Null(p.Amount);
        Assert.Null(p.PayjoinEndpoint);
    }

    [Fact]
    public void Amount_label_and_a_PayJoin_endpoint_are_read()
    {
        var link = $"BITCOIN:{Addr}?amount=0.015&label=Coffee%20shop&pj=https%3A%2F%2Fpay.example.com%2FBTC%2Fpj&pjos=0";

        Assert.True(Bip21Uri.TryParse(link, out var p, out var error), error);
        Assert.Equal(0.015m, p!.Amount);
        Assert.Equal("Coffee shop", p.Label);
        Assert.Equal(new Uri("https://pay.example.com/BTC/pj"), p.PayjoinEndpoint);
        Assert.True(p.OutputSubstitutionDisabled);
        Assert.Null(p.PayjoinIgnoredReason);
    }

    [Theory]
    [InlineData("0,5")]         // a comma is not a BIP-21 decimal point, and must not become 5
    [InlineData("1,000.5")]     // no group separators
    [InlineData("1e-3")]        // no exponent
    [InlineData("-1")]          // no sign
    [InlineData("0")]           // not a payment
    [InlineData("0.000000001")] // more precision than a satoshi
    [InlineData("1.2.3")]
    [InlineData("")]
    public void An_amount_outside_BIP21s_own_format_refuses_the_whole_link(string amount)
    {
        Assert.False(Bip21Uri.TryParse($"bitcoin:{Addr}?amount={amount}", out var p, out var error));
        Assert.Null(p);
        Assert.NotNull(error);
    }

    [Fact]
    public void A_key_given_twice_refuses_the_link()
    {
        // Two programs reading "the" amount of this link could pick different ones.
        Assert.False(Bip21Uri.TryParse($"bitcoin:{Addr}?amount=0.1&amount=10", out _, out var error));
        Assert.Contains("more than once", error);
    }

    [Fact]
    public void An_unknown_required_parameter_refuses_the_link()
    {
        // BIP-21: a req- key the wallet does not understand is a condition it cannot honour.
        Assert.False(Bip21Uri.TryParse($"bitcoin:{Addr}?req-somethingnew=1", out _, out var error));
        Assert.Contains("req-somethingnew", error);
    }

    [Theory]
    [InlineData("http%3A%2F%2Fpay.example.com%2Fpj")]            // plaintext clearnet
    [InlineData("ftp%3A%2F%2Fpay.example.com%2Fpj")]
    [InlineData("not-a-url")]
    public void A_PayJoin_endpoint_that_is_not_HTTPS_or_onion_is_ignored_and_says_why(string pj)
    {
        // The payment itself is still valid — only the PayJoin half is dropped, and the user is told.
        Assert.True(Bip21Uri.TryParse($"bitcoin:{Addr}?amount=0.01&pj={pj}", out var p, out var error), error);
        Assert.Null(p!.PayjoinEndpoint);
        Assert.NotNull(p.PayjoinIgnoredReason);
        Assert.Equal(0.01m, p.Amount);
    }

    [Fact]
    public void An_onion_endpoint_over_plain_HTTP_is_accepted()
    {
        const string onion = "http%3A%2F%2Fexampleonionaddressxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.onion%2Fpj";
        Assert.True(Bip21Uri.TryParse($"bitcoin:{Addr}?pj={onion}", out var p, out _));
        Assert.NotNull(p!.PayjoinEndpoint);
    }

    [Fact]
    public void A_plus_sign_is_a_plus_sign()
    {
        // RFC 3986: '+' is not a space outside HTML forms. Converting it would rewrite a pj= URL.
        Assert.True(Bip21Uri.TryParse($"bitcoin:{Addr}?pj=https%3A%2F%2Fpay.example.com%2Fa+b", out var p, out _));
        Assert.Equal("/a+b", p!.PayjoinEndpoint!.AbsolutePath);
    }

    [Theory]
    [InlineData("bitcoin:")]
    [InlineData("bitcoin:?amount=1")]
    [InlineData("litecoin:ltc1qxyz")]
    [InlineData(Addr)]
    public void Anything_without_a_bitcoin_address_is_not_a_payment_link(string text)
    {
        Assert.False(Bip21Uri.TryParse(text, out _, out _));
    }
}
