using System.Text.Json;
using Umbrella.Wallet.App;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The display cache that makes the portfolio total appear the instant a wallet unlocks, instead of
/// flashing $0 while the network catches up.
///
/// The interesting case is Ethereum's L2 rollups. ETH on mainnet, Arbitrum, Base, Optimism and Linea
/// are five different balances that all report the symbol "ETH" at the SAME 0x address — so anything
/// keyed on symbol + address alone sees five copies of one holding. Restoring the cache used to build
/// a dictionary from exactly that key, which throws on the second row, and it throws on the unlock
/// path, where a display cache has no business failing at all.
/// </summary>
public sealed class BalanceStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"umbrella-balance-{Guid.NewGuid():N}.json");

    private BalanceStore Store() => new(_path);

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { /* best effort */ }
    }

    [Fact]
    public void Eth_on_several_rollups_is_several_holdings_not_one()
    {
        // The same coin at the same address on four networks. If the network were not part of the
        // identity these would be indistinguishable, and four real balances would read as one.
        var entries = new[]
        {
            new BalanceStore.Entry("ETH", "0xabc", 1.0, 3000, 0, "Ethereum"),
            new BalanceStore.Entry("ETH", "0xabc", 2.0, 3000, 0, "Arbitrum"),
            new BalanceStore.Entry("ETH", "0xabc", 3.0, 3000, 0, "Base"),
            new BalanceStore.Entry("ETH", "0xabc", 4.0, 3000, 0, "Linea"),
        };

        Store().Save("main", entries);
        var loaded = Store().Load("main");

        Assert.Equal(4, loaded.Count);
        Assert.Equal(
            new[] { "Arbitrum", "Base", "Ethereum", "Linea" },
            loaded.Select(e => e.Network).OrderBy(n => n, StringComparer.Ordinal).ToArray());

        // And the keys they are matched back by are genuinely distinct.
        var keys = loaded.Select(e => e.Symbol + "|" + e.Address + "|" + e.Network).ToList();
        Assert.Equal(keys.Distinct().Count(), keys.Count);

        // The old key, for contrast: symbol + address alone collapses all four onto one string, which
        // is why building a dictionary from it threw on the second row — on the unlock path, where a
        // display cache failing takes the whole balance restore with it.
        var oldKeys = loaded.Select(e => e.Symbol + "|" + e.Address).ToList();
        Assert.Single(oldKeys.Distinct());
        Assert.Throws<ArgumentException>(() => oldKeys.ToDictionary(k => k, k => k));
    }

    [Fact]
    public void A_cache_written_before_the_network_field_existed_still_loads()
    {
        // Old files are real: somebody updates the wallet and their balances.json predates this field.
        // It has to degrade to an empty network, not to an exception on the unlock path.
        var legacy =
            """
            {"Wallets":{"main":[{"Symbol":"ETH","Address":"0xabc","Amount":1.5,"Price":3000,"Change":2}]}}
            """;
        File.WriteAllText(_path, legacy);

        var loaded = Store().Load("main");

        var entry = Assert.Single(loaded);
        Assert.Equal("ETH", entry.Symbol);
        Assert.Equal(1.5, entry.Amount);
        Assert.Equal(string.Empty, entry.Network);
    }

    [Fact]
    public void A_file_holding_two_rows_with_the_same_key_is_survivable()
    {
        // Written by an older build, hand-edited, or half-flushed. Loading must hand back what is
        // there and let the caller decide — the caller now groups rather than building a dictionary
        // that throws on the duplicate.
        var duplicated =
            """
            {"Wallets":{"main":[
              {"Symbol":"ETH","Address":"0xabc","Amount":1,"Price":3000,"Change":0,"Network":""},
              {"Symbol":"ETH","Address":"0xabc","Amount":2,"Price":3100,"Change":0,"Network":""}
            ]}}
            """;
        File.WriteAllText(_path, duplicated);

        var loaded = Store().Load("main");
        Assert.Equal(2, loaded.Count);

        // The grouping the view model applies: newest row wins, no exception.
        var byKey = loaded
            .GroupBy(e => e.Symbol + "|" + e.Address + "|" + e.Network, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(2.0, Assert.Single(byKey).Value.Amount);
    }

    [Fact]
    public void A_corrupt_file_is_no_cache_rather_than_a_crash()
    {
        File.WriteAllText(_path, "{ this is not json");
        Assert.Empty(Store().Load("main"));
    }

    [Fact]
    public void Saving_one_wallet_leaves_the_others_alone()
    {
        Store().Save("one", [new BalanceStore.Entry("BTC", "bc1q", 0.5, 60000, 0, "Bitcoin")]);
        Store().Save("two", [new BalanceStore.Entry("LTC", "ltc1q", 9, 80, 0, "Litecoin")]);

        Assert.Equal("BTC", Assert.Single(Store().Load("one")).Symbol);
        Assert.Equal("LTC", Assert.Single(Store().Load("two")).Symbol);
    }

    [Fact]
    public void The_network_survives_a_round_trip_through_json()
    {
        // Guards the serializer specifically: a defaulted record parameter is easy to add to the type
        // and still lose on the way to disk.
        var entry = new BalanceStore.Entry("ETH", "0xabc", 1, 3000, 0, "Optimism");
        var json = JsonSerializer.Serialize(entry);

        Assert.Contains("Optimism", json, StringComparison.Ordinal);
        Assert.Equal("Optimism", JsonSerializer.Deserialize<BalanceStore.Entry>(json)!.Network);
    }
}
