using System.Text.Json;
using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Balances that read "server did not respond" when the server had answered: an empty Cardano address
/// taken for "no answer", and a Monero row blaming a server while the Monero service was simply off.
/// </summary>
public sealed class UnreadBalanceReasonTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Koios_answering_an_empty_list_is_a_real_zero()
    {
        // What api.koios.rest returns, with a 200, for an address that has never been on chain
        // (checked live against a freshly derived address).
        Assert.Equal(0m, PublicChainBalanceClient.ParseKoiosBalance(Json("[]")));
    }

    [Theory]
    [InlineData("""[{"address":"addr1x","balance":"1000000","utxo_set":[]}]""", "1")]
    [InlineData("""[{"address":"addr1x","balance":"2500000"}]""", "2.5")]
    [InlineData("""[{"address":"addr1x","balance":1500000}]""", "1.5")]
    public void Koios_balances_are_read_in_ADA(string json, string ada)
    {
        Assert.Equal(decimal.Parse(ada, System.Globalization.CultureInfo.InvariantCulture),
            PublicChainBalanceClient.ParseKoiosBalance(Json(json)));
    }

    [Theory]
    [InlineData("""{"error":"rate limited"}""")]
    [InlineData("""[{"address":"addr1x"}]""")]
    [InlineData("""[{"address":"addr1x","balance":"lots"}]""")]
    [InlineData("""[{"address":"addr1x","balance":"-5"}]""")]
    [InlineData("""["addr1x"]""")]
    public void Anything_else_from_Koios_stays_unknown(string json)
    {
        Assert.Null(PublicChainBalanceClient.ParseKoiosBalance(Json(json)));
    }

    [Fact]
    public void An_unread_row_says_why_when_the_wallet_knows()
    {
        var off = new HoldingRowViewModel("XMR", "Monero", "Monero", 0, 0, 0, 0, "", "Ready",
            BalanceRead.Unknown, UnreadNote: Loc.Instance["balance.xmrOff"]);
        var failed = new HoldingRowViewModel("BTC", "Bitcoin", "Bitcoin", 0, 0, 0, 0, "", "Ready", BalanceRead.Unknown);

        Assert.Equal(Loc.Instance["balance.xmrOff"], off.BalanceNote);
        Assert.Equal(Loc.Instance["balance.unavailable"], failed.BalanceNote);
        Assert.NotEqual(off.BalanceNote, failed.BalanceNote);
    }

    [Fact]
    public void A_reason_never_hides_a_real_reading()
    {
        // The note belongs to an UNREAD balance only; once the service answers, the row reads normally.
        var live = new HoldingRowViewModel("XMR", "Monero", "Monero", 0, 1, 0, 0, "", "Ready",
            BalanceRead.Live, UnreadNote: Loc.Instance["balance.xmrOff"]);
        Assert.Equal(string.Empty, live.BalanceNote);
    }
}
