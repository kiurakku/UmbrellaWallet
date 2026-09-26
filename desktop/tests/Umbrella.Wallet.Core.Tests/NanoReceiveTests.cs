using System.Text.Json;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Nano receive and balance, against the Nano documentation's own BIP39/BIP44 test vector
/// (docs.nano.org, "Key management"): phrase + passphrase → seed → m/44'/165'/0' → private key → public
/// key → address. Each stage is asserted on its own, so a failure says which one is wrong — a wrong
/// public key with a right private key means the ed25519-BLAKE2b step; a wrong address with a right key
/// means the encoding or the checksum.
/// </summary>
public sealed class NanoReceiveTests
{
    private const string Phrase =
        "edge defense waste choose enrich upon flee junk siren film clown finish luggage leader kid quick brick print evidence swap drill paddle truly occur";

    private const string Passphrase = "some password";

    private const string Seed =
        "0dc285fde768f7ff29b66ce7252d56ed92fe003b605907f7a4f683c3dc8586d34a914d3c71fc099bb38ee4a59e5b081a3497b7a323e90cc68f67b5837690310c";

    private const string PrivateKey = "3be4fc2ef3f3b7374e6fc4fb6e7bb153f8a2998b3b3dab50853eabe128024143";
    private const string PublicKey = "5b65b0e8173ee0802c2c3e6c9080d1a16b06de1176c938a924f58670904e82c4";
    private const string Address = "nano_1pu7p5n3ghq1i1p4rhmek41f5add1uh34xpb94nkbxe8g4a6x1p69emk8y1d";

    private static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    [Fact]
    public void Every_stage_of_the_documented_vector_reproduces()
    {
        var seed = new Mnemonic(Phrase, Wordlist.English).DeriveSeed(Passphrase);
        Assert.Equal(Seed, Hex(seed));

        var priv = NanoAccounts.DerivePrivateKey(seed, 0);
        Assert.Equal(PrivateKey, Hex(priv));

        var pub = NanoAccounts.PublicKey(priv);
        Assert.Equal(PublicKey, Hex(pub));

        Assert.Equal(Address, NanoAccounts.Address(pub));
    }

    [Fact]
    public void The_wallets_deriver_gives_the_same_account()
    {
        var address = new Umbrella.Wallet.Core.Derivation.HdAddressDeriver()
            .DeriveReceiveAddress(Phrase, ChainId.Nano, 0, Passphrase);

        Assert.Equal(Address, address.Address);
        Assert.Equal("m/44'/165'/0'", address.DerivationPath);
    }

    [Fact]
    public void An_address_decodes_back_to_its_key_and_the_old_prefix_is_the_same_account()
    {
        Assert.Equal(PublicKey, Hex(NanoAccounts.TryDecode(Address)!));
        Assert.Equal(PublicKey, Hex(NanoAccounts.TryDecode("xrb_" + Address["nano_".Length..])!));
    }

    [Theory]
    // One character changed in the key part, then in the checksum: both must fail the checksum.
    [InlineData("nano_1pu7p5n3ghq1i1p4rhmek41f5add1uh34xpb94nkbxe8g4a6x1p69emk8y1e")]
    [InlineData("nano_1pu7p5n3ghq1i1p4rhmek41f5add1uh34xpb94nkbxe8g4a6x1p79emk8y1d")]
    // Too short, wrong prefix, a character Nano's alphabet does not have ('2', 'l', 'v', '0').
    [InlineData("nano_1pu7p5n3ghq1i1p4rhmek41f5add1uh34xpb94nkbxe8g4a6x1p69emk8y1")]
    [InlineData("xno_1pu7p5n3ghq1i1p4rhmek41f5add1uh34xpb94nkbxe8g4a6x1p69emk8y1d")]
    [InlineData("nano_2pu7p5n3ghq1i1p4rhmek41f5add1uh34xpb94nkbxe8g4a6x1p69emk8y1d")]
    [InlineData("nano_lpu7p5n3ghq1i1p4rhmek41f5add1uh34xpb94nkbxe8g4a6x1p69emk8y1d")]
    // The first character carries the 4 padding bits: anything past '1'/'3' sets one of them.
    [InlineData("nano_5pu7p5n3ghq1i1p4rhmek41f5add1uh34xpb94nkbxe8g4a6x1p69emk8y1d")]
    public void A_damaged_or_foreign_address_is_refused(string address) =>
        Assert.Null(NanoAccounts.TryDecode(address));

    [Fact]
    public void The_burn_address_is_a_valid_address()
    {
        // All-zero key: a well-known account that anyone can check against a block explorer.
        Assert.NotNull(NanoAccounts.TryDecode("nano_1111111111111111111111111111111111111111111111111111hifc8npp"));
        Assert.Equal("nano_1111111111111111111111111111111111111111111111111111hifc8npp", NanoAccounts.Address(new byte[32]));
    }

    [Fact]
    public void A_balance_counts_what_was_pocketed_and_what_is_waiting()
    {
        using var doc = JsonDocument.Parse("""
            {"balance":"1500000000000000000000000000000","pending":"250000000000000000000000000000","receivable":"250000000000000000000000000000"}
            """);
        var balance = NanoAccounts.ParseAccountBalance(doc.RootElement)!;

        Assert.Equal(1.5m, balance.Balance);
        Assert.Equal(0.25m, balance.Receivable);
        Assert.Equal(1.75m, balance.Total);
    }

    [Fact]
    public void Raw_amounts_beyond_what_a_decimal_holds_still_read()
    {
        // The whole supply in raw is ~1.3 × 10^38 — far past decimal's ~7.9 × 10^28.
        using var doc = JsonDocument.Parse("""{"balance":"133248297000000000000000000000000000000","receivable":"0"}""");
        Assert.Equal(133_248_297m, NanoAccounts.ParseAccountBalance(doc.RootElement)!.Balance);
    }

    [Fact]
    public void An_error_answer_is_not_a_zero()
    {
        using var doc = JsonDocument.Parse("""{"error":"Bad account number"}""");
        Assert.Null(NanoAccounts.ParseAccountBalance(doc.RootElement));
    }
}
