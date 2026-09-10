using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the Holdings ordering: value descending, 24h change descending, name A→Z, and an unknown /
/// "Default" sort leaves the catalog order untouched (stable).
/// </summary>
public sealed class HoldingsSorterTests
{
    private static HoldingRowViewModel Row(string symbol, double value, double change) =>
        new(symbol, symbol, "chain", Price: 1, Amount: value, Value: value, Change24h: change,
            Address: "addr", SupportStatus: "Ready");

    private static readonly HoldingRowViewModel[] Sample =
    [
        Row("BTC", value: 100, change: -2),
        Row("ETH", value: 300, change: 5),
        Row("ADA", value: 50, change: 1),
    ];

    [Fact]
    public void By_value_is_biggest_first()
    {
        var order = HoldingsSorter.Order(Sample, HoldingsSorter.ByValue).Select(r => r.Symbol).ToArray();
        Assert.Equal(new[] { "ETH", "BTC", "ADA" }, order);
    }

    [Fact]
    public void By_change_is_best_mover_first()
    {
        var order = HoldingsSorter.Order(Sample, HoldingsSorter.ByChange).Select(r => r.Symbol).ToArray();
        Assert.Equal(new[] { "ETH", "ADA", "BTC" }, order);
    }

    [Fact]
    public void By_name_is_alphabetical()
    {
        var order = HoldingsSorter.Order(Sample, HoldingsSorter.ByName).Select(r => r.Symbol).ToArray();
        Assert.Equal(new[] { "ADA", "BTC", "ETH" }, order);
    }

    [Fact]
    public void Default_and_unknown_keep_the_catalog_order()
    {
        var input = Sample.Select(r => r.Symbol).ToArray();
        Assert.Equal(input, HoldingsSorter.Order(Sample, HoldingsSorter.Default).Select(r => r.Symbol).ToArray());
        Assert.Equal(input, HoldingsSorter.Order(Sample, "whatever").Select(r => r.Symbol).ToArray());
        Assert.Equal(input, HoldingsSorter.Order(Sample, null).Select(r => r.Symbol).ToArray());
    }
}
