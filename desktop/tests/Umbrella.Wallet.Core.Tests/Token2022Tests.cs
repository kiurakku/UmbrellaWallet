using System.Text.Json;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Token-2022 (roadmap N.3): the second token program, whose mints can carry extensions that change
/// what a transfer does. The wallet sends the ones that cannot, and says why for the ones that can.
///
/// The account vector is mainnet's own: this wallet's PayPal USD really sits in that account, and the
/// derivation has to reproduce it with the Token-2022 program in the seeds. The policy is checked
/// against PYUSD's real extension list and against each thing that must stop a send.
/// </summary>
public sealed class Token2022Tests
{
    private static byte[] K(string s)
    {
        Assert.True(SolanaKeys.TryDecode(s, out var k));
        return k;
    }

    private const string Pyusd = "2b1kV6DkPAnxd5ixfnxCpjxmKwqjjaYmCZfHsFu24GXo";
    private static readonly byte[] Token2022 = K(SolanaTokens.Token2022Program);

    [Fact]
    public void The_associated_account_is_derived_with_the_tokens_own_program()
    {
        // The same wallet and mint under the original program would give a different address.
        Assert.Equal("FELzjYS4MXATQJNaKtKGJYizUyHkS9a7xmiJUfzaUj53",
            SolanaKeys.Encode(SplToken.AssociatedTokenAddress(K("H8sMJSCQxfKiFTCfDR3DUMLPwcRbM61LGFJ8N4dK3WjS"), K(Pyusd), Token2022)));
        Assert.NotEqual("FELzjYS4MXATQJNaKtKGJYizUyHkS9a7xmiJUfzaUj53",
            SolanaKeys.Encode(SplToken.AssociatedTokenAddress(K("H8sMJSCQxfKiFTCfDR3DUMLPwcRbM61LGFJ8N4dK3WjS"), K(Pyusd))));
    }

    [Fact]
    public void The_transfer_and_create_run_under_the_tokens_own_program()
    {
        var owner = K("H8sMJSCQxfKiFTCfDR3DUMLPwcRbM61LGFJ8N4dK3WjS");
        var mint = K(Pyusd);

        var transfer = SplToken.TransferChecked(owner, mint, owner, owner, 1, 6, Token2022);
        Assert.Equal(Token2022, transfer.ProgramId);

        var create = SplToken.CreateAssociatedAccountIdempotent(owner, owner, mint, Token2022);
        Assert.Equal(SplToken.AssociatedTokenProgram, create.ProgramId);
        Assert.Equal(Token2022, create.Accounts[^1].Key);                                   // the token program it creates under
        Assert.Equal(SplToken.AssociatedTokenAddress(owner, mint, Token2022), create.Accounts[1].Key);
    }

    [Fact]
    public void A_token_2022_account_is_given_room_for_its_extensions_when_renting()
    {
        Assert.Equal(165, SplToken.AccountSizeFor(SolanaTokens.TokenProgram));
        Assert.True(SplToken.AccountSizeFor(SolanaTokens.Token2022Program) > 165);
    }

    private static JsonElement Info(string extensions) =>
        JsonDocument.Parse($$"""{"decimals":6,"extensions":{{extensions}}}""").RootElement.Clone();

    [Fact]
    public void PayPal_USDs_real_extensions_do_not_stand_in_the_way()
    {
        // As mainnet lists them: a zero transfer fee, a hook with no program, metadata, confidential
        // transfer settings a plain transfer never touches, and a permanent delegate — which is the
        // token's own nature (the issuer can move it), not something this transfer changes.
        var info = Info("""
            [{"extension":"mintCloseAuthority","state":{"closeAuthority":"2apBGMsS6ti9RyF5TwQTDswXBWskiJP2LD4cUEDqYJjk"}},
             {"extension":"permanentDelegate","state":{"delegate":"2apBGMsS6ti9RyF5TwQTDswXBWskiJP2LD4cUEDqYJjk"}},
             {"extension":"transferFeeConfig","state":{"newerTransferFee":{"epoch":605,"maximumFee":0,"transferFeeBasisPoints":0},
              "olderTransferFee":{"epoch":605,"maximumFee":0,"transferFeeBasisPoints":0}}},
             {"extension":"confidentialTransferMint","state":{"auditorElgamalPubkey":null,"autoApproveNewAccounts":false}},
             {"extension":"transferHook","state":{"authority":"2apBGMsS6ti9RyF5TwQTDswXBWskiJP2LD4cUEDqYJjk","programId":null}},
             {"extension":"metadataPointer","state":{"metadataAddress":"2b1kV6DkPAnxd5ixfnxCpjxmKwqjjaYmCZfHsFu24GXo"}},
             {"extension":"tokenMetadata","state":{"name":"PayPal USD","symbol":"PYUSD"}}]
            """);
        Assert.Null(SplToken.WhyNotSendable(SolanaTokens.Token2022Program, info));
    }

    [Theory]
    [InlineData("""[{"extension":"nonTransferable","state":{}}]""", "non-transferable")]
    [InlineData("""[{"extension":"transferFeeConfig","state":{"newerTransferFee":{"maximumFee":1000000,"transferFeeBasisPoints":50},"olderTransferFee":{"maximumFee":1000000,"transferFeeBasisPoints":50}}}]""", "transfer fee")]
    [InlineData("""[{"extension":"transferHook","state":{"programId":"Hook11111111111111111111111111111111111111"}}]""", "transfer hook")]
    [InlineData("""[{"extension":"pausableConfig","state":{"paused":true}}]""", "paused")]
    [InlineData("""[{"extension":"defaultAccountState","state":{"accountState":"frozen"}}]""", "start frozen")]
    [InlineData("""[{"extension":"interestBearingConfig","state":{"currentRate":500}}]""", "scaled by the issuer")]
    [InlineData("""[{"extension":"scaledUiAmountConfig","state":{"multiplier":"2"}}]""", "scaled by the issuer")]
    public void Anything_that_could_change_what_a_transfer_does_is_refused_with_a_reason(string extensions, string reason)
    {
        var why = SplToken.WhyNotSendable(SolanaTokens.Token2022Program, Info(extensions));
        Assert.NotNull(why);
        Assert.Contains(reason, why);
    }

    [Fact]
    public void The_original_token_program_has_no_extensions_to_judge()
    {
        Assert.Null(SplToken.WhyNotSendable(SolanaTokens.TokenProgram, Info("""[{"extension":"nonTransferable","state":{}}]""")));
        Assert.Null(SplToken.WhyNotSendable(SolanaTokens.Token2022Program, JsonDocument.Parse("""{"decimals":6}""").RootElement.Clone()));
    }

    [Fact]
    public void A_token_whose_issuer_can_freeze_or_seize_it_says_so()
    {
        var pyusd = JsonDocument.Parse("""
            {"decimals":6,"freezeAuthority":"2apBGMsS6ti9RyF5TwQTDswXBWskiJP2LD4cUEDqYJjk",
             "extensions":[{"extension":"permanentDelegate","state":{"delegate":"2apBGMsS6ti9RyF5TwQTDswXBWskiJP2LD4cUEDqYJjk"}}]}
            """).RootElement.Clone();
        var powers = SplToken.IssuerPowers(pyusd);
        Assert.Contains("freeze it in any account", powers);
        Assert.Contains("move it out of any account", powers);

        // A mint with neither says nothing, rather than inventing a reassurance.
        Assert.Null(SplToken.IssuerPowers(JsonDocument.Parse("""{"decimals":6,"freezeAuthority":null}""").RootElement.Clone()));
    }
}
