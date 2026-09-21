using System.Security.Cryptography;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit.Abstractions;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Live, read-only: runs the real XRP send preparation against the real XRP Ledger, from and to public
/// accounts, and never signs. It proves the answers rippled and Clio actually give today read the way
/// the offline tests assume — the sequence, the reserve, the fee and the destination's flags. Never
/// part of the offline run.
/// </summary>
[Trait("Category", "Live")]
public sealed class XrpSendLiveTests(ITestOutputHelper output)
{
    /// <summary>ACCOUNT_ZERO: an account nobody holds the key to, funded on the live ledger.</summary>
    private const string AccountZero = "rrrrrrrrrrrrrrrrrrrrBZbvji";

    /// <summary>The genesis account: it requires a destination tag and asks not to be sent XRP.</summary>
    private const string Genesis = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";

    private static void Online()
    {
        Umbrella.Wallet.Core.Safety.ChainEndpoints.ClearAll();   // the offline suite points every chain at a closed port
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
    }

    [Fact]
    public async Task A_destination_that_requires_a_tag_is_stopped_without_one()
    {
        Online();
        var (quote, error) = await new XrpTransactionSender().PrepareAsync(AccountZero, Genesis, 1m, null);
        output.WriteLine(error);
        Assert.Null(quote);
        Assert.Contains("destination tag", error);
    }

    [Fact]
    public async Task A_destination_that_refuses_XRP_is_stopped_even_with_a_tag()
    {
        Online();
        var (quote, error) = await new XrpTransactionSender().PrepareAsync(AccountZero, Genesis, 1m, "1");
        output.WriteLine(error);
        Assert.Null(quote);
        Assert.Contains("asked not to be sent XRP", error);
    }

    [Fact]
    public async Task A_new_destination_needs_the_reserve_and_then_prepares()
    {
        Online();
        var fresh = XrpAddress.Encode(RandomNumberGenerator.GetBytes(20));

        var (small, smallError) = await new XrpTransactionSender().PrepareAsync(AccountZero, fresh, 0.5m, null);
        output.WriteLine(smallError);
        Assert.Null(small);
        Assert.Contains("at least 1 XRP", smallError);

        var (quote, error) = await new XrpTransactionSender().PrepareAsync(AccountZero, fresh, 2m, "7");
        output.WriteLine(error ?? $"{quote}");
        Assert.NotNull(quote);
        Assert.True(quote!.CreatesAccount);
        Assert.Equal(1_000_000, quote.ReserveBaseDrops);
        Assert.InRange(quote.FeeDrops, 10, 10_000);
        Assert.True(quote.Sequence >= 1);
        Assert.Equal(7u, quote.DestinationTag);
    }
}
