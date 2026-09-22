using System.Net.Http.Json;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit.Abstractions;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Live, read-only: the real Solana validator runs what this wallet compiles. <c>simulateTransaction</c>
/// with signature checking off executes a transaction without landing it, so a malformed message — a
/// wrong header count, a wrong account index — is refused there exactly as it would be for real. Nothing
/// is sent. Never part of the offline run.
/// </summary>
[Trait("Category", "Live")]
public sealed class SolanaSendLiveTests(ITestOutputHelper output)
{
    private const string Node = "https://api.mainnet-beta.solana.com";

    /// <summary>The BIP39 test phrase; its Solana address holds a little SOL on mainnet.</summary>
    private const string TestPhrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static void Online()
    {
        Umbrella.Wallet.Core.Safety.ChainEndpoints.ClearAll();
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
    }

    private async Task<JsonElement> Rpc(string method, params object[] parameters)
    {
        using var res = await PublicHttp.Shared.PostAsJsonAsync(Node, SolanaRpc.Request(method, parameters));
        var text = await res.Content.ReadAsStringAsync();
        output.WriteLine($"{method}: {text[..Math.Min(700, text.Length)]}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Simulates a compiled message as the validator would run it; returns the result's value.</summary>
    private async Task<JsonElement> Simulate(byte[] message)
    {
        var tx = SolanaMessage.Transaction(new byte[64], message);   // an unsigned slot: sigVerify is off
        var root = await Rpc("simulateTransaction", Convert.ToBase64String(tx),
            new { encoding = "base64", sigVerify = false, replaceRecentBlockhash = true, commitment = "confirmed" });
        var (result, error) = SolanaRpc.Unwrap(root);
        Assert.Null(error);                      // a malformed transaction is an RPC error, not a result
        return result!.Value.GetProperty("value");
    }

    /// <summary>
    /// A fee payer that certainly can pay: a well-known, heavily funded exchange hot wallet, checked
    /// live to be an ordinary System-owned account. Simulation never needs its key. (The test phrase's
    /// own address will not do: anyone holding that public phrase can — and someone did — reassign it
    /// to another program, after which it cannot pay fees.)
    /// </summary>
    private async Task<byte[]> FundedFeePayerAsync()
    {
        foreach (var candidate in new[] { "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM", "H8sMJSCQxfKiFTCfDR3DUMLPwcRbM61LGFJ8N4dK3WjS" })
        {
            var (info, _) = SolanaRpc.Unwrap(await Rpc("getAccountInfo", candidate, new { encoding = "base64" }));
            if (info is not { } i || i.GetProperty("value").ValueKind != JsonValueKind.Object) continue;
            var value = i.GetProperty("value");
            if (value.GetProperty("owner").GetString() != "11111111111111111111111111111111") continue;
            if (value.GetProperty("lamports").GetUInt64() < 50_000_000) continue;
            Assert.True(SolanaKeys.TryDecode(candidate, out var key));
            return key;
        }

        throw new InvalidOperationException("None of the known funded accounts is usable right now.");
    }

    [Fact]
    public async Task The_validator_runs_a_compiled_transfer()
    {
        Online();
        var payer = await FundedFeePayerAsync();

        // To itself: an account that certainly exists, so rent cannot be the reason for any failure.
        var message = SolanaTransactionSender.BuildTransferMessage(payer, payer, 1_000, new byte[32]);
        var value = await Simulate(message);

        Assert.Equal(JsonValueKind.Null, value.GetProperty("err").ValueKind);
        Assert.Contains(value.GetProperty("logs").EnumerateArray(),
            l => l.GetString() == "Program 11111111111111111111111111111111 success");
    }

    [Fact]
    public async Task The_real_send_preparation_reads_balance_rent_and_destination()
    {
        Online();
        var from = new HdAddressDeriver().DeriveReceiveAddress(TestPhrase, ChainId.Sol, passphrase: "").Address;

        // A fresh destination and less than the rent minimum: refused before anything is signed.
        var fresh = SolanaKeys.Encode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var (quote, error) = await new SolanaTransactionSender().PrepareAsync(from, fresh, 0.0001m);
        output.WriteLine(error ?? $"{quote}");
        Assert.Null(quote);
        Assert.True(error!.Contains("minimum a Solana account must hold") || error.Contains("Not enough SOL"), error);
    }
}
