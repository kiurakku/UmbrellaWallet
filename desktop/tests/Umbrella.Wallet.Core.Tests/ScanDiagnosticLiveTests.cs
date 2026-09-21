using System.Collections.Concurrent;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit.Abstractions;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>Live, read-only: runs the real balance scan against the real explorers and reports every
/// failed request, so "balance unavailable" can be traced to its cause. Never part of the offline run.</summary>
[Trait("Category", "Live")]
public sealed class ScanDiagnosticLiveTests(ITestOutputHelper output)
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private sealed class Recording(IUtxoExplorer inner) : IUtxoExplorer
    {
        public ConcurrentBag<string> Failures { get; } = [];
        public int Calls;

        public async Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            try { return await inner.GetActivityAsync(address, ct); }
            catch (Exception ex) { Failures.Add($"activity {address[..10]}: {ex.GetType().Name}: {ex.Message}"); throw; }
        }

        public async Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            try { return await inner.GetUtxosAsync(address, ct); }
            catch (Exception ex) { Failures.Add($"utxo {address[..10]}: {ex.GetType().Name}: {ex.Message}"); throw; }
        }
    }

    [Fact]
    public async Task Koios_on_an_address_nobody_has_used()
    {
        Umbrella.Wallet.Core.Safety.ChainEndpoints.ClearAll();
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
        var phrase = new NBitcoin.Mnemonic(NBitcoin.Wordlist.English, NBitcoin.WordCount.Twelve).ToString();
        var address = new Umbrella.Wallet.Core.Derivation.HdAddressDeriver().DeriveReceiveAddress(phrase, ChainId.Ada).Address;
        using var body = new StringContent($"{{\"_addresses\":[\"{address}\"]}}", System.Text.Encoding.UTF8, "application/json");
        using var res = await PublicHttp.Shared.PostAsync("https://api.koios.rest/api/v1/address_info", body);
        output.WriteLine($"{address} -> {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
    }

    [Theory]
    [InlineData("BTC")]
    [InlineData("LTC")]
    [InlineData("DOGE")]
    public async Task Scan(string symbol)
    {
        Umbrella.Wallet.Core.Safety.ChainEndpoints.ClearAll();   // the offline suite points every chain at a closed port
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);   // the offline suite arms it; this diagnostic is the one place that must reach out
        var chain = symbol switch { "BTC" => ChainId.Btc, "LTC" => ChainId.Ltc, _ => ChainId.Doge };
        IUtxoExplorer inner = symbol == "DOGE" ? new BlockCypherUtxoExplorer("doge") : EsploraUtxoExplorer.For(symbol);
        var rec = new Recording(inner);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await new UtxoAccountScanner().ScanAsync(Phrase, chain, rec, UtxoScanFloors.None);
        output.WriteLine($"{symbol}: partial={result.Partial} calls={rec.Calls} utxos={result.Utxos.Count} in {sw.Elapsed.TotalSeconds:0.0}s");
        foreach (var f in rec.Failures.Take(12)) output.WriteLine("  " + f);
    }
}
