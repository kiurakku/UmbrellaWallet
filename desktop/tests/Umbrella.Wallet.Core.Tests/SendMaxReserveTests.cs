using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The Max button has to leave enough behind to pay the network fee.
///
/// A coin with no reserve is not a small omission. Max hands the user the WHOLE balance, the planner
/// then cannot fit a fee inside it, and the send fails — so on that coin the button simply does not
/// work, and the reason is invisible. Bitcoin Cash, Dogecoin and every Ethereum L2 were in that state:
/// added to the Send picker, never added to the reserve table.
///
/// So the table is tied to the capability set rather than maintained by hand. Add a coin to
/// SendableSymbols without a reserve and this fails, naming it.
/// </summary>
public sealed class SendMaxReserveTests
{
    [Fact]
    public void Every_sendable_coin_reserves_something_for_its_fee()
    {
        var missing = MainViewModel.SendableSymbols
            .Where(s => !MainViewModel.FeePaidInAnotherCoin.Contains(s))
            .Where(s => MainViewModel.SendMaxReserveFor(s) <= 0m)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void A_token_whose_fee_is_paid_in_another_coin_reserves_nothing()
    {
        // The deliberate exception, stated rather than left as a gap: USDT on Tron pays its fee in
        // TRX, so holding back USDT would just strand part of the balance for no reason.
        foreach (var symbol in MainViewModel.FeePaidInAnotherCoin)
        {
            Assert.Equal(0m, MainViewModel.SendMaxReserveFor(symbol));
        }
    }

    [Fact]
    public void Every_exempt_symbol_is_actually_sendable()
    {
        // Keeps the exemption list honest: exempting something the wallet cannot send would quietly
        // shrink what the first test checks.
        foreach (var symbol in MainViewModel.FeePaidInAnotherCoin)
        {
            Assert.Contains(symbol, MainViewModel.SendableSymbols);
        }
    }

    [Theory]
    [InlineData("BCH")]
    [InlineData("DOGE")]
    [InlineData("ARB")]
    [InlineData("BASE")]
    [InlineData("OP")]
    [InlineData("LINEA")]
    public void The_coins_that_were_missing_are_named_here_so_they_stay_fixed(string symbol)
    {
        Assert.True(MainViewModel.SendMaxReserveFor(symbol) > 0m, symbol);
    }

    [Fact]
    public void No_reserve_is_large_enough_to_swallow_a_realistic_balance()
    {
        // The other failure mode. Over-reserving is not "safe" — it silently refuses to send money the
        // user does have. Each reserve should be a fee, not a holding.
        var ceilings = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["BTC"] = 0.001m, ["LTC"] = 0.01m, ["BCH"] = 0.001m, ["DOGE"] = 10m,
            ["ETH"] = 0.01m, ["BNB"] = 0.01m, ["MATIC"] = 1m, ["AVAX"] = 0.1m,
            ["FTM"] = 1m, ["CRO"] = 1m, ["ARB"] = 0.01m, ["BASE"] = 0.01m,
            ["OP"] = 0.01m, ["LINEA"] = 0.01m, ["SOL"] = 0.01m, ["TON"] = 0.1m,
            ["TRX"] = 10m, ["XMR"] = 0.01m, ["ADA"] = 5m,
        };

        var excessive = MainViewModel.SendableSymbols
            .Where(s => ceilings.ContainsKey(s))
            .Where(s => MainViewModel.SendMaxReserveFor(s) > ceilings[s])
            .ToList();

        Assert.Empty(excessive);
    }
}
