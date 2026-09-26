using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The Nano balance against the real nodes. <c>Category=Live</c>: run by hand, never in CI.
/// </summary>
[Trait("Category", "Live")]
public sealed class NanoLiveTests
{
    [Fact]
    public async Task Every_listed_node_answers_for_a_real_account()
    {
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);

        // The documentation's vector account: never funded, so every node must answer a real zero —
        // and "answer" is the point: an error would read as unknown, never as 0.
        const string account = "nano_1pu7p5n3ghq1i1p4rhmek41f5add1uh34xpb94nkbxe8g4a6x1p69emk8y1d";
        var answered = 0;
        foreach (var node in ChainEndpoints.Known["XNO"])
        {
            ChainEndpoints.ClearAll();
            ChainEndpoints.SetOverride("XNO", node.BaseUrl);
            var balance = await new PublicChainBalanceClient().GetBalanceAsync(ChainId.Nano, account);
            if (balance is not null)
            {
                Assert.Equal(0m, balance.NativeAmount);
                answered++;
            }
        }

        ChainEndpoints.ClearAll();
        Assert.True(answered >= 2, $"only {answered} of the Nano nodes answered");
    }
}
