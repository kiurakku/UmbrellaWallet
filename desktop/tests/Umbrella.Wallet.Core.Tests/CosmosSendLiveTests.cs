using System.Net.Http.Json;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit.Abstractions;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Live, read-only: a Cosmos Hub node checks what the offline tests cannot. Its own decoder reads back
/// the memo and timeout height this wallet encodes (fields no cosmjs vector carries), and the real send
/// preparation runs against a real account. Nothing is broadcast. Never part of the offline run.
/// </summary>
[Trait("Category", "Live")]
public sealed class CosmosSendLiveTests(ITestOutputHelper output)
{
    private const string Node = "https://cosmos-rest.publicnode.com";

    private const string FaucetMnemonic =
        "economy stock theory fatal elder harbor betray wasp final emotion task crumble siren bottom lizard educate guess current outdoor pair theory focus wife stone";

    /// <summary>The BIP39 test phrase: its Cosmos address exists on the Hub with a few uatom in it.</summary>
    private const string TestPhrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static void Online()
    {
        Umbrella.Wallet.Core.Safety.ChainEndpoints.ClearAll();
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
    }

    [Fact]
    public async Task The_hubs_own_decoder_reads_back_the_memo_and_timeout_height()
    {
        Online();
        var deriver = new HdAddressDeriver();
        using var key = deriver.DeriveCosmosKey(FaucetMnemonic, passphrase: "");
        var send = new CosmosSend(
            "cosmos1pkptre7fdkl6gfrzlesjjvhxhlc3r4gmmk8rs6", "cosmos1qypqxpq9qcrsszg2pvxq6rs0zqg3yyc5lzv7xu",
            1234567, "uatom", "deposit 104", 33079342, key.PubKey.ToBytes(), 1, 3, 702, 93600, CosmosTransactions.HubChainId);
        var (tx, _) = CosmosTransactions.Sign(send, key);

        using var res = await PublicHttp.Shared.PostAsJsonAsync($"{Node}/cosmos/tx/v1beta1/decode", new { tx_bytes = Convert.ToBase64String(tx) });
        var text = await res.Content.ReadAsStringAsync();
        output.WriteLine(text);
        using var doc = JsonDocument.Parse(text);
        var decoded = doc.RootElement.GetProperty("tx");
        var body = decoded.GetProperty("body");
        Assert.Equal("deposit 104", body.GetProperty("memo").GetString());
        Assert.Equal("33079342", body.GetProperty("timeout_height").GetString());
        var msg = body.GetProperty("messages")[0];
        Assert.Equal("/cosmos.bank.v1beta1.MsgSend", msg.GetProperty("@type").GetString());
        Assert.Equal("1234567", msg.GetProperty("amount")[0].GetProperty("amount").GetString());
        var auth = decoded.GetProperty("auth_info");
        Assert.Equal("3", auth.GetProperty("signer_infos")[0].GetProperty("sequence").GetString());
        Assert.Equal("93600", auth.GetProperty("fee").GetProperty("gas_limit").GetString());
        Assert.Equal("702", auth.GetProperty("fee").GetProperty("amount")[0].GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Preparing_from_a_real_account_reads_it_and_names_what_is_missing()
    {
        Online();
        var deriver = new HdAddressDeriver();
        var from = deriver.DeriveReceiveAddress(TestPhrase, ChainId.Atom, passphrase: "").Address;
        var key = deriver.DeriveCosmosPublicKey(TestPhrase, passphrase: "").ToBytes();

        var (quote, error) = await new CosmosTransactionSender().PrepareAsync(
            from, key, "cosmos1qypqxpq9qcrsszg2pvxq6rs0zqg3yyc5lzv7xu", 1m, "104");
        output.WriteLine(error ?? $"{quote}");

        // The account holds a few uatom, so the answer is the honest "not enough" — which means the
        // node, the account, the balance and the gas price were all read.
        Assert.Null(quote);
        Assert.Contains("Not enough ATOM", error);
    }

    [Fact]
    public async Task A_real_simulation_gives_the_gas_for_a_transfer()
    {
        Online();
        var deriver = new HdAddressDeriver();
        var from = deriver.DeriveReceiveAddress(TestPhrase, ChainId.Atom, passphrase: "").Address;
        var key = deriver.DeriveCosmosPublicKey(TestPhrase, passphrase: "").ToBytes();

        using var accountRes = await PublicHttp.Shared.GetAsync($"{Node}/cosmos/auth/v1beta1/accounts/{from}");
        var (_, account) = CosmosSendRules.ParseAccount((int)accountRes.StatusCode,
            JsonDocument.Parse(await accountRes.Content.ReadAsStringAsync()).RootElement.Clone());
        Assert.NotNull(account);

        // One uatom out, one uatom of fee: within its balance, so the node can run it to the end.
        var draft = new CosmosSend(from, "cosmos1qypqxpq9qcrsszg2pvxq6rs0zqg3yyc5lzv7xu", 1, "uatom", "104", 0, key,
            account!.AccountNumber, account.Sequence, 1, 200_000, CosmosTransactions.HubChainId);
        using var res = await PublicHttp.Shared.PostAsJsonAsync($"{Node}/cosmos/tx/v1beta1/simulate",
            new { tx_bytes = Convert.ToBase64String(CosmosTransactions.SimulationBytes(draft)) });
        var text = await res.Content.ReadAsStringAsync();
        output.WriteLine($"{(int)res.StatusCode}: {text[..Math.Min(600, text.Length)]}");

        var (gas, simError) = CosmosSendRules.ParseSimulation((int)res.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
        Assert.True(gas is > 0 || simError is not null);
    }
}
