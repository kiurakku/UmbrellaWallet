using System.Numerics;
using System.Text.Json;
using NBitcoin.DataEncoders;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// NEAR send (roadmap N.7), pinned to near-api-js's own "serialize and sign transfer tx object" test
/// (near/near-api-js, test/unit/signers/key_pair_signer.test.ts): its keypair, accounts, nonce, block
/// hash and amount, and the exact signature and SignedTransaction bytes it expects — never this
/// wallet's own output.
/// </summary>
public sealed class NearSendTests
{
    // KeyPair.fromString('ed25519:3hoMW1...'): 64 bytes, the 32-byte seed then the public key.
    private const string SecretKey = "3hoMW1HvnRLSFCLZnvPzWeoGwtdHzke34B2cTHM8rhcbG3TbuLKtShTv3DvyejnXKXKBiV7YPkLeqUHN1ghnqpFv";
    private const string PublicKey = "Anu7LYDfpLtkP7E16LT9imXF694BdQaa9ufVkQiwTQxC";

    private static readonly byte[] BlockHash =
    [
        15, 164, 115, 253, 38, 144, 29, 242, 150, 190, 106, 220, 76, 196, 223, 52, 208, 64, 239, 162, 67, 82, 36, 182,
        152, 105, 16, 230, 48, 194, 254, 246,
    ];

    private const string ExpectedSignature = "lpqDMyGG7pdV5IOTJVJYBuGJo9LSu0tHYOlEQ+l+HE8i3u7wBZqOlxMQDtpuGRRNp+ig735TmyBwi6HY0CG9AQ==";

    private const string ExpectedSignedHex =
        "09000000746573742e6e65617200917b3d268d4b58f7fec1b150bd68d69be3ee5d4cc39855e341538465bb77860d01000000000000000d00000077686174657665722e6e6561720fa473fd26901df296be6adc4cc4df34d040efa2435224b6986910e630c2fef601000000030100000000000000000000000000000000969a83332186ee9755e4839325525806e189a3d2d2bb4b4760e94443e97e1c4f22deeef0059a8e9713100eda6e19144da7e8a0ef7e539b20708ba1d8d021bd01";

    private static byte[] Seed => Encoders.Base58.DecodeData(SecretKey)[..32];

    private static NearTransfer Reference(BigInteger? yocto = null) =>
        new("test.near", Encoders.Base58.DecodeData(PublicKey), 1, "whatever.near", BlockHash, yocto ?? BigInteger.One);

    [Fact]
    public void The_reference_seed_is_the_reference_public_key()
    {
        var pub = Umbrella.Wallet.Core.Derivation.Slip10Ed25519.PublicKey(Seed);
        Assert.Equal(PublicKey, Encoders.Base58.EncodeData(pub));
    }

    [Fact]
    public void The_signature_is_the_one_near_api_js_makes()
    {
        var (_, _, signature) = NearTransactions.Sign(Reference(), Seed);
        Assert.Equal(ExpectedSignature, Convert.ToBase64String(signature));
    }

    [Fact]
    public void The_signed_transaction_is_byte_identical_to_near_api_js()
    {
        var (signed, _, _) = NearTransactions.Sign(Reference(), Seed);
        Assert.Equal(ExpectedSignedHex, Convert.ToHexString(Convert.FromBase64String(signed)).ToLowerInvariant());
    }

    [Fact]
    public void The_hash_is_base58_of_sha256_of_the_transaction()
    {
        var t = Reference();
        var expected = Encoders.Base58.EncodeData(System.Security.Cryptography.SHA256.HashData(NearTransactions.TransactionBytes(t)));
        Assert.Equal(expected, NearTransactions.Sign(t, Seed).Hash);
    }

    [Fact]
    public void A_key_that_is_not_the_senders_is_refused()
    {
        var other = new byte[32];
        other[0] = 1;
        Assert.Throws<InvalidOperationException>(() => NearTransactions.Sign(Reference(), other));
    }

    [Fact]
    public void Only_a_positive_amount_that_fits_u128_is_encoded()
    {
        Assert.Throws<ArgumentException>(() => NearTransactions.TransactionBytes(Reference(BigInteger.Zero)));
        Assert.Throws<ArgumentException>(() => NearTransactions.TransactionBytes(Reference(BigInteger.One << 128)));
    }

    // --- account ids -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("alice.near", true)]
    [InlineData("a-b_c.near", true)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]   // implicit
    [InlineData("0x32400084c286cf3e17e7b677ea9583e60a000324", true)]                         // eth-implicit
    [InlineData("Alice.near", false)]      // uppercase
    [InlineData("alice..near", false)]     // separators in a row
    [InlineData(".alice", false)]
    [InlineData("alice.", false)]
    [InlineData("a", false)]               // too short
    [InlineData("alice near", false)]
    public void Account_ids_follow_NEARs_own_rule(string id, bool valid)
    {
        Assert.Equal(valid, NearTransactions.IsValidAccountId(id));
    }

    [Fact]
    public void Implicit_accounts_are_64_lowercase_hex()
    {
        Assert.True(NearTransactions.IsImplicit(new string('a', 64)));
        Assert.False(NearTransactions.IsImplicit(new string('A', 64)));
        Assert.False(NearTransactions.IsImplicit("alice.near"));
    }

    // --- amounts -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("1", "1000000000000000000000000")]
    [InlineData("0.5", "500000000000000000000000")]
    [InlineData("0.000000000000000000000001", "1")]
    public void Amounts_convert_to_yocto_exactly(string near, string yocto)
    {
        Assert.True(NearTransactions.TryToYocto(decimal.Parse(near, System.Globalization.CultureInfo.InvariantCulture), out var y));
        Assert.Equal(BigInteger.Parse(yocto), y);
    }

    [Fact]
    public void Amounts_finer_than_a_yocto_are_refused()
    {
        Assert.False(NearTransactions.TryToYocto(0.0000000000000000000000001m, out _));
        Assert.False(NearTransactions.TryToYocto(0m, out _));
    }

    // --- what the RPC says ----------------------------------------------------------------------------

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Spendable_leaves_the_storage_the_account_must_pay_for()
    {
        var state = NearRpc.ParseViewAccount(Json("""
            {"jsonrpc":"2.0","id":1,"result":{"amount":"5000000000000000000000000","locked":"0","storage_usage":182,"block_hash":"x"}}
            """));
        Assert.NotNull(state);
        // 182 bytes × 10^19 yocto = 0.00182 NEAR kept for storage.
        Assert.Equal(BigInteger.Parse("5000000000000000000000000") - BigInteger.Parse("1820000000000000000000"), state!.Spendable);
    }

    [Fact]
    public void The_access_key_gives_the_nonce_and_the_block_to_sign_against()
    {
        var hash = Encoders.Base58.EncodeData(BlockHash);
        var key = NearRpc.ParseAccessKey(Json(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"nonce\":85,\"permission\":\"FullAccess\",\"block_hash\":\"" + hash + "\",\"block_height\":1}}"));
        Assert.NotNull(key);
        Assert.Equal(85UL, key!.Value.Nonce);
        Assert.Equal(BlockHash, key.Value.BlockHash);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{"amount":"lots","locked":"0","storage_usage":182}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{"error":"account does not exist"}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"error":{"name":"HANDLER_ERROR"}}""")]
    public void An_account_answer_that_is_not_understood_stops_the_send(string json)
    {
        Assert.Null(NearRpc.ParseViewAccount(Json(json)));
    }

    [Fact]
    public void A_missing_account_is_recognised_as_such()
    {
        Assert.True(NearRpc.IsUnknownAccount(Json("""{"jsonrpc":"2.0","id":1,"error":{"cause":{"name":"UNKNOWN_ACCOUNT"},"name":"HANDLER_ERROR"}}""")));
        Assert.False(NearRpc.IsUnknownAccount(Json("""{"jsonrpc":"2.0","id":1,"error":{"name":"TIMEOUT_ERROR"}}""")));
    }
}
