using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap P0.6 / MANIFESTO §4: an unanswered balance lookup must never render as a balance.
///
/// The bug this pins is quiet and specific. <c>PublicChainBalanceClient.GetBalanceAsync</c> returns
/// null when nobody answered — a rate-limited explorer, a dead node, the kill-switch refusing a
/// clearnet request — and the refresh loop used to fold that null into "keep the previous amount",
/// which on a freshly unlocked wallet is <c>0</c>. The row then said <c>0.000000 BTC · $0.00</c> with
/// exactly the same confidence as a genuinely empty address. That is the wallet telling somebody
/// their money is gone.
/// </summary>
public sealed class BalanceReadoutTests
{
    [Fact]
    public void A_failed_read_on_a_row_that_never_had_one_stays_unknown()
    {
        var (amount, state) = BalanceReadout.Apply(read: null, previousAmount: 0, BalanceRead.Unknown);

        Assert.Equal(0, amount);
        Assert.Equal(BalanceRead.Unknown, state);
        Assert.Equal("— BTC", BalanceReadout.AmountText(amount, state, "BTC"));
        Assert.False(BalanceReadout.CountsTowardsTotal(state));
    }

    [Fact]
    public void A_real_zero_from_the_chain_is_a_live_reading_not_an_error()
    {
        // An empty address is an answer. Hiding it behind "unavailable" would be the opposite lie.
        var (amount, state) = BalanceReadout.Apply(read: 0m, previousAmount: 0, BalanceRead.Unknown);

        Assert.Equal(0, amount);
        Assert.Equal(BalanceRead.Live, state);
        Assert.Equal("0.000000 BTC", BalanceReadout.AmountText(amount, state, "BTC"));
        Assert.True(BalanceReadout.CountsTowardsTotal(state));
    }

    [Fact]
    public void A_failed_read_after_a_good_one_keeps_the_number_but_stops_calling_it_current()
    {
        var (amount, state) = BalanceReadout.Apply(read: null, previousAmount: 0.75, BalanceRead.Live);

        Assert.Equal(0.75, amount);                    // not blanked — a minute-old figure still helps
        Assert.Equal(BalanceRead.Cached, state);       // but it is no longer presented as current
        Assert.True(BalanceReadout.CountsTowardsTotal(state));
    }

    [Fact]
    public void A_successful_read_clears_a_stale_marker()
    {
        var (amount, state) = BalanceReadout.Apply(read: 1.25m, previousAmount: 0.75, BalanceRead.Cached);

        Assert.Equal(1.25, amount);
        Assert.Equal(BalanceRead.Live, state);
    }

    [Fact]
    public void A_holdings_row_with_no_reading_shows_a_dash_and_no_fiat_value()
    {
        var unknown = new HoldingRowViewModel(
            "BTC", "Bitcoin", "Bitcoin", Price: 60_000, Amount: 0, Value: 0, Change24h: 0,
            Address: "bc1q…", SupportStatus: "Ready", Balance: BalanceRead.Unknown);

        Assert.Equal("— BTC", unknown.AmountLabel);
        Assert.Equal("—", unknown.ValueLabel);
        Assert.True(unknown.IsBalanceUnknown);
        Assert.NotEqual(string.Empty, unknown.BalanceNote);
    }

    [Fact]
    public void A_holdings_row_that_was_read_shows_the_number_with_no_warning()
    {
        var live = new HoldingRowViewModel(
            "BTC", "Bitcoin", "Bitcoin", Price: 60_000, Amount: 0, Value: 0, Change24h: 0,
            Address: "bc1q…", SupportStatus: "Ready", Balance: BalanceRead.Live);

        Assert.Equal("0.000000 BTC", live.AmountLabel);
        Assert.False(live.IsBalanceUnknown);
        Assert.Equal(string.Empty, live.BalanceNote);
    }

    /// <summary>
    /// A freshly derived account has read nothing yet. If its default were "Live" the first paint
    /// after unlock — before any explorer has answered — would be a confident row of zeros, which is
    /// the very state P0.6 is about.
    /// </summary>
    [Fact]
    public void A_newly_derived_account_starts_out_unknown()
    {
        var account = new WalletAccountViewModel(
            "BTC", "Bitcoin", "Ready", "bc1q…", "m/84'/0'/0'/0/0", 0, 0, "Bitcoin", 0);

        Assert.Equal(BalanceRead.Unknown, account.Balance);
        Assert.True(account.IsBalanceUnknown);
        Assert.Equal("— BTC", account.AmountLabel);
    }
}
