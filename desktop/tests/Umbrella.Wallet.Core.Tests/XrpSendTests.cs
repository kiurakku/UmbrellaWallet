using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// XRP send (roadmap N.4).
///
/// Nothing here is checked against this wallet's own output. The signed payments are xrpl.js's own
/// wallet tests (packages/xrpl/test/wallet/index.test.ts): the same fields signed by the same key must
/// give the same blob and the same hash, byte for byte — which pins the field order, the amount and
/// length encodings, the signing prefix, the deterministic low-S signature and the hash at once. The
/// keys come from those tests' family seeds, and each derived public key is checked against the one
/// xrpl.js put in its signed blob before it signs anything here.
/// </summary>
public sealed class XrpSendTests
{
    private const string SeedA = "ss1x3KLrSvfg7irFc1D929WXZ7z9H";
    private const string SeedAPublicKey = "02A8A44DB3D4C73EEEE11DFE54D2029103B776AA8A8D293A91D645977C9DF5F544";
    private const string SeedB = "shd2nxpFD6iBRKWsRss2P4tKMWyy9";
    private const string SeedBPublicKey = "0305E09ED602D40AB1AF65646A4007C2DAC17CB6CDACDE301E74FB2D728EA057CF";

    [Fact]
    public void The_family_seeds_give_the_keys_xrpl_js_signed_with()
    {
        Assert.Equal(SeedAPublicKey, Convert.ToHexString(KeyFromFamilySeed(SeedA).PubKey.ToBytes()));
        Assert.Equal(SeedBPublicKey, Convert.ToHexString(KeyFromFamilySeed(SeedB).PubKey.ToBytes()));
        Assert.Equal("rwiZ3q3D3QuG4Ga2HyGdq3kPKJRGctVG8a",
            XrpAddress.Encode(XrpAddress.AccountIdFromPublicKey(KeyFromFamilySeed(SeedB).PubKey.ToBytes())));
    }

    [Fact]
    public void A_plain_payment_signs_to_xrpl_js_bytes()
    {
        // "sign with a prepared payment" — exactly the shape this wallet sends, without a tag.
        var tx = XrpTransactions.Payment(new XrpPayment(
            "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59", "rQ3PTWGLCbPz8ZCicV5tCX3xuymojTng5r",
            Drops: 1, FeeDrops: 12, Sequence: 23, LastLedgerSequence: 8819954, DestinationTag: null));

        var (blob, hash) = XrpTransactions.Sign(tx, KeyFromFamilySeed(SeedA));

        Assert.Equal(
            "12000022800000002400000017201B008694F261400000000000000168400000000000000C732102A8A44DB3D4C73EEEE11DFE54D2029103B776AA8A8D293A91D645977C9DF5F54474473045022100E8929B68B137AB2AAB1AD3A4BB253883B0C8C318DC8BB39579375751B8E54AC502206893B2D61244AFE777DAC9FA3D9DDAC7780A9810AF4B322D629784FD626B8CE481145E7B112523F68D2F5E879DB4EAC51C6698A693048314FDB08D07AAA0EB711793A3027304D688E10C3648",
            blob);
        Assert.Equal("AA1D2BDC59E504AA6C2416E864C615FB18042C1AB4457BEB883F7194D8C452B5", hash);
    }

    [Fact]
    public void A_payment_with_a_destination_tag_signs_to_xrpl_js_bytes()
    {
        // "sign with lowercase hex data in memo": a payment with a destination tag. It also carries a
        // source tag and a memo, which this wallet never sends; they are set here, from the reference
        // bytes, only so the signature can be compared whole.
        var tx = XrpTransactions.Payment(new XrpPayment(
                "rwiZ3q3D3QuG4Ga2HyGdq3kPKJRGctVG8a", "rUeEBYXHo8vF86Rqir3zWGRQ84W9efdAQd",
                Drops: 10_000_000, FeeDrops: 12, Sequence: 12, LastLedgerSequence: 14000999, DestinationTag: 9999))
            .UInt32(XrplField.SourceTag, 8888)
            .Raw(XrplField.Memos, Convert.FromHexString(
                "EA7C1F687474703A2F2F6578616D706C652E636F6D2F6D656D6F2F67656E657269637D0472656E74E1F1"));

        var (blob, hash) = XrpTransactions.Sign(tx, KeyFromFamilySeed(SeedB));

        Assert.Equal(
            "120000228000000023000022B8240000000C2E0000270F201B00D5A36761400000000098968068400000000000000C73210305E09ED602D40AB1AF65646A4007C2DAC17CB6CDACDE301E74FB2D728EA057CF744730450221009C00E8439E017CA622A5A1EE7643E26B4DE9C808DE2ABE45D33479D49A4CEC66022062175BE8733442FA2A4D9A35F85A57D58252AE7B19A66401FE238B36FA28E5A081146C1856D0E36019EA75C56D7E8CBA6E35F9B3F71583147FB49CD110A1C46838788CD12764E3B0F837E0DDF9EA7C1F687474703A2F2F6578616D706C652E636F6D2F6D656D6F2F67656E657269637D0472656E74E1F1",
            blob);
        Assert.Equal("41B9CB78D8E18A796CDD4B0BC6FB0EA19F64C4F25FDE23049197852CAB71D10D", hash);
    }

    [Fact]
    public void The_signing_mechanics_match_xrpl_js_on_a_second_transaction_type()
    {
        // "sign successfully" (REQUEST_FIXTURES.normal): an AccountSet, so the prefix, signature and
        // hash are pinned on a transaction whose fields are not the Payment builder's.
        var tx = new XrplObject()
            .UInt16(XrplField.TransactionType, 3)
            .UInt32(XrplField.Flags, XrpTransactions.FullyCanonicalSig)
            .UInt32(XrplField.Sequence, 23)
            .UInt32(XrplField.LastLedgerSequence, 8820051)
            .Drops(XrplField.Fee, 12)
            .Blob(XrplField.Domain, Encoding.ASCII.GetBytes("example.com"))
            .AccountId(XrplField.Account, Decode("r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59"));

        var (blob, hash) = XrpTransactions.Sign(tx, KeyFromFamilySeed(SeedA));

        Assert.Equal(
            "12000322800000002400000017201B0086955368400000000000000C732102A8A44DB3D4C73EEEE11DFE54D2029103B776AA8A8D293A91D645977C9DF5F54474463044022025464FA5466B6E28EEAD2E2D289A7A36A11EB9B269D211F9C76AB8E8320694E002205D5F99CB56E5A996E5636A0E86D029977BEFA232B7FB64ABA8F6E29DC87A9E89770B6578616D706C652E636F6D81145E7B112523F68D2F5E879DB4EAC51C6698A69304",
            blob);
        Assert.Equal("93F6C6CE73C092AA005103223F3A1F557F4C097A2943D96760F6490F04379917", hash);
    }

    [Fact]
    public void A_key_that_is_not_the_senders_is_refused_before_signing()
    {
        // Seed B's own address (pinned above). xrpl.js's fixtures sign for other accounts freely — a
        // regular key can — but this wallet only ever signs for the address its key gives.
        var p = new XrpPayment("rwiZ3q3D3QuG4Ga2HyGdq3kPKJRGctVG8a", "rQ3PTWGLCbPz8ZCicV5tCX3xuymojTng5r", 1, 12, 23, 8819954, null);
        Assert.Throws<InvalidOperationException>(() => XrpTransactions.SignPayment(p, KeyFromFamilySeed(SeedA)));
        Assert.StartsWith("1200", XrpTransactions.SignPayment(p, KeyFromFamilySeed(SeedB)).BlobHex);
    }

    [Fact]
    public void The_signing_key_is_the_one_the_receive_address_comes_from()
    {
        // xrpl.js's fromMnemonic test phrase (XrpReceiveTests pins its public key and address).
        const string phrase = "assault rare scout seed design extend noble drink talk control guitar quote";
        var deriver = new HdAddressDeriver();
        using var key = deriver.DeriveXrpKey(phrase, passphrase: "");
        Assert.Equal(deriver.DeriveXrpPublicKey(phrase, passphrase: "").ToBytes(), key.PubKey.ToBytes());
        Assert.Equal(deriver.DeriveReceiveAddress(phrase, ChainId.Xrp, passphrase: "").Address,
            XrpAddress.Encode(XrpAddress.AccountIdFromPublicKey(key.PubKey.ToBytes())));
    }

    [Theory]
    [InlineData("1", 1_000_000L)]
    [InlineData("0.000001", 1L)]
    [InlineData("12.5", 12_500_000L)]
    [InlineData("100000000000", 100_000_000_000_000_000L)]
    public void Amounts_become_whole_drops(string xrp, long drops)
    {
        Assert.True(XrpTransactions.TryToDrops(decimal.Parse(xrp, CultureInfo.InvariantCulture), out var d));
        Assert.Equal(drops, d);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0000001")]            // a seventh decimal: less than a drop
    [InlineData("100000000000.000001")]  // more XRP than exists
    public void Amounts_that_are_not_whole_drops_are_refused(string xrp) =>
        Assert.False(XrpTransactions.TryToDrops(decimal.Parse(xrp, CultureInfo.InvariantCulture), out _));

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("0", 0u)]
    [InlineData(" 12345 ", 12345u)]
    [InlineData("4294967295", 4294967295u)]
    public void Destination_tags_are_whole_numbers_or_nothing(string text, uint? expected)
    {
        Assert.True(XrpDestinationTag.TryParse(text, out var tag, out var error));
        Assert.Null(error);
        Assert.Equal(expected, tag);
    }

    [Theory]
    [InlineData("4294967296")]
    [InlineData("-1")]
    [InlineData("12.0")]
    [InlineData("1e3")]
    [InlineData("12 345")]
    [InlineData("abc")]
    public void Anything_else_is_not_a_destination_tag(string text)
    {
        Assert.False(XrpDestinationTag.TryParse(text, out var tag, out var error));
        Assert.Null(tag);
        Assert.NotNull(error);
    }

    // --- rippled's answers ----------------------------------------------------------------------

    [Fact]
    public void The_open_ledger_gives_the_sequence_balance_and_flags()
    {
        var (lookup, state, ledger) = XrpSendRules.ParseAccount(Json("""
            {"account_data":{"Account":"rG1QQv2nh2gr7RCZ1P8YYcBUKCCN633jCn","Balance":"25000000","Flags":131072,
             "LedgerEntryType":"AccountRoot","OwnerCount":2,"Sequence":42},
             "ledger_current_index":90000123,"status":"success","validated":false}
            """), current: true);

        Assert.Equal(XrpAccountLookup.Found, lookup);
        Assert.Equal(new XrpAccountState(25_000_000, 42, 2, 131072), state);
        Assert.True(state!.RequiresDestinationTag);
        Assert.Equal(90000123u, ledger);
    }

    [Fact]
    public void An_account_the_ledger_does_not_have_is_not_found_not_zero()
    {
        var (lookup, state, _) = XrpSendRules.ParseAccount(Json("""
            {"account":"rG1QQv2nh2gr7RCZ1P8YYcBUKCCN633jCn","error":"actNotFound","error_code":19,
             "error_message":"Account not found.","status":"error","validated":true}
            """), current: false);
        Assert.Equal(XrpAccountLookup.NotFound, lookup);
        Assert.Null(state);
    }

    [Theory]
    [InlineData("""{"error":"tooBusy","status":"error"}""")]
    [InlineData("""{"account_data":{"Balance":"1","Flags":0,"OwnerCount":0,"Sequence":1},"status":"success","validated":false}""")]   // not validated
    [InlineData("""{"account_data":{"Balance":"1.5","Flags":0,"OwnerCount":0,"Sequence":1},"status":"success","validated":true}""")]  // not drops
    [InlineData("""{"account_data":{"Balance":"1","Flags":0,"OwnerCount":0},"status":"success","validated":true}""")]                 // no sequence
    [InlineData("""{"status":"success","validated":true}""")]
    public void Any_other_account_answer_is_unreadable(string json) =>
        Assert.Equal(XrpAccountLookup.Unreadable, XrpSendRules.ParseAccount(Json(json), current: false).Lookup);

    [Fact]
    public void The_open_ledger_answer_must_name_its_ledger()
    {
        // Without it there is no last ledger to put on the payment, so nothing to settle it by.
        var json = """{"account_data":{"Balance":"1","Flags":0,"OwnerCount":0,"Sequence":1},"status":"success","validated":false}""";
        Assert.Equal(XrpAccountLookup.Unreadable, XrpSendRules.ParseAccount(Json(json), current: true).Lookup);
    }

    [Fact]
    public void The_reserve_comes_from_the_validated_ledger_in_whole_drops()
    {
        var state = XrpSendRules.ParseServerInfo(Json("""
            {"info":{"build_version":"2.3.0","validated_ledger":{"age":2,"base_fee_xrp":0.00001,
             "hash":"AB","reserve_base_xrp":1,"reserve_inc_xrp":0.2,"seq":90000100}},"status":"success"}
            """));
        Assert.Equal(new XrpLedgerState(1_000_000, 200_000, 10, 90000100), state);
        Assert.Equal(1_400_000, state!.LockedDrops(2));
    }

    [Fact]
    public void Ripples_own_servers_write_the_reserve_in_exponent_form_and_it_still_reads_exactly()
    {
        // s1.ripple.com, as it answers today: the same figures as XRPL Labs's cluster, written 1E-5, 1E0, 2E-1.
        var state = XrpSendRules.ParseServerInfo(Json("""
            {"info":{"validated_ledger":{"age":7,"hash":"A8","seq":107146498,"base_fee_xrp":1E-5,
             "reserve_base_xrp":1E0,"reserve_inc_xrp":2E-1}},"status":"success"}
            """));
        Assert.Equal(new XrpLedgerState(1_000_000, 200_000, 10, 107146498), state);
    }

    [Theory]
    [InlineData("""{"info":{},"status":"success"}""")]
    [InlineData("""{"info":{"validated_ledger":{"base_fee_xrp":0.00001,"reserve_inc_xrp":0.2,"seq":5}},"status":"success"}""")]
    [InlineData("""{"info":{"validated_ledger":{"base_fee_xrp":0.00001,"reserve_base_xrp":0,"reserve_inc_xrp":0.2,"seq":5}},"status":"success"}""")]
    [InlineData("""{"info":{"validated_ledger":{"base_fee_xrp":0.0000001,"reserve_base_xrp":1,"reserve_inc_xrp":0.2,"seq":5}},"status":"success"}""")]
    [InlineData("""{"error":"noNetwork","status":"error"}""")]
    public void A_reserve_that_cannot_be_read_exactly_is_no_reserve(string json) =>
        Assert.Null(XrpSendRules.ParseServerInfo(Json(json)));

    [Fact]
    public void The_fee_is_what_gets_into_the_open_ledger_never_below_the_minimum()
    {
        Assert.Equal(12, XrpSendRules.ParseFee(Json("""
            {"drops":{"base_fee":"10","median_fee":"5000","minimum_fee":"10","open_ledger_fee":"12"},"status":"success"}
            """)));
        Assert.Equal(10, XrpSendRules.ParseFee(Json("""
            {"drops":{"base_fee":"10","median_fee":"5000","minimum_fee":"10","open_ledger_fee":"8"},"status":"success"}
            """)));
        Assert.Null(XrpSendRules.ParseFee(Json("""{"drops":{"base_fee":"10"},"status":"success"}""")));
    }

    [Theory]
    [InlineData("tesSUCCESS", XrpSubmitOutcome.Provisional)]   // a forecast: only a validated ledger is final
    [InlineData("terQUEUED", XrpSubmitOutcome.Provisional)]
    [InlineData("tecUNFUNDED_PAYMENT", XrpSubmitOutcome.Provisional)]
    [InlineData("temBAD_AMOUNT", XrpSubmitOutcome.Rejected)]
    [InlineData("tefPAST_SEQ", XrpSubmitOutcome.Rejected)]
    [InlineData("telINSUF_FEE_P", XrpSubmitOutcome.Rejected)]
    [InlineData("xyzSOMETHING", XrpSubmitOutcome.Unknown)]
    public void A_submit_answer_is_a_forecast_or_a_refusal(string code, XrpSubmitOutcome expected)
    {
        var (outcome, _) = XrpSubmit.ParseSubmit(Json(
            $"{{\"engine_result\":\"{code}\",\"engine_result_code\":0,\"engine_result_message\":\"m\",\"status\":\"success\"}}"));
        Assert.Equal(expected, outcome);
    }

    [Fact]
    public void A_refused_request_relayed_nothing()
    {
        var (outcome, reason) = XrpSubmit.ParseSubmit(Json(
            """{"error":"invalidTransaction","error_exception":"fails local checks: Empty SigningPubKey.","status":"error"}"""));
        Assert.Equal(XrpSubmitOutcome.Rejected, outcome);
        Assert.Contains("Empty SigningPubKey", reason);
    }

    [Fact]
    public void Only_a_validated_ledger_settles_a_payment()
    {
        Assert.Equal(XrpSubmitOutcome.Included, XrpSubmit.ParseLookup(Json(
            """{"hash":"AB","meta":{"TransactionResult":"tesSUCCESS"},"status":"success","validated":true}""")).Outcome);
        Assert.Equal(XrpSubmitOutcome.FailedFeeCharged, XrpSubmit.ParseLookup(Json(
            """{"hash":"AB","meta":{"TransactionResult":"tecNO_DST_INSUF_XRP"},"status":"success","validated":true}""")).Outcome);
        Assert.Equal(XrpSubmitOutcome.Provisional, XrpSubmit.ParseLookup(Json(
            """{"hash":"AB","meta":{"TransactionResult":"tesSUCCESS"},"status":"success","validated":false}""")).Outcome);
    }

    [Fact]
    public void Not_found_is_final_only_when_the_server_searched_every_ledger_it_could_be_in()
    {
        Assert.Equal(XrpSubmitOutcome.Rejected, XrpSubmit.ParseLookup(Json(
            """{"error":"txnNotFound","searched_all":true,"status":"error"}""")).Outcome);
        Assert.Equal(XrpSubmitOutcome.Unknown, XrpSubmit.ParseLookup(Json(
            """{"error":"txnNotFound","searched_all":false,"status":"error"}""")).Outcome);
        Assert.Equal(XrpSubmitOutcome.Unknown, XrpSubmit.ParseLookup(Json(
            """{"error":"txnNotFound","status":"error"}""")).Outcome);
    }

    [Fact]
    public void Deposit_approval_is_read_or_unknown()
    {
        Assert.True(XrpSendRules.ParseDepositAuthorized(Json(
            """{"deposit_authorized":true,"destination_account":"rB","source_account":"rA","status":"success","validated":true}""")));
        Assert.False(XrpSendRules.ParseDepositAuthorized(Json(
            """{"deposit_authorized":false,"status":"success","validated":true}""")));
        Assert.Null(XrpSendRules.ParseDepositAuthorized(Json("""{"error":"dstActNotFound","status":"error"}""")));
    }

    // --- helpers ---------------------------------------------------------------------------------

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static byte[] Decode(string address)
    {
        Assert.True(XrpAddress.TryDecode(address, out var id));
        return id;
    }

    private static readonly BigInteger Order = BigInteger.Parse(
        "0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141", NumberStyles.HexNumber);

    /// <summary>
    /// The secp256k1 key of an XRPL family seed ("s…"), as ripple-keypairs derives it: a private
    /// generator from SHA-512Half(seed ‖ i), then the account key from the generator's public key
    /// ‖ account 0 ‖ i, added to it. Test-only: the wallet signs with its BIP44 key.
    /// </summary>
    private static Key KeyFromFamilySeed(string seed)
    {
        var value = BigInteger.Zero;
        foreach (var c in seed) value = (value * 58) + XrpAddress.Alphabet.IndexOf(c, StringComparison.Ordinal);
        var full = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        Assert.Equal(21, full.Length);
        Assert.Equal(0x21, full[0]);
        var entropy = full[1..17];

        var generator = Scalar(entropy, null);
        var generatorPublic = new Key(To32(generator)).PubKey.ToBytes();
        var account = (Scalar(generatorPublic, 0) + generator) % Order;
        return new Key(To32(account));
    }

    private static BigInteger Scalar(byte[] bytes, uint? discriminator)
    {
        for (uint i = 0; ; i++)
        {
            var data = new List<byte>(bytes);
            if (discriminator is { } d) data.AddRange(BigEndian(d));
            data.AddRange(BigEndian(i));
            var k = new BigInteger(SHA512.HashData(data.ToArray())[..32], isUnsigned: true, isBigEndian: true);
            if (k > 0 && k < Order) return k;
        }
    }

    private static byte[] BigEndian(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static byte[] To32(BigInteger v)
    {
        var b = v.ToByteArray(isUnsigned: true, isBigEndian: true);
        var r = new byte[32];
        b.CopyTo(r, 32 - b.Length);
        return r;
    }
}
