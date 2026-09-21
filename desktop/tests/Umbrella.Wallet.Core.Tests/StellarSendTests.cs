using System.Text.Json;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Stellar send (roadmap N.5), pinned to transactions the Stellar Go SDK builds in its own tests
/// (stellar/go, txnbuild/transaction_test.go: TestPayment, TestCreateAccount, TestMemoText, TestMemoID),
/// with that file's keypair (helpers_test.go, newKeypair0 / newKeypair2) — never to this wallet's own
/// output. A signer that is wrong by one byte builds a transaction the network rejects at best.
/// </summary>
public sealed class StellarSendTests
{
    private const byte SeedVersion = 18 << 3;   // 'S'

    // newKeypair0 — address GDQNY3PBOJOKYZSRMK2S7LHHGWZIUISD4QORETLMXEWXBI7KFZZMKTL3
    private const string Kp0Seed = "SBPQUZ6G4FZNWFHKUWC5BEYWF6R52E3SEP7R3GWYSM2XTKGF5LNTWW4R";
    private const string Kp0 = "GDQNY3PBOJOKYZSRMK2S7LHHGWZIUISD4QORETLMXEWXBI7KFZZMKTL3";

    private static byte[] Seed(string s) =>
        StellarKeys.TryDecode(s, SeedVersion, out var seed) ? seed : throw new ArgumentException(s);

    private static StellarTransfer Transfer(string to, long sequence, bool create, StellarMemo? memo = null) =>
        new(Kp0, to, 10 * StellarTransactions.StroopsPerXlm, create, StellarTransactions.MinBaseFee,
            sequence, memo ?? StellarMemo.None, MaxTime: 0);   // NewInfiniteTimeout: bounds (0, 0)

    [Fact]
    public void The_reference_seed_is_the_reference_address()
    {
        var pub = Umbrella.Wallet.Core.Derivation.Slip10Ed25519.PublicKey(Seed(Kp0Seed));
        Assert.Equal(Kp0, StellarKeys.EncodeAccountId(pub));
    }

    [Fact]
    public void A_payment_is_byte_identical_to_the_Go_SDK()
    {
        // TestPayment: sequence 9605939170639898, incremented by the builder.
        var (envelope, _) = StellarTransactions.Sign(
            Transfer("GB7BDSZU2Y27LYNLALKKALB52WS2IZWYBDGY6EQBLEED3TJOCVMZRH7H", 9605939170639899, create: false),
            Seed(Kp0Seed), StellarTransactions.TestNetwork);

        Assert.Equal(
            "AAAAAgAAAADg3G3hclysZlFitS+s5zWyiiJD5B0STWy5LXCj6i5yxQAAAGQAIiCNAAAAGwAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAQAAAAB+Ecs01jX14asC1KAsPdWlpGbYCM2PEgFZCD3NLhVZmAAAAAAAAAAABfXhAAAAAAAAAAAB6i5yxQAAAEDXBkKYzThQi3/XhJqGzfh/EjaAx/4zK3xBT1/JDNtdkk/kxn4qxHVx++xiV72lqZXxiphNwflA8C7mC8Dvim0E",
            envelope);
    }

    [Fact]
    public void Funding_a_new_account_is_byte_identical_to_the_Go_SDK()
    {
        // TestCreateAccount: sequence 9605939170639897, incremented by the builder.
        var (envelope, _) = StellarTransactions.Sign(
            Transfer("GCCOBXW2XQNUSL467IEILE6MMCNRR66SSVL4YQADUNYYNUVREF3FIV2Z", 9605939170639898, create: true),
            Seed(Kp0Seed), StellarTransactions.TestNetwork);

        Assert.Equal(
            "AAAAAgAAAADg3G3hclysZlFitS+s5zWyiiJD5B0STWy5LXCj6i5yxQAAAGQAIiCNAAAAGgAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAACE4N7avBtJL576CIWTzGCbGPvSlVfMQAOjcYbSsSF2VAAAAAAF9eEAAAAAAAAAAAHqLnLFAAAAQB7MjKIwNEOTIjbEeV+QIjaQp/ZpV5qpbkbDaU54gkfdTOFOUxZq66lTS5FOfP5fmPIVD8InQ00Usy2SmzFC/wc=",
            envelope);
    }

    // The Go SDK's memo tests use an operation this wallet never builds (BumpSequence), so they pin the
    // memo where it sits in the envelope: after the envelope type (4), source (36), fee (4), sequence (8)
    // and the time bounds (4 + 16) — byte 72.
    private const int MemoOffset = 72;

    [Theory]
    [InlineData("Twas brillig",
        "AAAAAgAAAAB+Ecs01jX14asC1KAsPdWlpGbYCM2PEgFZCD3NLhVZmAAAAGQADKJBAAAAAQAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAxUd2FzIGJyaWxsaWcAAAABAAAAAAAAAAsAAAAAAAAAAQAAAAAAAAABLhVZmAAAAECC0/P+zBk5lpH4zIumNt59nFVrPiDGOu8TrJE4r0mXoae8Fmg1yyHQm3Yo5huuPjc/nzwU/R2DKkkQ3C4mWA0N")]
    [InlineData("314159",
        "AAAAAgAAAAB+Ecs01jX14asC1KAsPdWlpGbYCM2PEgFZCD3NLhVZmAAAAGQADC4KAAAAAQAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAgAAAAAABMsvAAAAAQAAAAAAAAALAAAAAAAAAAEAAAAAAAAAAS4VWZgAAABAOT/1f1XoeqY14+wp6rVgwE4fCCPnItc9/85jZN++Fy7lS88e40b3ufQCpzzMCD8AyfHF8BCs/Pn2DiJHxCPQCQ==")]
    public void Memos_are_encoded_as_the_Go_SDK_encodes_them(string typed, string referenceEnvelope)
    {
        Assert.True(StellarMemo.TryParse(typed, out var memo, out _));
        var mine = StellarTransactions.MemoXdr(memo);
        var theirs = Convert.FromBase64String(referenceEnvelope).AsSpan(MemoOffset, mine.Length).ToArray();
        Assert.Equal(theirs, mine);
    }

    [Fact]
    public void The_same_transfer_signed_for_the_public_network_is_a_different_transaction()
    {
        // The network id is inside what is signed; a testnet signature must not be valid on mainnet.
        var t = Transfer("GB7BDSZU2Y27LYNLALKKALB52WS2IZWYBDGY6EQBLEED3TJOCVMZRH7H", 9605939170639899, create: false);
        var test = StellarTransactions.Sign(t, Seed(Kp0Seed), StellarTransactions.TestNetwork);
        var live = StellarTransactions.Sign(t, Seed(Kp0Seed), StellarTransactions.PublicNetwork);

        Assert.NotEqual(test.Hash, live.Hash);
        Assert.NotEqual(test.EnvelopeBase64, live.EnvelopeBase64);
        Assert.Equal(StellarTransactions.Hash(StellarTransactions.PublicNetwork, StellarTransactions.TransactionXdr(t)), live.Hash);
    }

    [Fact]
    public void A_key_that_is_not_the_source_accounts_is_refused()
    {
        var t = Transfer("GB7BDSZU2Y27LYNLALKKALB52WS2IZWYBDGY6EQBLEED3TJOCVMZRH7H", 1, create: false);
        var otherSeed = Seed("SBZVMB74Z76QZ3ZOY7UTDFYKMEGKW5XFJEB6PFKBF4UYSSWHG4EDH7PY");   // newKeypair2
        Assert.Throws<InvalidOperationException>(() => StellarTransactions.Sign(t, otherSeed));
    }

    [Fact]
    public void A_mistyped_destination_never_reaches_the_signer()
    {
        // One character changed: the StrKey checksum fails, and nothing is built.
        var t = Transfer("GB7BDSZU2Y27LYNLALKKALB52WS2IZWYBDGY6EQBLEED3TJOCVMZRH7J", 1, create: false);
        Assert.Throws<ArgumentException>(() => StellarTransactions.TransactionXdr(t));
    }

    // --- memo, amounts ---------------------------------------------------------------------------

    [Theory]
    [InlineData("", StellarMemoType.None)]
    [InlineData("   ", StellarMemoType.None)]
    [InlineData("314159", StellarMemoType.Id)]
    [InlineData("18446744073709551615", StellarMemoType.Id)]     // ulong.MaxValue
    [InlineData("18446744073709551616", StellarMemoType.Text)]   // one past it: text, and the review says so
    [InlineData("deposit 42", StellarMemoType.Text)]
    [InlineData("-5", StellarMemoType.Text)]
    public void A_typed_memo_is_read_as_the_type_the_review_will_show(string typed, StellarMemoType expected)
    {
        Assert.True(StellarMemo.TryParse(typed, out var memo, out _));
        Assert.Equal(expected, memo.Type);
    }

    [Fact]
    public void A_text_memo_is_limited_by_bytes_not_characters()
    {
        Assert.True(StellarMemo.TryParse(new string('a', 28), out _, out _));
        Assert.False(StellarMemo.TryParse(new string('a', 29), out _, out var error));
        Assert.NotNull(error);
        // Ten Cyrillic letters are twenty bytes; fifteen are thirty.
        Assert.True(StellarMemo.TryParse(new string('ж', 10), out _, out _));
        Assert.False(StellarMemo.TryParse(new string('ж', 15), out _, out _));
    }

    [Theory]
    [InlineData("1.2345678", 12_345_678L)]
    [InlineData("10", 100_000_000L)]
    [InlineData("0.0000001", 1L)]
    public void Amounts_convert_to_stroops_exactly(string xlm, long stroops)
    {
        Assert.True(StellarTransactions.TryToStroops(decimal.Parse(xlm, System.Globalization.CultureInfo.InvariantCulture), out var s));
        Assert.Equal(stroops, s);
    }

    [Theory]
    [InlineData("0.00000001")]   // an eighth decimal: refused, not rounded
    [InlineData("0")]
    [InlineData("-1")]
    public void Amounts_that_cannot_be_signed_exactly_are_refused(string xlm)
    {
        Assert.False(StellarTransactions.TryToStroops(decimal.Parse(xlm, System.Globalization.CultureInfo.InvariantCulture), out _));
    }

    // --- what Horizon says ------------------------------------------------------------------------

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Spendable_leaves_the_reserve_and_promised_offers_behind()
    {
        var state = StellarSendRules.ParseAccount(Json("""
            {"sequence":"123456789","num_subentries":3,"num_sponsoring":0,"num_sponsored":0,
             "balances":[{"asset_type":"credit_alphanum4","balance":"5.0000000"},
                         {"asset_type":"native","balance":"100.5000000","selling_liabilities":"1.0000000"}]}
            """));

        Assert.NotNull(state);
        Assert.Equal(123456789L, state!.Sequence);
        // (2 + 3 subentries) × 0.5 XLM reserve + 1 XLM promised to an offer = 3.5 XLM kept back.
        Assert.Equal(35_000_000L, state.LockedStroops);
        Assert.Equal(970_000_000L, state.SpendableStroops);
    }

    [Fact]
    public void Sponsorship_moves_the_reserve()
    {
        var state = StellarSendRules.ParseAccount(Json("""
            {"sequence":"1","num_subentries":2,"num_sponsoring":1,"num_sponsored":2,
             "balances":[{"asset_type":"native","balance":"3.0000000"}]}
            """));
        // (2 + 2 + 1 − 2) × 0.5 = 1.5 XLM kept.
        Assert.Equal(15_000_000L, state!.LockedStroops);
    }

    [Fact]
    public void A_balance_below_the_reserve_has_nothing_spendable_rather_than_a_negative_amount()
    {
        var state = StellarSendRules.ParseAccount(Json("""
            {"sequence":"1","num_subentries":0,"balances":[{"asset_type":"native","balance":"0.8000000"}]}
            """));
        Assert.Equal(0L, state!.SpendableStroops);
    }

    [Theory]
    [InlineData("""{"num_subentries":0,"balances":[{"asset_type":"native","balance":"1.0000000"}]}""")]      // no sequence
    [InlineData("""{"sequence":"1","balances":[{"asset_type":"credit_alphanum4","balance":"1.0000000"}]}""")] // no XLM line
    [InlineData("""{"sequence":"1","balances":[{"asset_type":"native","balance":"lots"}]}""")]
    [InlineData("""{"sequence":"1","num_subentries":"3","balances":[{"asset_type":"native","balance":"1.0000000"}]}""")]
    [InlineData("""{"sequence":"x","balances":[{"asset_type":"native","balance":"1.0000000"}]}""")]
    public void An_account_answer_that_is_not_understood_stops_the_send(string json)
    {
        Assert.Null(StellarSendRules.ParseAccount(Json(json)));
    }

    [Theory]
    [InlineData("""{"last_ledger_base_fee":"100","fee_charged":{"p95":"250"}}""", 250u)]
    [InlineData("""{"last_ledger_base_fee":"100","fee_charged":{"p95":"50"}}""", 100u)]        // never under the minimum
    [InlineData("""{"last_ledger_base_fee":"100","fee_charged":{"p95":"999999"}}""", 10_000u)] // never over 0.001 XLM
    [InlineData("""{"last_ledger_base_fee":"300","fee_charged":{"p95":"100"}}""", 300u)]       // the ledger's own base wins
    public void The_fee_bid_follows_the_network_within_bounds(string json, uint expected)
    {
        Assert.Equal(expected, StellarSendRules.ParseFeeBid(Json(json)));
    }

    [Fact]
    public void An_unreadable_fee_answer_is_unknown()
    {
        Assert.Null(StellarSendRules.ParseFeeBid(Json("""{"fee_charged":{"p95":250}}""")));
        Assert.Null(StellarSendRules.ParseFeeBid(Json("""{"error":"x"}""")));
    }
}

/// <summary>
/// What Horizon says after a submit, read the way the Send screen must act on it. The line that
/// matters: a refusal is safe to review again, and "no answer" is NOT — the transaction may be in a
/// ledger, and a fresh send would take the next sequence number and pay twice.
/// </summary>
public sealed class StellarSubmitTests
{
    [Fact]
    public void A_200_with_a_hash_is_a_payment_in_a_ledger()
    {
        var r = StellarSubmit.Parse(200, """{"hash":"abc123","successful":true,"ledger":1}""");
        Assert.Equal(StellarSubmitOutcome.Included, r.Outcome);
        Assert.Equal("abc123", r.Hash);
    }

    [Fact]
    public void Included_but_failed_is_not_a_payment()
    {
        var r = StellarSubmit.Parse(200, """{"hash":"abc123","successful":false}""");
        Assert.Equal(StellarSubmitOutcome.Rejected, r.Outcome);
    }

    [Theory]
    [InlineData("tx_failed", "op_underfunded", "minimum balance")]
    [InlineData("tx_failed", "op_no_destination", "not a Stellar account yet")]
    [InlineData("tx_bad_seq", null, "Another transaction")]
    [InlineData("tx_insufficient_fee", null, "higher fee")]
    [InlineData("tx_too_late", null, "window closed")]
    public void A_refusal_says_why_in_words(string tx, string? op, string expected)
    {
        var ops = op is null ? "" : ",\"operations\":[\"" + op + "\"]";
        var body = "{\"type\":\"https://stellar.org/horizon-errors/transaction_failed\",\"status\":400," +
                   "\"extras\":{\"result_codes\":{\"transaction\":\"" + tx + "\"" + ops + "}}}";
        var r = StellarSubmit.Parse(400, body);

        Assert.Equal(StellarSubmitOutcome.Rejected, r.Outcome);
        Assert.Contains(expected, r.Reason);
    }

    [Theory]
    [InlineData(504, """{"type":"https://stellar.org/horizon-errors/timeout","status":504}""")]
    [InlineData(503, "")]
    [InlineData(500, "not json")]
    [InlineData(0, null)]
    public void No_clear_answer_is_unknown_never_a_refusal(int status, string? body)
    {
        // Treating a timeout as "failed" would offer a retry, and a retry could pay twice.
        Assert.Equal(StellarSubmitOutcome.Unknown, StellarSubmit.Parse(status, body).Outcome);
    }

    [Fact]
    public void An_unknown_code_is_reported_as_itself()
    {
        Assert.Contains("op_line_full", StellarSubmit.Explain(["tx_failed", "op_line_full"]));
    }
}
