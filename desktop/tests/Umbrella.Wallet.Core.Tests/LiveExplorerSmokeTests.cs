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

    // A long-lived, high-activity Bitcoin Cash address, for the Haskoin history path.
    private const string ActiveBchAddress = "bitcoincash:qqm04dymqgx3j4kav9xmfvh6ew3jfnagdyn58nq7hl";

    [Fact]
    public async Task Bch_history_parses_real_haskoin_responses()
    {
        // Haskoin's transactions/full is a different shape from Esplora, so its parse can only be proven
        // against a real response. Confirms the live payload yields normalized BCH rows.
        var rows = await new OnChainHistoryClient().GetBitcoinCashAsync(ActiveBchAddress);

        Assert.NotEmpty(rows);
        var t = rows[0];
        Assert.Equal("BCH", t.Asset);
        Assert.True(t.Kind is "Sent" or "Received");
        Assert.Equal(64, t.Hash.Length);                       // a real 32-byte txid, hex-encoded
        Assert.Contains("blockchair.com/bitcoin-cash", t.Explorer);
        Assert.True(t.UnixMs > 0);
    }
}
