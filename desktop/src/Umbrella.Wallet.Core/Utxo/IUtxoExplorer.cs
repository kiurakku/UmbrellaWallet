namespace Umbrella.Wallet.Core.Utxo;

/// <summary>Whether an address has ever been used, and its unspent-output balance if so.</summary>
public sealed record AddressActivity(bool Used, int TxCount);

/// <summary>An unspent output as reported by an explorer, before it is tied to an HD path.</summary>
public sealed record ExplorerUtxo(string TxId, int Vout, long ValueSat, bool Confirmed);

/// <summary>
/// The port the HD scanner reads a UTXO chain through. Kept behind an interface so discovery can be
/// exercised with recorded fixtures and offline tests — no real explorer, no real funds. The live
/// adapter (Esplora over the shared Tor-aware HttpClient) lives in the Infrastructure layer.
/// </summary>
public interface IUtxoExplorer
{
    /// <summary>Has this address any confirmed or mempool history? Used to walk the gap limit.</summary>
    Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct);

    /// <summary>The spendable outputs on an address.</summary>
    Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct);
}
