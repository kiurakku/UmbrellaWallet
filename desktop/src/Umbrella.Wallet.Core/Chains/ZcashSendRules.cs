using System.Globalization;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>One transparent coin the wallet can spend, as an explorer reports it.</summary>
/// <param name="Height">The block it was mined in; null while it is still unconfirmed.</param>
public sealed record ZcashUtxo(string TxId, uint Index, ulong Zatoshi, long? Height);

/// <summary>
/// A planned transparent spend: which coins fund it, what it pays, the fee, and the change.
/// Everything here is decided before a key is touched.
/// </summary>
public sealed record ZcashSpendPlan(
    IReadOnlyList<ZcashUtxo> Inputs,
    ulong AmountZat,
    ulong FeeZat,
    ulong ChangeZat,
    byte[] ToScript,
    byte[] ChangeScript)
{
    public ulong TotalInputZat => Inputs.Aggregate(0UL, (sum, u) => sum + u.Zatoshi);

    /// <summary>True when the change was too small to be worth an output and went to the miner instead.</summary>
    public bool ChangeSweptToFee { get; init; }
}

/// <summary>
/// Choosing the coins for a transparent Zcash spend, and every refusal that has to happen before
/// anything is signed. Kept away from the network so each decision is tested offline.
///
/// The fee is not a guess: ZIP-317 tells every Zcash node what a transaction of this shape should pay,
/// and it depends on how many coins fund it — so fee and coin selection are decided together, in one
/// loop, rather than a fee being estimated and then quietly invalidated by the next input.
/// </summary>
public static class ZcashSendRules
{
    /// <summary>
    /// Zcash's own dust rule: three times the floor of 100 zat per 1000 bytes over the output's size
    /// plus the 148 bytes it will one day cost to spend. A standard output lands on 54 zatoshi. An
    /// output below this is refused by every node, so the wallet must never build one.
    /// </summary>
    public static ulong DustThreshold(int scriptPubKeyLength)
    {
        // 8 bytes of value + the script's length prefix + the script itself.
        var serialized = 8 + VarIntSize(scriptPubKeyLength) + scriptPubKeyLength;
        return 3 * (ulong)(100 * (serialized + 148) / 1000);
    }

    /// <summary>
    /// Plans a spend of <paramref name="amountZat"/> to <paramref name="toScript"/>, or says why it
    /// cannot be planned. Only confirmed coins are used: spending an unconfirmed one chains this
    /// payment onto a transaction that may still be replaced or dropped.
    /// </summary>
    public static (ZcashSpendPlan? Plan, string? Error) Plan(
        IReadOnlyList<ZcashUtxo> utxos,
        ulong amountZat,
        byte[] toScript,
        byte[] changeScript)
    {
        if (amountZat == 0) return (null, "Amount must be positive.");

        var dust = DustThreshold(toScript.Length);
        if (amountZat < dust)
            return (null, $"That amount is below Zcash's dust limit ({Zec(dust)} ZEC) and every node would refuse it.");

        // Largest coins first: fewer inputs means a smaller ZIP-317 fee and a smaller transaction.
        var spendable = utxos.Where(u => u.Height is not null && u.Zatoshi > 0)
            .OrderByDescending(u => u.Zatoshi)
            .ToList();
        if (spendable.Count == 0)
            return (null, utxos.Count == 0
                ? "This address holds no confirmed Zcash to spend."
                : "Every coin at this address is still unconfirmed. Wait for a confirmation and try again.");

        var changeDust = DustThreshold(changeScript.Length);
        var chosen = new List<ZcashUtxo>();
        ulong total = 0;

        foreach (var utxo in spendable)
        {
            chosen.Add(utxo);
            total += utxo.Zatoshi;

            // Price this shape with change, since that is what will usually be built.
            var feeWithChange = ZcashTransactions.ConventionalFee(chosen.Count, 2);
            if (total < amountZat + feeWithChange) continue;

            var change = total - amountZat - feeWithChange;
            if (change >= changeDust)
                return (new ZcashSpendPlan(chosen, amountZat, feeWithChange, change, toScript, changeScript), null);

            // No change worth making: the transaction has one output, which ZIP-317 may price lower.
            var feeNoChange = ZcashTransactions.ConventionalFee(chosen.Count, 1);
            if (total >= amountZat + feeNoChange)
            {
                // Whatever is left over beyond the fee is too small to pay out; it goes to the miner
                // rather than becoming an output no node would accept.
                return (new ZcashSpendPlan(chosen, amountZat, total - amountZat, 0, toScript, changeScript)
                    { ChangeSweptToFee = true }, null);
            }
        }

        var shortfallFee = ZcashTransactions.ConventionalFee(chosen.Count, 2);
        var needed = amountZat + shortfallFee;
        return (null, total >= amountZat
            ? $"Not enough ZEC to cover the {Zec(shortfallFee)} ZEC network fee as well as the amount."
            : $"Not enough ZEC: this address holds {Zec(total)} and the send needs {Zec(needed)}.");
    }

    /// <summary>
    /// The whole balance, minus the fee that sending it costs. Returns 0 when the fee eats everything,
    /// which is the honest answer to "send max" on an address that cannot afford to be emptied.
    /// </summary>
    public static ulong MaxSendable(IReadOnlyList<ZcashUtxo> utxos)
    {
        var spendable = utxos.Where(u => u.Height is not null && u.Zatoshi > 0).ToList();
        if (spendable.Count == 0) return 0;
        var total = spendable.Aggregate(0UL, (sum, u) => sum + u.Zatoshi);
        var fee = ZcashTransactions.ConventionalFee(spendable.Count, 1);
        return total > fee ? total - fee : 0;
    }

    /// <summary>
    /// An amount for a message written in English. The decimal separator here is a full stop whatever
    /// the machine's locale says, because these sentences are English and a locale that writes "0,01"
    /// into one of them reads as a thousands separator to everybody else.
    /// </summary>
    private static string Zec(ulong zatoshi) =>
        ZcashTransactions.ToZec(zatoshi).ToString("0.########", CultureInfo.InvariantCulture);

    private static int VarIntSize(int length) => length switch
    {
        < 0xFD => 1,
        <= 0xFFFF => 3,
        _ => 5,
    };
}
