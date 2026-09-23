using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Org.BouncyCastle.Math.EC.Rfc8032;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit.Abstractions;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Live, read-only: a mainnet validator runs the SPL transfer this wallet builds — creating the
/// recipient's USDC account and moving one unit of USDC into it — from a real, funded wallet, with
/// signature checking off. Nothing lands. Never part of the offline run.
/// </summary>
[Trait("Category", "Live")]
public sealed class SplTokenLiveTests(ITestOutputHelper output)
{
    private const string Node = "https://api.mainnet-beta.solana.com";

    /// <summary>A heavily used exchange wallet whose associated USDC account holds USDC.</summary>
    private const string Holder = "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM";
    private const string Usdc = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";

    private static void Online()
    {
        Umbrella.Wallet.Core.Safety.ChainEndpoints.ClearAll();
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
    }

    /// <summary>A fresh wallet address — a real key's public half, so it is on the curve.</summary>
    private static string FreshWallet()
    {
        var pub = new byte[32];
        Ed25519.GeneratePublicKey(RandomNumberGenerator.GetBytes(32), 0, pub, 0);
        return SolanaKeys.Encode(pub);
    }

    [Fact]
    public async Task The_validator_runs_a_transfer_that_creates_the_recipients_account()
    {
        Online();
        var fresh = FreshWallet();
        var quote = new SplSendQuote(Holder, fresh, Usdc, "USDC", 0.000001m, 1, 6, CreatesAccount: true, 0, Node);
        SolanaKeys.TryDecode(Holder, out var from);
        SolanaKeys.TryDecode(fresh, out var to);
        SolanaKeys.TryDecode(Usdc, out var mint);

        var message = SolanaMessage.Compile(from, SplTokenSender.Instructions(quote, from, to, mint), new byte[32]);
        var tx = SolanaMessage.Transaction(new byte[64], message);

        using var res = await PublicHttp.Shared.PostAsJsonAsync(Node, SolanaRpc.Request("simulateTransaction",
            Convert.ToBase64String(tx), new { encoding = "base64", sigVerify = false, replaceRecentBlockhash = true, commitment = "confirmed" }));
        var text = await res.Content.ReadAsStringAsync();
        output.WriteLine(text[..Math.Min(300, text.Length)]);

        var (result, error) = SolanaRpc.Unwrap(JsonDocument.Parse(text).RootElement);
        Assert.Null(error);
        var value = result!.Value.GetProperty("value");
        Assert.Equal(JsonValueKind.Null, value.GetProperty("err").ValueKind);
        var logs = value.GetProperty("logs").EnumerateArray().Select(l => l.GetString()).ToList();
        foreach (var line in logs) output.WriteLine(line);
        Assert.Contains("Program ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL success", logs);
        // The second top-level instruction is the TransferChecked, run by the token program itself
        // (which no longer logs instruction names), and it is the last thing to succeed.
        Assert.Contains("Program TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA invoke [1]", logs);
        Assert.Equal("Program TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA success", logs[^1]);
    }

    [Fact]
    public async Task Preparing_reads_the_mint_the_accounts_and_the_rent()
    {
        Online();
        var fresh = FreshWallet();

        var (quote, error) = await new SplTokenSender().PrepareAsync(Holder, Usdc, "USDC", fresh, 0.000001m);
        output.WriteLine(error ?? $"{quote}");

        Assert.NotNull(quote);
        Assert.True(quote!.CreatesAccount);
        Assert.Equal(1UL, quote.Units);
        Assert.Equal(6, quote.Decimals);
        Assert.InRange(quote.RentLamports, 1_000_000UL, 3_000_000UL);

        // A token account is not a wallet, and a program address is refused before anything is read.
        var (_, tokenAccountError) = await new SplTokenSender().PrepareAsync(Holder, Usdc, "USDC", "5WKb9eZnbevTR5Qc12s3E5mZ6WazbUiTGMrHsUwfiySW", 1m);
        output.WriteLine(tokenAccountError);
        Assert.Contains("program, not a wallet", tokenAccountError);   // an associated account is itself off-curve
    }
    /// <summary>A wallet whose PayPal USD (Token-2022) sits in its associated account.</summary>
    private const string PyusdHolder = "H8sMJSCQxfKiFTCfDR3DUMLPwcRbM61LGFJ8N4dK3WjS";
    private const string Pyusd = "2b1kV6DkPAnxd5ixfnxCpjxmKwqjjaYmCZfHsFu24GXo";

    [Fact]
    public async Task The_validator_runs_a_token_2022_transfer_of_paypal_usd()
    {
        Online();
        var fresh = FreshWallet();
        var (quote, error) = await new SplTokenSender().PrepareAsync(PyusdHolder, Pyusd, "PYUSD", fresh, 0.000001m);
        output.WriteLine(error ?? $"{quote}");

        // PYUSD's extensions (a zero fee, a hook with no program, metadata) do not stop a plain transfer.
        Assert.NotNull(quote);
        Assert.Equal(SolanaTokens.Token2022Program, quote!.TokenProgram);
        Assert.True(quote.CreatesAccount);

        SolanaKeys.TryDecode(PyusdHolder, out var from);
        SolanaKeys.TryDecode(fresh, out var to);
        SolanaKeys.TryDecode(Pyusd, out var mint);
        var message = SolanaMessage.Compile(from, SplTokenSender.Instructions(quote, from, to, mint), new byte[32]);

        using var res = await PublicHttp.Shared.PostAsJsonAsync(Node, SolanaRpc.Request("simulateTransaction",
            Convert.ToBase64String(SolanaMessage.Transaction(new byte[64], message)),
            new { encoding = "base64", sigVerify = false, replaceRecentBlockhash = true, commitment = "confirmed" }));
        var text = await res.Content.ReadAsStringAsync();
        var (result, rpcError) = SolanaRpc.Unwrap(JsonDocument.Parse(text).RootElement);
        Assert.Null(rpcError);
        var value = result!.Value.GetProperty("value");
        var logs = value.GetProperty("logs").EnumerateArray().Select(l => l.GetString()).ToList();
        foreach (var line in logs) output.WriteLine(line);

        Assert.Equal(JsonValueKind.Null, value.GetProperty("err").ValueKind);
        Assert.Equal($"Program {SolanaTokens.Token2022Program} success", logs[^1]);
    }
}
