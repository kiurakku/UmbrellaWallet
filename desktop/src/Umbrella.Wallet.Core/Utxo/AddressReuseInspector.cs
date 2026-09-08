namespace Umbrella.Wallet.Core.Utxo;

/// <summary>What the wallet knows about a receive address it is about to show.</summary>
public enum AddressUseState
{
    /// <summary>Could not be established (explorer unreachable, or the chain has no explorer here).
    /// Never presented as "fresh" — the UI says it could not check, per the roadmap's no-silent-zero rule.</summary>
    Unknown = 0,

    /// <summary>No on-chain history: safe to hand out.</summary>
    Fresh = 1,

    /// <summary>Already has on-chain history: handing it out again links the two counterparties.</summary>
    Used = 2,
}

/// <summary>
/// Answers one question for the Receive screen: has the address on display already been used?
/// (Secure &amp; anonymous roadmap 3.3 — "warn before receiving to an already-used address".)
///
/// Deliberately split in two so the fast answer needs no network:
/// <see cref="FromLocalState"/> reads the durable scan floors the wallet already keeps, and
/// <see cref="InspectAsync"/> confirms against the explorer. Both fail *open to Unknown*: a network
/// error or missing state is reported as "not checked", never as "fresh", so the warning can only be
/// silenced by evidence.
/// </summary>
public sealed class AddressReuseInspector
{
    /// <summary>
    /// The verdict derivable offline from durable address state. Discovery records the *highest*
    /// external index seen with on-chain history, so an index strictly above it is provably untouched
    /// as of that scan; at or below it, only the explorer can say. Returns
    /// <see cref="AddressUseState.Fresh"/> only when the scan floor proves it.
    /// </summary>
    public static AddressUseState FromLocalState(uint shownIndex, uint? lastSeenUsedExternalIndex)
    {
        if (lastSeenUsedExternalIndex is not { } used) return AddressUseState.Unknown;
        return shownIndex > used ? AddressUseState.Fresh : AddressUseState.Unknown;
    }

    /// <summary>
    /// Asks the explorer whether this exact address has any history. Any failure is
    /// <see cref="AddressUseState.Unknown"/>: a warning is never suppressed by a broken connection.
    /// </summary>
    public async Task<AddressUseState> InspectAsync(
        IUtxoExplorer explorer, string address, CancellationToken ct = default)
    {
        if (explorer is null || string.IsNullOrWhiteSpace(address)) return AddressUseState.Unknown;

        try
        {
            var activity = await explorer.GetActivityAsync(address, ct).ConfigureAwait(false);
            return activity.Used || activity.TxCount > 0 ? AddressUseState.Used : AddressUseState.Fresh;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return AddressUseState.Unknown;
        }
    }

    /// <summary>
    /// The verdict for a shown receive address: the offline floor first (free, instant), and the
    /// explorer only when the floor cannot prove the address is untouched.
    /// </summary>
    public async Task<AddressUseState> InspectAsync(
        IUtxoExplorer explorer,
        string address,
        uint shownIndex,
        uint? lastSeenUsedExternalIndex,
        CancellationToken ct = default)
    {
        var local = FromLocalState(shownIndex, lastSeenUsedExternalIndex);
        if (local == AddressUseState.Fresh) return AddressUseState.Fresh;
        return await InspectAsync(explorer, address, ct).ConfigureAwait(false);
    }
}
