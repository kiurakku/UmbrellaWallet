using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The three history readers against the real default servers, for accounts known to have activity.
/// <c>Category=Live</c>: run by hand, never in CI — the suite must not fail because a public node is busy.
/// </summary>
[Trait("Category", "Live")]
public sealed class AccountHistoryLiveTests
{
    private static void Online()
    {
        ChainEndpoints.ClearAll();
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
    }

    [Fact]
    public async Task Xrp_history_reads_real_payments()
    {
        Online();
        var rows = await new AccountHistoryClient().GetXrpAsync("rEb8TK3gBgk5auZkwc6sHnwrGVJH8DuaLh", limit: 10);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("XRP", r.Asset));
        Assert.All(rows, r => Assert.True(r.UnixMs > new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds()));
    }

    [Fact]
    public async Task Stellar_history_reads_real_payments()
    {
        Online();
        var rows = await new AccountHistoryClient().GetStellarAsync("GAHK7EEG2WWHVKDNT4CEQFZGKF2LGDSW2IVM4S5DP42RBW3K6BTODB4A", limit: 10);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("XLM", r.Asset));
    }
}
