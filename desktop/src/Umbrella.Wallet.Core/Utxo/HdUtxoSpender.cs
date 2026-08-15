using System.Text;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Utxo;

/// <summary>What the user wants to send, before inputs are chosen. Amounts are in satoshi.</summary>
public sealed record UtxoSpendRequest(
    ChainId Chain,
    string ToAddress,
    long AmountSat,
    double FeeRateSatPerVByte,
    long DevFeeSat = 0,
    string? DevFeeAddress = null,
    string? Memo = null);

/// <summary>
/// A concrete spend: which owned UTXOs fund it (drawn from ANY external or internal address), the
/// amounts, and how much change is due. The change address itself is assigned by the caller after it
/// durably reserves the next internal index — so the plan can be shown on a review screen before any
/// index is burned.
/// </summary>
public sealed record UtxoSpendPlan(
    ChainId Chain,
    IReadOnlyList<OwnedUtxo> Inputs,
    long InputSat,
    long AmountSat,
    long DevFeeSat,
    long FeeSat,
    long ChangeSat)
{
    public bool NeedsChange => ChangeSat > 0;
}

/// <summary>
/// Selects UTXOs across every address the wallet controls and signs a transaction, deriving each
/// input's key from the exact HD path it was received on — not just receive #0. Change returns to a
/// fresh internal (change=1) address. Pure and offline: no network, no store; the caller supplies the
/// discovered UTXOs and the reserved change address, so the whole spend is testable without funds
/// (roadmap §3.3).
/// </summary>
public sealed class HdUtxoSpender
{
    public const long DustSat = 546;

    private readonly HdAddressDeriver _deriver;

    public HdUtxoSpender(HdAddressDeriver? deriver = null) => _deriver = deriver ?? new HdAddressDeriver();

    // Rough virtual sizes per input/output plus fixed overhead, by script type. Segwit (BTC/LTC
    // P2WPKH) is far smaller than legacy (DOGE P2PKH).
    private static (int InputVb, int OutputVb, int OverheadVb) SizeModel(ScriptPubKeyType t) => t switch
    {
        ScriptPubKeyType.Segwit => (68, 31, 11),
        _ => (148, 34, 10),
    };

    /// <summary>
    /// Chooses confirmed inputs largest-first across all owned addresses and computes the fee and
    /// change. Returns an error string rather than throwing on bad input or insufficient funds.
    /// </summary>
    public (UtxoSpendPlan? Plan, string? Error) PlanSpend(
        ChainId chain, IReadOnlyList<OwnedUtxo> utxos, UtxoSpendRequest request)
    {
        if (request.AmountSat <= 0) return (null, "Amount must be positive.");
        if (request.AmountSat < DustSat) return (null, $"Amount is below the dust limit ({DustSat} sat).");
        if (request.FeeRateSatPerVByte <= 0) return (null, "Fee rate must be positive.");

        var (_, _, network, scriptType) = HdAddressDeriver.BitcoinLikeParams(chain);

        try { BitcoinAddress.Create(request.ToAddress, network); }
        catch { return (null, "That is not a valid destination address for this network."); }

        long devFee = 0;
        if (request.DevFeeSat > 0 && !string.IsNullOrWhiteSpace(request.DevFeeAddress))
        {
            try { BitcoinAddress.Create(request.DevFeeAddress!, network); devFee = request.DevFeeSat; }
            catch { devFee = 0; } // a bad fee address must never block the user's own transfer
        }

        if (!string.IsNullOrEmpty(request.Memo) && Encoding.ASCII.GetByteCount(request.Memo) > 80)
            return (null, "Memo is too long for an OP_RETURN (max 80 bytes).");

        var (inVb, outVb, overhead) = SizeModel(scriptType);
        var opReturnVb = string.IsNullOrEmpty(request.Memo) ? 0 : Encoding.ASCII.GetByteCount(request.Memo) + 11;
        var baseOutputs = 1 + (devFee > 0 ? 1 : 0); // recipient (+ dev fee)

        // Only confirmed outputs are spendable — unconfirmed change can vanish on a reorg.
        var candidates = utxos.Where(u => u.Confirmed).OrderByDescending(u => u.ValueSat).ToList();
        if (candidates.Count == 0) return (null, "No confirmed spendable outputs.");

        var selected = new List<OwnedUtxo>();
        long total = 0, fee = 0;
        foreach (var u in candidates)
        {
            selected.Add(u);
            total += u.ValueSat;
            // Assume a change output while selecting; drop it below if change is dust.
            var vsize = selected.Count * inVb + (baseOutputs + 1) * outVb + overhead + opReturnVb;
            fee = (long)Math.Ceiling(vsize * request.FeeRateSatPerVByte);
            if (total >= request.AmountSat + devFee + fee) break;
        }

        if (total < request.AmountSat + devFee + fee)
            return (null, $"Insufficient funds: have {total} sat, need {request.AmountSat + devFee + fee} sat including fee.");

        var change = total - request.AmountSat - devFee - fee;
        long changeSat;
        if (change > DustSat)
        {
            changeSat = change;
        }
        else
        {
            // Change would be dust: drop the change output and roll the remainder into the fee.
            fee = total - request.AmountSat - devFee;
            changeSat = 0;
        }

        return (new UtxoSpendPlan(chain, selected, total, request.AmountSat, devFee, fee, changeSat), null);
    }

    /// <summary>
    /// Builds and signs the transaction for a plan. Each input's Coin carries its own scriptPubKey and
    /// each input's key is derived from its own path, so NBitcoin signs every input with the right key.
    /// When the plan has change, <paramref name="changeAddress"/> (a freshly reserved internal address)
    /// receives it. The signed transaction is verified before it is returned; nothing is broadcast here.
    /// </summary>
    public (Transaction? Tx, string? Error) BuildSigned(
        string mnemonic, UtxoSpendPlan plan, UtxoSpendRequest request, string? changeAddress)
    {
        try
        {
            var (_, _, network, _) = HdAddressDeriver.BitcoinLikeParams(plan.Chain);
            var builder = network.CreateTransactionBuilder();

            foreach (var input in plan.Inputs)
            {
                var account = _deriver.DeriveUtxoAccount(mnemonic, input.Path);
                var coin = new Coin(
                    uint256.Parse(input.TxId), (uint)input.Vout,
                    Money.Satoshis(input.ValueSat), account.ScriptPubKey);
                builder.AddCoins(coin);
                builder.AddKeys(account.PrivateKey);
            }

            builder.Send(BitcoinAddress.Create(request.ToAddress, network), Money.Satoshis(plan.AmountSat));

            if (plan.DevFeeSat > 0 && !string.IsNullOrWhiteSpace(request.DevFeeAddress))
                builder.Send(BitcoinAddress.Create(request.DevFeeAddress!, network), Money.Satoshis(plan.DevFeeSat));

            if (!string.IsNullOrWhiteSpace(request.Memo))
            {
                var memoBytes = Encoding.ASCII.GetBytes(request.Memo);
                if (memoBytes.Length > 80) return (null, "Memo exceeds the 80-byte OP_RETURN limit.");
                builder.Send(TxNullDataTemplate.Instance.GenerateScriptPubKey(memoBytes), Money.Zero);
            }

            builder.SendFees(Money.Satoshis(plan.FeeSat));

            if (plan.NeedsChange)
            {
                if (string.IsNullOrWhiteSpace(changeAddress))
                    return (null, "A change address is required but was not supplied.");
                builder.SetChange(BitcoinAddress.Create(changeAddress, network));
            }

            var tx = builder.BuildTransaction(sign: true);
            if (!builder.Verify(tx, out var errors))
                return (null, "Signature verification failed: " + string.Join("; ", errors.Select(e => e.ToString())));

            return (tx, null);
        }
        catch (Exception ex)
        {
            return (null, $"Build failed: {ex.Message}");
        }
    }
}
