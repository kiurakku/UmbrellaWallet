using System.Globalization;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// SPL token send (roadmap N.3).
///
/// The associated-account vectors are mainnet itself: for each (wallet, mint) pair below, the account
/// is the one <c>getTokenAccountsByOwner</c> returned holding that wallet's tokens (1,095 USDC and
/// 338 USDT when these were taken), found among the wallet's other token accounts only by derivation.
/// The instruction layouts are the SPL programs' own (TransferChecked = 12, CreateIdempotent = 1), and
/// a mainnet validator runs them in <c>SplTokenLiveTests</c>.
/// </summary>
public sealed class SplTokenTests
{
    private static byte[] K(string s)
    {
        Assert.True(SolanaKeys.TryDecode(s, out var k));
        return k;
    }

    [Theory]
    [InlineData("9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM", "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v", "FGETo8T8wMcN2wCjav8VK6eh3dLk63evNDPxzLSJra8B")]
    [InlineData("H8sMJSCQxfKiFTCfDR3DUMLPwcRbM61LGFJ8N4dK3WjS", "Es9vMFrzaCERmJfrF4H2FYD4KCoNkY11McCe8BenwNYB", "5WKb9eZnbevTR5Qc12s3E5mZ6WazbUiTGMrHsUwfiySW")]
    public void The_associated_account_is_the_one_mainnet_holds_the_tokens_in(string owner, string mint, string account) =>
        Assert.Equal(account, SolanaKeys.Encode(SplToken.AssociatedTokenAddress(K(owner), K(mint))));

    [Fact]
    public void TransferChecked_names_the_mint_and_its_decimals()
    {
        var source = Enumerable.Repeat((byte)1, 32).ToArray();
        var mint = Enumerable.Repeat((byte)2, 32).ToArray();
        var dest = Enumerable.Repeat((byte)3, 32).ToArray();
        var owner = Enumerable.Repeat((byte)4, 32).ToArray();

        var ix = SplToken.TransferChecked(source, mint, dest, owner, 1_500_000, 6);

        Assert.Equal(SplToken.TokenProgram, ix.ProgramId);
        Assert.Equal(new byte[] { 12, 0x60, 0xE3, 0x16, 0, 0, 0, 0, 0, 6 }, ix.Data);   // 1,500,000 LE, then 6
        Assert.Equal([source, mint, dest, owner], ix.Accounts.Select(a => a.Key));
        Assert.Equal([false, false, false, true], ix.Accounts.Select(a => a.IsSigner));
        Assert.Equal([true, false, true, false], ix.Accounts.Select(a => a.IsWritable));
    }

    [Fact]
    public void Creating_the_recipients_account_is_idempotent_and_paid_by_the_sender()
    {
        var payer = K("9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM");
        var owner = K("H8sMJSCQxfKiFTCfDR3DUMLPwcRbM61LGFJ8N4dK3WjS");
        var mint = K("Es9vMFrzaCERmJfrF4H2FYD4KCoNkY11McCe8BenwNYB");

        var ix = SplToken.CreateAssociatedAccountIdempotent(payer, owner, mint);

        Assert.Equal(SplToken.AssociatedTokenProgram, ix.ProgramId);
        Assert.Equal(new byte[] { 1 }, ix.Data);
        Assert.Equal([payer, K("5WKb9eZnbevTR5Qc12s3E5mZ6WazbUiTGMrHsUwfiySW"), owner, mint, SplToken.SystemProgram, SplToken.TokenProgram],
            ix.Accounts.Select(a => a.Key));
        Assert.True(ix.Accounts[0].IsSigner && ix.Accounts[0].IsWritable);
        Assert.True(ix.Accounts[1].IsWritable && !ix.Accounts[1].IsSigner);
        Assert.True(ix.Accounts.Skip(2).All(a => !a.IsSigner && !a.IsWritable));
    }

    [Theory]
    [InlineData("1", 6, 1_000_000UL)]
    [InlineData("0.000001", 6, 1UL)]
    [InlineData("1234.5", 9, 1_234_500_000_000UL)]
    [InlineData("7", 0, 7UL)]
    public void Amounts_scale_by_the_mints_decimals(string amount, int decimals, ulong units)
    {
        Assert.True(SplToken.TryToUnits(decimal.Parse(amount, CultureInfo.InvariantCulture), decimals, out var u));
        Assert.Equal(units, u);
    }

    [Theory]
    [InlineData("0", 6)]
    [InlineData("0.0000001", 6)]   // finer than the token allows
    [InlineData("0.5", 0)]
    public void Amounts_the_mint_cannot_hold_are_refused(string amount, int decimals) =>
        Assert.False(SplToken.TryToUnits(decimal.Parse(amount, CultureInfo.InvariantCulture), decimals, out _));

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void Account_lookups_say_what_an_address_is()
    {
        Assert.Equal(SplToken.AccountKind.Missing, SplToken.ParseAccount(Json("""{"context":{"slot":1},"value":null}""")).Kind);

        var wallet = SplToken.ParseAccount(Json("""
            {"context":{"slot":1},"value":{"data":["","base64"],"executable":false,"lamports":2,"owner":"11111111111111111111111111111111","space":0}}
            """));
        Assert.Equal(SplToken.AccountKind.Wallet, wallet.Kind);

        var mint = SplToken.ParseAccount(Json("""
            {"context":{"slot":1},"value":{"data":{"parsed":{"info":{"decimals":6,"isInitialized":true,"supply":"1"},"type":"mint"},
             "program":"spl-token","space":82},"owner":"TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA","lamports":1}}
            """));
        Assert.Equal((SplToken.AccountKind.Mint, SolanaTokens.TokenProgram, 6), (mint.Kind, mint.Program, mint.Decimals));

        var account = SplToken.ParseAccount(Json("""
            {"context":{"slot":1},"value":{"data":{"parsed":{"info":{"isNative":false,"mint":"EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v",
             "owner":"9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM","state":"initialized",
             "tokenAmount":{"amount":"1095074585","decimals":6,"uiAmount":1095.074585,"uiAmountString":"1095.074585"}},"type":"account"},
             "program":"spl-token","space":165},"owner":"TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA","lamports":2039280}}
            """));
        Assert.Equal(SplToken.AccountKind.TokenAccount, account.Kind);
        Assert.Equal("EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v", account.Mint);
        Assert.Equal("9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM", account.Owner);
        Assert.Equal(1_095_074_585UL, account.Units);

        Assert.Equal(SplToken.AccountKind.Other, SplToken.ParseAccount(Json("""
            {"context":{"slot":1},"value":{"data":["","base64"],"owner":"9G4pPipvCwQkf2X3CtFFJgK88vdN43EoKn7Kf8wxjKa","lamports":1}}
            """)).Kind);
        Assert.Equal(SplToken.AccountKind.Unreadable, SplToken.ParseAccount(Json("""{"context":{"slot":1}}""")).Kind);
    }
}
