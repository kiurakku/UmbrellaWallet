using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// History for XRP and Stellar, read from answers shaped like the real ones (captured from
/// xrplcluster.com and horizon.stellar.org, trimmed).
///
/// The rule that matters most is the XRP one: the amount shown is what the ledger says was DELIVERED.
/// A partial payment names a large Amount and delivers a sliver; a history that printed Amount would show
/// the user money that never arrived — the same trick that has emptied exchanges.
/// </summary>
public sealed class AccountHistoryTests
{
    private const string Me = "rEb8TK3gBgk5auZkwc6sHnwrGVJH8DuaLh";
    private const string Them = "rLpvuHZFE46NUyZH5XaMvmYRJZF7aory7t";

    private static string XrpAnswer(string txs) => """{"result":{"account":"@Me@","transactions":[@txs@],"validated":true,"status":"success"}}""".Replace("@Me@", Me).Replace("@txs@", txs);

    private static string XrpPayment(string from, string to, string amount, string delivered, string result = "tesSUCCESS",
        string hash = "9FAF70B356A2DA2F5340CAA878A83AB6FC7275F3BDB9D05C92DD5E65FC0197DE", string type = "Payment") => """
        {"meta":{"TransactionResult":"@result@","delivered_amount":@delivered@},
         "tx":{"TransactionType":"@type@","Account":"@from@","Destination":"@to@","Amount":@amount@,
               "hash":"@hash@","date":843725290},
         "validated":true}
        """.Replace("@result@", result).Replace("@delivered@", delivered).Replace("@type@", type).Replace("@from@", from).Replace("@to@", to).Replace("@amount@", amount).Replace("@hash@", hash);

    [Fact]
    public void An_incoming_xrp_payment_is_read_with_its_sender_and_date()
    {
        var rows = AccountHistoryClient.ParseXrp(XrpAnswer(XrpPayment(Them, Me, "\"20793800\"", "\"20793800\"")), Me);

        var row = Assert.Single(rows);
        Assert.Equal("Received", row.Kind);
        Assert.Equal("XRP", row.Asset);
        Assert.Equal("20.7938", row.Amount);
        Assert.Equal(Them, row.Counterparty);
        // 843725290 seconds after 2000-01-01 is 2026-09-26.
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 8, 8, 10, TimeSpan.Zero).ToUnixTimeMilliseconds(), row.UnixMs);
        Assert.Equal("livenet.xrpl.org/transactions/9FAF70B356A2DA2F5340CAA878A83AB6FC7275F3BDB9D05C92DD5E65FC0197DE", row.Explorer);
    }

    [Fact]
    public void A_partial_payment_shows_what_was_delivered_not_what_it_claimed()
    {
        // Claims 1,000,000 XRP, delivers 0.000001.
        var rows = AccountHistoryClient.ParseXrp(XrpAnswer(XrpPayment(Them, Me, "\"1000000000000\"", "\"1\"")), Me);

        Assert.Equal("0.000001", Assert.Single(rows).Amount);
    }

    [Fact]
    public void Failed_and_non_xrp_payments_are_left_out()
    {
        var failed = XrpPayment(Them, Me, "\"5000000\"", "\"5000000\"", result: "tecUNFUNDED_PAYMENT");
        var issued = XrpPayment(Them, Me, """{"currency":"USD","issuer":"rX","value":"5"}""",
            """{"currency":"USD","issuer":"rX","value":"5"}""");
        var trust = XrpPayment(Me, Them, "\"0\"", "\"0\"", type: "TrustSet");

        Assert.Empty(AccountHistoryClient.ParseXrp(XrpAnswer($"{failed},{issued},{trust}"), Me));
    }

    [Fact]
    public void An_outgoing_xrp_payment_names_the_recipient()
    {
        var row = Assert.Single(AccountHistoryClient.ParseXrp(XrpAnswer(XrpPayment(Me, Them, "\"40283187\"", "\"40283187\"")), Me));
        Assert.Equal("Sent", row.Kind);
        Assert.Equal(Them, row.Counterparty);
        Assert.Equal("40.283187", row.Amount);
    }

    private const string StellarMe = "GAHK7EEG2WWHVKDNT4CEQFZGKF2LGDSW2IVM4S5DP42RBW3K6BTODB4A";
    private const string StellarThem = "GC7YFQUTYWEZI6CE4JSOFCM6GRPXHLZOQPKFDGYO6BFTKYQ4KWU7KQI7";

    [Fact]
    public void Stellar_payments_and_the_accounts_creation_are_read()
    {
        var json = """
            {"_embedded":{"records":[
              {"type":"payment","from":"@StellarThem@","to":"@StellarMe@","amount":"12.5000000","asset_type":"native",
               "transaction_hash":"b56f891b","created_at":"2026-09-26T07:04:49Z","transaction_successful":true},
              {"type":"payment","from":"@StellarMe@","to":"@StellarThem@","amount":"3.0000000","asset_type":"credit_alphanum4",
               "asset_code":"USDC","transaction_hash":"c60b3999","created_at":"2026-09-25T00:56:54Z","transaction_successful":true},
              {"type":"create_account","funder":"@StellarThem@","account":"@StellarMe@","starting_balance":"2.0000000",
               "transaction_hash":"cf1fbbee","created_at":"2026-09-23T23:28:33Z","transaction_successful":true}
            ]}}
            """.Replace("@StellarThem@", StellarThem).Replace("@StellarMe@", StellarMe);

        var rows = AccountHistoryClient.ParseStellar(json, StellarMe);

        Assert.Equal(2, rows.Count);   // the USDC payment is not XLM and is left out
        Assert.Equal("12.5", rows[0].Amount);
        Assert.Equal("Received", rows[0].Kind);
        Assert.Equal("stellar.expert/explorer/public/tx/b56f891b", rows[0].Explorer);
        Assert.Equal("2", rows[1].Amount);
        Assert.Equal(StellarThem, rows[1].Counterparty);
    }

    [Fact]
    public void A_failed_stellar_payment_is_left_out()
    {
        var json = """
            {"_embedded":{"records":[
              {"type":"payment","from":"@StellarMe@","to":"@StellarThem@","amount":"1.0000000","asset_type":"native",
               "transaction_hash":"aa","created_at":"2026-09-26T07:04:49Z","transaction_successful":false}
            ]}}
            """.Replace("@StellarMe@", StellarMe).Replace("@StellarThem@", StellarThem);
        Assert.Empty(AccountHistoryClient.ParseStellar(json, StellarMe));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"result":{"status":"error"}}""")]
    [InlineData("""{"_embedded":{}}""")]
    public void An_unexpected_answer_is_no_rows_not_an_exception(string json)
    {
        Assert.Empty(AccountHistoryClient.ParseXrp(json, Me));
        Assert.Empty(AccountHistoryClient.ParseStellar(json, StellarMe));
    }
}
