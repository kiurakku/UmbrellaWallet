using System.Net.Http.Json;
using System.Text.Json;
using Umbrella.Wallet.Core.Polkadot;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit.Abstractions;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Live, read-only: Polkadot Asset Hub as it runs today. Its metadata must parse, and must still
/// describe the Balances call and the transaction extensions this wallet builds; nothing is sent.
/// Never part of the offline run.
/// </summary>
[Trait("Category", "Live")]
public sealed class PolkadotSendLiveTests(ITestOutputHelper output)
{
    private const string Node = "https://polkadot-asset-hub-rpc.polkadot.io";

    private static void Online()
    {
        Umbrella.Wallet.Core.Safety.ChainEndpoints.ClearAll();
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
    }

    private static async Task<JsonElement> Rpc(string method, params object[] parameters)
    {
        using var res = await PublicHttp.Shared.PostAsJsonAsync(Node, new { jsonrpc = "2.0", id = 1, method, @params = parameters });
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("result").Clone();
    }

    [Fact]
    public async Task The_running_runtime_describes_the_transfer_and_extensions_this_wallet_builds()
    {
        Online();
        var metadata = RuntimeMetadata.Parse(Convert.FromHexString((await Rpc("state_getMetadata")).GetString()![2..]));

        output.WriteLine($"{metadata.Types.Count} types, {metadata.Pallets.Count} pallets, extrinsic v{metadata.ExtrinsicVersion}");
        output.WriteLine(string.Join(", ", metadata.Extensions.Select(e => e.Identifier)));
        foreach (var e in metadata.Extensions)
        {
            var t = metadata.Types[e.Type];
            output.WriteLine($"  {e.Identifier}: extra {string.Join("::", t.Path)} {t.Kind} fields={t.Fields.Count} empty={metadata.IsEmpty(e.Type)}; additional empty={metadata.IsEmpty(e.AdditionalSigned)}");
        }

        var call = metadata.Call("Balances", "transfer_keep_alive");
        Assert.NotNull(call);
        output.WriteLine($"Balances.transfer_keep_alive = {call.Value.Pallet}/{call.Value.Call}; fields {string.Join(", ", call.Value.Fields.Select(f => f.Name))}");
        Assert.Equal(["dest", "value"], call.Value.Fields.Select(f => f.Name));

        var ed = metadata.PalletNamed("Balances")!.Constants["ExistentialDeposit"];
        output.WriteLine($"ExistentialDeposit = {new System.Numerics.BigInteger(ed, isUnsigned: true)} planck");
        Assert.Null(PolkadotTransactions.CheckExtensions(metadata));
    }
    [Fact]
    public async Task The_node_accepts_the_signature_and_encoding_and_stops_only_at_payment()
    {
        // A fresh key with nothing in it: the node's validation runs the decoding, the signature, the era
        // and the nonce, and only then the fee. "Cannot pay" (Invalid::Payment) is the answer that proves
        // everything before it was right; a wrong byte would be BadProof or a decode failure instead.
        Online();
        var metadata = RuntimeMetadata.Parse(Convert.FromHexString((await Rpc("state_getMetadata")).GetString()![2..]));
        var version = await Rpc("state_getRuntimeVersion");
        var genesis = Convert.FromHexString((await Rpc("chain_getBlockHash", 0)).GetString()![2..]);
        var head = (await Rpc("chain_getFinalizedHead")).GetString()!;
        var header = await Rpc("chain_getHeader", head);
        var number = Convert.ToUInt32(header.GetProperty("number").GetString()![2..], 16);

        using var key = Sr25519.FromMiniSecret(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var to = Convert.FromHexString("d43593c715fdd31c61141abd04a99fd6822c8558854ccde39a5684e7a56da27d");
        var transfer = new DotTransfer(key.PublicKey, to, 1_000_000_000, 0,
            version.GetProperty("specVersion").GetUInt32(), version.GetProperty("transactionVersion").GetUInt32(),
            genesis, number, Convert.FromHexString(head[2..]));

        var (extrinsic, hash) = PolkadotTransactions.Sign(metadata, transfer, key);
        output.WriteLine($"extrinsic {extrinsic.Length} bytes, hash 0x{Convert.ToHexString(hash).ToLowerInvariant()}");

        byte[] call = [0x02, .. extrinsic, .. Convert.FromHexString(head[2..])];   // source External, tx, at block
        var validity = (await Rpc("state_call", "TaggedTransactionQueue_validate_transaction", "0x" + Convert.ToHexString(call))).GetString()!;
        output.WriteLine($"validate_transaction: {validity}");

        Assert.Equal("0x010001", validity);   // Err(Invalid(Payment))
    }
    [Fact]
    public async Task A_real_blocks_events_are_walked_to_the_first_extrinsics_success()
    {
        // Every block's first extrinsic is the timestamp inherent, which always succeeds. Finding its
        // ExtrinsicSuccess means every event before it was walked with the runtime's own types.
        Online();
        var metadata = RuntimeMetadata.Parse(Convert.FromHexString((await Rpc("state_getMetadata")).GetString()![2..]));
        var head = (await Rpc("chain_getFinalizedHead")).GetString()!;
        var events = (await Rpc("state_getStorage", "0x26aa394eea5630e07c48ae0c9558cef780d41e5e16056765bc8461851072c9d7", head)).GetString()!;
        output.WriteLine($"events: {events.Length / 2} bytes");
        Assert.True(PolkadotSendRules.ExtrinsicSucceeded(metadata, events, 0));
    }

    [Fact]
    public async Task Preparing_reads_the_account_and_names_what_is_missing()
    {
        Online();
        using var fresh = Sr25519.FromMiniSecret(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var (quote, error) = await new PolkadotTransactionSender().PrepareAsync(
            Ss58.Encode(fresh.PublicKey), "15oF4uVJwmo4TdGW7VfQxNLavjCXviqxT9S1MgbjMNHr6Sp5", 0.1m);
        output.WriteLine(error ?? $"{quote}");
        Assert.Null(quote);
        Assert.Contains("holds no DOT on Asset Hub", error);
    }
}
