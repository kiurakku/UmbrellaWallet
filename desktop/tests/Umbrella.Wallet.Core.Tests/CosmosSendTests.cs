using System.Globalization;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Cosmos Hub (ATOM) send (roadmap N.6).
///
/// Nothing here is checked against this wallet's own output. The vectors are cosmjs's own
/// (packages/proto-signing/src/testutils.ts, used by its DirectSecp256k1HdWallet tests): the faucet
/// mnemonic's key signs one MsgSend at sequences 0, 1 and 2, and each vector fixes the body bytes, the
/// auth-info bytes, the sign bytes, the signature and the signed transaction. Matching all five, three
/// times, pins the protobuf encoding, the default-field omission (sequence 0 is not written), the
/// SignDoc and the deterministic low-S signature at once.
/// </summary>
public sealed class CosmosSendTests
{
    private const string FaucetMnemonic =
        "economy stock theory fatal elder harbor betray wasp final emotion task crumble siren bottom lizard educate guess current outdoor pair theory focus wife stone";

    private const string FaucetAddress = "cosmos1pkptre7fdkl6gfrzlesjjvhxhlc3r4gmmk8rs6";
    private const string Recipient = "cosmos1qypqxpq9qcrsszg2pvxq6rs0zqg3yyc5lzv7xu";

    private const string Body =
        "0a90010a1c2f636f736d6f732e62616e6b2e763162657461312e4d736753656e6412700a2d636f736d6f7331706b707472653766646b6c366766727a6c65736a6a766878686c63337234676d6d6b38727336122d636f736d6f7331717970717870713971637273737a673270767871367273307a716733797963356c7a763778751a100a0575636f736d120731323334353637";

    public static TheoryData<ulong, string, string, string, string> Vectors => new()
    {
        {
            0,
            "0a4e0a460a1f2f636f736d6f732e63727970746f2e736563703235366b312e5075624b657912230a21034f04181eeba35391b858633a765c4a0c189697b40d216354d50890d350c7029012040a02080112130a0d0a0575636f736d12043230303010c09a0c",
            "0a93010a90010a1c2f636f736d6f732e62616e6b2e763162657461312e4d736753656e6412700a2d636f736d6f7331706b707472653766646b6c366766727a6c65736a6a766878686c63337234676d6d6b38727336122d636f736d6f7331717970717870713971637273737a673270767871367273307a716733797963356c7a763778751a100a0575636f736d12073132333435363712650a4e0a460a1f2f636f736d6f732e63727970746f2e736563703235366b312e5075624b657912230a21034f04181eeba35391b858633a765c4a0c189697b40d216354d50890d350c7029012040a02080112130a0d0a0575636f736d12043230303010c09a0c1a0c73696d642d74657374696e672001",
            "c9dd20e07464d3a688ff4b710b1fbc027e495e797cfa0b4804da2ed117959227772de059808f765aa29b8f92edf30f4c2c5a438e30d3fe6897daa7141e3ce6f9",
            "0a93010a90010a1c2f636f736d6f732e62616e6b2e763162657461312e4d736753656e6412700a2d636f736d6f7331706b707472653766646b6c366766727a6c65736a6a766878686c63337234676d6d6b38727336122d636f736d6f7331717970717870713971637273737a673270767871367273307a716733797963356c7a763778751a100a0575636f736d12073132333435363712650a4e0a460a1f2f636f736d6f732e63727970746f2e736563703235366b312e5075624b657912230a21034f04181eeba35391b858633a765c4a0c189697b40d216354d50890d350c7029012040a02080112130a0d0a0575636f736d12043230303010c09a0c1a40c9dd20e07464d3a688ff4b710b1fbc027e495e797cfa0b4804da2ed117959227772de059808f765aa29b8f92edf30f4c2c5a438e30d3fe6897daa7141e3ce6f9"
        },
        {
            1,
            "0a500a460a1f2f636f736d6f732e63727970746f2e736563703235366b312e5075624b657912230a21034f04181eeba35391b858633a765c4a0c189697b40d216354d50890d350c7029012040a020801180112130a0d0a0575636f736d12043230303010c09a0c",
            "0a93010a90010a1c2f636f736d6f732e62616e6b2e763162657461312e4d736753656e6412700a2d636f736d6f7331706b707472653766646b6c366766727a6c65736a6a766878686c63337234676d6d6b38727336122d636f736d6f7331717970717870713971637273737a673270767871367273307a716733797963356c7a763778751a100a0575636f736d12073132333435363712670a500a460a1f2f636f736d6f732e63727970746f2e736563703235366b312e5075624b657912230a21034f04181eeba35391b858633a765c4a0c189697b40d216354d50890d350c7029012040a020801180112130a0d0a0575636f736d12043230303010c09a0c1a0c73696d642d74657374696e672001",
            "525adc7e61565a509c60497b798c549fbf217bb5cd31b24cc9b419d098cc95330c99ecc4bc72448f85c365a4e3f91299a3d40412fb3751bab82f1940a83a0a4c",
            "0a93010a90010a1c2f636f736d6f732e62616e6b2e763162657461312e4d736753656e6412700a2d636f736d6f7331706b707472653766646b6c366766727a6c65736a6a766878686c63337234676d6d6b38727336122d636f736d6f7331717970717870713971637273737a673270767871367273307a716733797963356c7a763778751a100a0575636f736d12073132333435363712670a500a460a1f2f636f736d6f732e63727970746f2e736563703235366b312e5075624b657912230a21034f04181eeba35391b858633a765c4a0c189697b40d216354d50890d350c7029012040a020801180112130a0d0a0575636f736d12043230303010c09a0c1a40525adc7e61565a509c60497b798c549fbf217bb5cd31b24cc9b419d098cc95330c99ecc4bc72448f85c365a4e3f91299a3d40412fb3751bab82f1940a83a0a4c"
        },
        {
            2,
            "0a500a460a1f2f636f736d6f732e63727970746f2e736563703235366b312e5075624b657912230a21034f04181eeba35391b858633a765c4a0c189697b40d216354d50890d350c7029012040a020801180212130a0d0a0575636f736d12043230303010c09a0c",
            "0a93010a90010a1c2f636f736d6f732e62616e6b2e763162657461312e4d736753656e6412700a2d636f736d6f7331706b707472653766646b6c366766727a6c65736a6a766878686c63337234676d6d6b38727336122d636f736d6f7331717970717870713971637273737a673270767871367273307a716733797963356c7a763778751a100a0575636f736d12073132333435363712670a500a460a1f2f636f736d6f732e63727970746f2e736563703235366b312e5075624b657912230a21034f04181eeba35391b858633a765c4a0c189697b40d216354d50890d350c7029012040a020801180212130a0d0a0575636f736d12043230303010c09a0c1a0c73696d642d74657374696e672001",
            "f3f2ca73806f2abbf6e0fe85f9b8af66f0e9f7f79051fdb8abe5bb8633b17da132e82d577b9d5f7a6dae57a144efc9ccc6eef15167b44b3b22a57240109762af",
            "0a93010a90010a1c2f636f736d6f732e62616e6b2e763162657461312e4d736753656e6412700a2d636f736d6f7331706b707472653766646b6c366766727a6c65736a6a766878686c63337234676d6d6b38727336122d636f736d6f7331717970717870713971637273737a673270767871367273307a716733797963356c7a763778751a100a0575636f736d12073132333435363712670a500a460a1f2f636f736d6f732e63727970746f2e736563703235366b312e5075624b657912230a21034f04181eeba35391b858633a765c4a0c189697b40d216354d50890d350c7029012040a020801180212130a0d0a0575636f736d12043230303010c09a0c1a40f3f2ca73806f2abbf6e0fe85f9b8af66f0e9f7f79051fdb8abe5bb8633b17da132e82d577b9d5f7a6dae57a144efc9ccc6eef15167b44b3b22a57240109762af"
        },
    };

    private static readonly HdAddressDeriver Deriver = new();

    private static CosmosSend VectorSend(ulong sequence) => new(
        FaucetAddress, Recipient, 1234567, "ucosm", Memo: "", TimeoutHeight: 0,
        Deriver.DeriveCosmosPublicKey(FaucetMnemonic, passphrase: "").ToBytes(),
        AccountNumber: 1, sequence, Fee: 2000, GasLimit: 200000, ChainId: "simd-testing");

    [Fact]
    public void The_faucet_mnemonic_gives_the_key_and_address_cosmjs_signs_with()
    {
        Assert.Equal("034F04181EEBA35391B858633A765C4A0C189697B40D216354D50890D350C70290",
            Convert.ToHexString(Deriver.DeriveCosmosPublicKey(FaucetMnemonic, passphrase: "").ToBytes()));
        Assert.Equal(FaucetAddress, Deriver.DeriveReceiveAddress(FaucetMnemonic, ChainId.Atom, passphrase: "").Address);
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void A_transfer_encodes_and_signs_to_cosmjs_bytes(ulong sequence, string authInfo, string signBytes, string signature, string signedTx)
    {
        var send = VectorSend(sequence);
        var body = CosmosTransactions.BodyBytes(send);
        var auth = CosmosTransactions.AuthInfoBytes(send);

        Assert.Equal(Body, Convert.ToHexString(body).ToLowerInvariant());
        Assert.Equal(authInfo, Convert.ToHexString(auth).ToLowerInvariant());
        Assert.Equal(signBytes, Convert.ToHexString(CosmosTransactions.SignDocBytes(body, auth, send.ChainId, send.AccountNumber)).ToLowerInvariant());

        using var key = Deriver.DeriveCosmosKey(FaucetMnemonic, passphrase: "");
        var (tx, hash) = CosmosTransactions.Sign(send, key);

        Assert.Equal(signedTx, Convert.ToHexString(tx).ToLowerInvariant());
        Assert.EndsWith(signature, Convert.ToHexString(tx).ToLowerInvariant());
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(tx)), hash);
    }

    [Fact]
    public void A_memo_and_a_timeout_height_are_written_after_the_message()
    {
        // Field 2 (memo, a string) and field 3 (timeout_height, a varint) of TxBody. That a live node
        // decodes them back is checked in CosmosSendLiveTests.
        var body = CosmosTransactions.BodyBytes(VectorSend(0) with { Memo = "104", TimeoutHeight = 33079342 });
        var tail = Convert.ToHexString(body[^10..]).ToLowerInvariant();
        Assert.Equal("1203313034" + "18ae80e30f", tail);   // 33079342 as a LEB128 varint
        Assert.StartsWith(Body, Convert.ToHexString(body).ToLowerInvariant());
    }

    [Fact]
    public void A_key_that_is_not_the_senders_is_refused_before_signing()
    {
        using var other = Deriver.DeriveCosmosKey(FaucetMnemonic, addressIndex: 1, passphrase: "");
        Assert.Throws<InvalidOperationException>(() => CosmosTransactions.Sign(VectorSend(0), other));
    }

    [Theory]
    [InlineData("1", 1_000_000UL)]
    [InlineData("0.000001", 1UL)]
    [InlineData("12.5", 12_500_000UL)]
    public void Amounts_become_whole_micro_atom(string atom, ulong micro)
    {
        Assert.True(CosmosTransactions.TryToMicro(decimal.Parse(atom, CultureInfo.InvariantCulture), out var m));
        Assert.Equal(micro, m);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0000001")]
    public void Amounts_that_are_not_whole_micro_atom_are_refused(string atom) =>
        Assert.False(CosmosTransactions.TryToMicro(decimal.Parse(atom, CultureInfo.InvariantCulture), out _));

    [Fact]
    public void Memos_fit_the_hubs_limit_and_carry_no_control_characters()
    {
        Assert.True(CosmosTransactions.TryValidateMemo("  104 ", out var memo, out _));
        Assert.Equal("104", memo);
        Assert.True(CosmosTransactions.TryValidateMemo(new string('a', 256), out _, out _));
        Assert.False(CosmosTransactions.TryValidateMemo(new string('a', 257), out _, out _));
        Assert.False(CosmosTransactions.TryValidateMemo("line\nbreak", out _, out _));
    }

    // --- a Hub node's answers (shapes as cosmos-rest.publicnode.com and Keplr's node give them) -----

    private static System.Text.Json.JsonElement Json(string text) => System.Text.Json.JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void An_account_gives_its_number_sequence_and_key()
    {
        var (lookup, state) = CosmosSendRules.ParseAccount(200, Json("""
            {"account":{"@type":"/cosmos.auth.v1beta1.BaseAccount","address":"cosmos19rl4cm2hmr8afy4kldpxz3fka4jguq0auqdal4",
             "pub_key":{"@type":"/cosmos.crypto.secp256k1.PubKey","key":"Ak9OKtmcNNYLm6YoPJQxqEGK+GcyEpYfl6d7Y3f80Fti"},
             "account_number":"1425847","sequence":"7"}}
            """));
        Assert.Equal(CosmosAccountLookup.Found, lookup);
        Assert.Equal(1425847UL, state!.AccountNumber);
        Assert.Equal(7UL, state.Sequence);
        Assert.Equal(33, state.PublicKey!.Length);
    }

    [Fact]
    public void A_new_account_has_no_key_on_record_yet()
    {
        var (lookup, state) = CosmosSendRules.ParseAccount(200, Json("""
            {"account":{"@type":"/cosmos.auth.v1beta1.BaseAccount","address":"cosmos1x","pub_key":null,"account_number":"5","sequence":"0"}}
            """));
        Assert.Equal(CosmosAccountLookup.Found, lookup);
        Assert.Null(state!.PublicKey);
        Assert.Equal(0UL, state.Sequence);
    }

    [Fact]
    public void An_address_the_chain_never_saw_is_not_found_and_anything_else_is_not_a_default()
    {
        Assert.Equal(CosmosAccountLookup.NotFound, CosmosSendRules.ParseAccount(404, Json(
            """{"code":5,"message":"account cosmos1pkptre7fdkl6gfrzlesjjvhxhlc3r4gmmk8rs6 not found","details":[]}""")).Lookup);
        Assert.Equal(CosmosAccountLookup.Unsupported, CosmosSendRules.ParseAccount(200, Json(
            """{"account":{"@type":"/cosmos.vesting.v1beta1.ContinuousVestingAccount","base_vesting_account":{}}}""")).Lookup);
        Assert.Equal(CosmosAccountLookup.Unreadable, CosmosSendRules.ParseAccount(500, Json("""{"code":13,"message":"internal"}""")).Lookup);
        Assert.Equal(CosmosAccountLookup.Unreadable, CosmosSendRules.ParseAccount(200, Json(
            """{"account":{"@type":"/cosmos.auth.v1beta1.BaseAccount","account_number":"5"}}""")).Lookup);
        Assert.Equal(CosmosAccountLookup.Unreadable, CosmosSendRules.ParseAccount(200, null).Lookup);
    }

    [Fact]
    public void The_node_names_its_chain_and_latest_block()
    {
        Assert.Equal("cosmoshub-4", CosmosSendRules.ParseNodeNetwork(Json(
            """{"default_node_info":{"network":"cosmoshub-4","version":"0.38.22"},"application_version":{"name":"gaia"}}""")));
        Assert.Equal((33079242UL, "cosmoshub-4"), CosmosSendRules.ParseLatestBlock(Json(
            """{"block_id":{"hash":"TH0a"},"block":{"header":{"chain_id":"cosmoshub-4","height":"33079242"}}}""")));
        Assert.Null(CosmosSendRules.ParseLatestBlock(Json("""{"block":{"header":{"chain_id":"cosmoshub-4","height":"0"}}}""")));
    }

    [Fact]
    public void The_gas_price_comes_from_the_fee_market_in_uatom()
    {
        Assert.Equal(0.005m, CosmosSendRules.ParseGasPrice(Json("""{"price":{"denom":"uatom","amount":"0.005000000000000000"}}""")));
        Assert.Null(CosmosSendRules.ParseGasPrice(Json("""{"price":{"denom":"uosmo","amount":"0.005"}}""")));
        Assert.Null(CosmosSendRules.ParseGasPrice(Json("""{"code":12,"message":"Not Implemented"}""")));
    }

    [Fact]
    public void The_fee_leaves_room_for_the_gas_and_the_price_to_move()
    {
        // 72,000 gas simulated at 0.005 uatom: a 93,600 limit, and 93,600 × 0.0075 = 702 uatom.
        Assert.Equal((93_600UL, 702UL), CosmosSendRules.FeeFor(72_000, 0.005m));
    }

    [Fact]
    public void A_simulation_gives_the_gas_or_the_reason_it_would_be_refused()
    {
        Assert.Equal((72_000UL, (string?)null), CosmosSendRules.ParseSimulation(200, Json(
            """{"gas_info":{"gas_wanted":"0","gas_used":"72000"},"result":{"data":""}}""")));
        var (gas, error) = CosmosSendRules.ParseSimulation(400, Json(
            """{"code":5,"message":"spendable balance 3uatom is smaller than 1000uatom: insufficient funds","details":[]}"""));
        Assert.Null(gas);
        Assert.Contains("insufficient funds", error);
    }

    [Fact]
    public void A_sync_broadcast_is_a_mempool_entry_or_a_refusal_that_cost_nothing()
    {
        Assert.Equal(CosmosSubmitOutcome.Pending, CosmosSendRules.ParseBroadcast(200, Json(
            """{"tx_response":{"height":"0","txhash":"AB","codespace":"","code":0,"raw_log":""}}""")).Outcome);
        var refused = CosmosSendRules.ParseBroadcast(200, Json(
            """{"tx_response":{"height":"0","txhash":"709E","codespace":"sdk","code":32,"raw_log":"account sequence mismatch, expected 8, got 7"}}"""));
        Assert.Equal(CosmosSubmitOutcome.Rejected, refused.Outcome);
        Assert.Contains("sequence mismatch", refused.Reason);
        Assert.Equal(CosmosSubmitOutcome.Unknown, CosmosSendRules.ParseBroadcast(502, null).Outcome);
        Assert.Equal(CosmosSubmitOutcome.Unknown, CosmosSendRules.ParseBroadcast(0, null).Outcome);
    }

    [Fact]
    public void Only_a_block_settles_a_transfer()
    {
        Assert.Equal(CosmosSubmitOutcome.Included, CosmosSendRules.ParseLookup(200, Json(
            """{"tx":{},"tx_response":{"height":"33079250","txhash":"AB","code":0,"raw_log":""}}""")).Outcome);
        Assert.Equal(CosmosSubmitOutcome.FailedFeeCharged, CosmosSendRules.ParseLookup(200, Json(
            """{"tx":{},"tx_response":{"height":"33079250","txhash":"AB","code":11,"raw_log":"out of gas"}}""")).Outcome);
        Assert.Equal(CosmosSubmitOutcome.Pending, CosmosSendRules.ParseLookup(404, Json(
            """{"code":5,"message":"tx not found: AB","details":[]}""")).Outcome);
        Assert.Equal(CosmosSubmitOutcome.Unknown, CosmosSendRules.ParseLookup(500, Json("""{"code":13}""")).Outcome);
    }
}
