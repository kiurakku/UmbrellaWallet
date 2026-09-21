using System.Text.Json;
using NBitcoin.DataEncoders;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// XRP receive and balance (roadmap N.4).
///
/// Nothing here is checked against this wallet's own output. The seed-to-key step is pinned to
/// xrpl.js's own <c>Wallet.fromMnemonic</c> test; the key-to-address step to the worked example in
/// XRPL's address documentation. Together they fix phrase → address end to end, which is what decides
/// whether the same phrase restores these funds in Xaman, Ledger or Trust.
/// </summary>
public sealed class XrpReceiveTests
{
    /// <summary>xrpl.js packages/xrpl/test/wallet — the default-path mnemonic test.</summary>
    private const string XrplJsMnemonic =
        "assault rare scout seed design extend noble drink talk control guitar quote";

    private const string XrplJsPublicKey = "035953FCD81D001CF634EB44A87940F3F98ADF2483D09C914BAED0539BE50F385D";

    private static readonly HdAddressDeriver Deriver = new();

    [Fact]
    public void The_key_at_the_default_path_is_the_one_xrpl_js_derives()
    {
        var key = Deriver.DeriveXrpPublicKey(XrplJsMnemonic, passphrase: "");
        Assert.Equal(XrplJsPublicKey, Encoders.Hex.EncodeData(key.ToBytes()).ToUpperInvariant());
    }

    [Fact]
    public void A_public_key_encodes_to_the_address_XRPL_documents()
    {
        // xrpl.org, "Address Encoding": this public key gives this classic address. It exercises the
        // hand-written RIPEMD-160 as well as the base58 alphabet and checksum.
        var publicKey = Encoders.Hex.DecodeData("ED9434799226374926EDA3B54B1B461B4ABF7237962EAE18528FEA67595397FA32");
        Assert.Equal("rDTXLQ7ZKZVKz33zJbHjgVShjsBnqMBhmN", XrpAddress.Encode(XrpAddress.AccountIdFromPublicKey(publicKey)));
    }

    [Fact]
    public void The_receive_address_is_the_documented_encoding_of_the_pinned_key()
    {
        var address = Deriver.DeriveReceiveAddress(XrplJsMnemonic, ChainId.Xrp, passphrase: "");
        var expected = XrpAddress.Encode(XrpAddress.AccountIdFromPublicKey(Encoders.Hex.DecodeData(XrplJsPublicKey)));

        Assert.Equal(expected, address.Address);
        Assert.StartsWith("r", address.Address);
        Assert.True(XrpAddress.IsValid(address.Address));
        Assert.Equal("m/44'/144'/0'/0/0", address.DerivationPath);
    }

    [Fact]
    public void A_passphrase_gives_a_different_XRP_account()
    {
        // The hidden wallet must not share its XRP address with the normal one.
        var normal = Deriver.DeriveReceiveAddress(XrplJsMnemonic, ChainId.Xrp, passphrase: "").Address;
        var hidden = Deriver.DeriveReceiveAddress(XrplJsMnemonic, ChainId.Xrp, passphrase: "hidden").Address;
        Assert.NotEqual(normal, hidden);
    }

    [Fact]
    public void XRP_is_receive_and_balance_only_until_a_signed_payment_is_proven()
    {
        var info = ChainCatalog.Get(ChainId.Xrp);
        Assert.True(info.CanReceive);
        Assert.True(info.CanSyncBalance);
        Assert.False(info.CanSend);
        Assert.True(ChainCatalog.HasRealAddress(ChainId.Xrp));
    }

    // --- the balance answer ------------------------------------------------------------------------

    private static decimal? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return XrpLedger.ParseAccountInfo(doc.RootElement);
    }

    [Fact]
    public void A_validated_account_reads_its_balance_in_XRP()
    {
        Assert.Equal(25.5m, Parse(
            """{"status":"success","validated":true,"account_data":{"Account":"r…","Balance":"25500000"}}"""));
    }

    [Fact]
    public void An_account_the_ledger_does_not_have_yet_is_a_real_zero()
    {
        // Never funded with the reserve: the ledger says so. That is an answer, not a failure.
        Assert.Equal(0m, Parse("""{"status":"error","error":"actNotFound","error_code":19}"""));
    }

    [Theory]
    [InlineData("""{"status":"error","error":"noNetwork"}""")]
    [InlineData("""{"status":"error","error":"tooBusy"}""")]
    [InlineData("""{"status":"success","validated":false,"account_data":{"Balance":"1000000"}}""")]  // not final
    [InlineData("""{"status":"success","account_data":{"Balance":"1000000"}}""")]                    // no validated flag
    [InlineData("""{"status":"success","validated":true,"account_data":{"Balance":"12.5"}}""")]      // not drops
    [InlineData("""{"status":"success","validated":true,"account_data":{"Balance":"-1"}}""")]
    [InlineData("""{"status":"success","validated":true,"account_data":{"Balance":1000000}}""")]     // number, not string
    [InlineData("""{"status":"success","validated":true}""")]
    [InlineData("""[]""")]
    [InlineData("""{"status":"success","validated":true,"account_data":"oops"}""")]
    [InlineData("""{"status":{"x":1}}""")]
    [InlineData("""{"status":"error","error":{"x":1}}""")]
    public void Anything_else_is_unknown_never_zero(string json)
    {
        Assert.Null(Parse(json));
    }
}
