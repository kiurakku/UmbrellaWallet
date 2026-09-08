using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Live smoke against the REAL public explorers (needs network; excluded from CI via the "Live"
/// category). Proves the Esplora adapter still parses production responses — the half the offline
/// fake-explorer tests cannot cover. Run explicitly:
///   dotnet test --filter Category=Live
/// BTC (Blockstream) and LTC (litecoinspace) share the identical Esplora shape, so exercising LTC
/// validates the adapter for both; BTC is left out here because Blockstream rate-limits shared IPs.
/// </summary>
[Trait("Category", "Live")]
public sealed class LiveExplorerSmokeTests
{
    // A long-lived, high-activity Litecoin address.
    private const string ActiveLtcAddress = "LYhttvnKawAv6RcHQ4eBkNtifuiEA99PFe";

    [Fact]
    public async Task Ltc_explorer_reports_activity_for_a_used_address()
    {
        var explorer = EsploraUtxoExplorer.For("LTC");
        var activity = await explorer.GetActivityAsync(ActiveLtcAddress, CancellationToken.None);

        Assert.True(activity.Used, "a high-activity address must read as used");
        Assert.True(activity.TxCount > 0);
    }

    [Fact]
    public async Task Ltc_explorer_returns_parseable_utxos()
    {
        var explorer = EsploraUtxoExplorer.For("LTC");
        var utxos = await explorer.GetUtxosAsync(ActiveLtcAddress, CancellationToken.None);

        Assert.NotEmpty(utxos);
        var u = utxos[0];
        Assert.Equal(64, u.TxId.Length); // a real 32-byte txid, hex-encoded
        Assert.True(u.ValueSat > 0);
        Assert.True(u.Vout >= 0);
    }
}
