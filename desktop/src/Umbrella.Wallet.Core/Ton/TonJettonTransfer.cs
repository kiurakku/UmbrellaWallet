using System.Numerics;
using System.Text;

namespace Umbrella.Wallet.Core.Ton;

/// <summary>
/// The body of a TEP-74 jetton transfer (roadmap N.3).
///
/// A jetton does not move the way TON does. The wallet sends an internal message to ITS OWN jetton
/// wallet — a separate contract, one per (owner, jetton) — and that contract moves the balance to the
/// recipient's jetton wallet. So the destination of the message and the destination of the money are
/// different addresses, and getting them the wrong way round sends the tokens nowhere recoverable.
///
/// <code>
/// transfer#0f8a7ea5 query_id:uint64 amount:(VarUInteger 16) destination:MsgAddress
///                   response_destination:MsgAddress custom_payload:(Maybe ^Cell)
///                   forward_ton_amount:(VarUInteger 16) forward_payload:(Either Cell ^Cell)
/// </code>
///
/// Correctness is anchored the same way the v4R2 transfer is: <c>TonJettonTransferTests</c> pins the
/// cell HASH to what <c>@ton/core</c> produces for the same inputs. A single wrong bit anywhere in
/// the layout changes that hash, so the test cannot pass while the body is subtly wrong.
/// </summary>
public static class TonJettonTransfer
{
    /// <summary>TEP-74 <c>transfer</c> opcode.</summary>
    public const uint TransferOp = 0x0f8a7ea5;

    /// <summary>
    /// What to attach to the message so the jetton wallet can pay its own forward fees. Jetton
    /// transfers cost more gas than a plain TON send, and an under-funded one bounces back rather
    /// than delivering — which looks to the user exactly like the tokens vanishing for a while.
    /// </summary>
    public const long DefaultAttachedNano = 50_000_000;   // 0.05 TON

    /// <summary>
    /// A token of TON forwarded to the recipient's jetton wallet so it notifies the owner. One
    /// nanoton is enough to trigger the notification without funding anything.
    /// </summary>
    public const long DefaultForwardNano = 1;

    /// <summary>
    /// Builds the transfer body.
    /// </summary>
    /// <param name="amountUnits">The jetton amount in its own smallest units — already scaled by the
    /// jetton's decimals, in integer arithmetic, never through a floating-point step.</param>
    /// <param name="destinationWorkchain">The RECIPIENT's wallet (not their jetton wallet).</param>
    /// <param name="responseWorkchain">Where leftover TON is returned — the sender's own wallet.</param>
    /// <param name="comment">Optional text, carried as a forward payload the recipient can read.</param>
    public static TonCell BuildTransferBody(
        BigInteger amountUnits,
        int destinationWorkchain, byte[] destinationHash,
        int responseWorkchain, byte[] responseHash,
        BigInteger forwardNano,
        string? comment = null)
    {
        if (amountUnits <= 0)
            throw new ArgumentOutOfRangeException(nameof(amountUnits), "A transfer must be a positive amount.");
        if (forwardNano < 0)
            throw new ArgumentOutOfRangeException(nameof(forwardNano), "Forward amount cannot be negative.");

        var b = new TonCellBuilder()
            .StoreUInt(TransferOp, 32)
            .StoreUInt(0, 64)                                             // query_id
            .StoreCoins(amountUnits)
            .StoreAddressStd(destinationWorkchain, destinationHash)
            .StoreAddressStd(responseWorkchain, responseHash)
            .StoreBit(false)                                              // custom_payload: none
            .StoreCoins(forwardNano);

        if (string.IsNullOrEmpty(comment))
        {
            // forward_payload: inline and empty. An empty payload still notifies the recipient's
            // jetton wallet; it just carries nothing.
            b.StoreBit(false);
        }
        else
        {
            // forward_payload: ^Cell, holding a text comment (opcode 0 + UTF-8), because the body is
            // already too full to carry it inline.
            var payload = new TonCellBuilder()
                .StoreUInt(0, 32)
                .StoreBytes(Encoding.UTF8.GetBytes(comment))
                .EndCell();

            b.StoreBit(true).StoreRef(payload);
        }

        return b.EndCell();
    }

    /// <summary>
    /// Converts a human amount into the jetton's smallest units, exactly — and REFUSES an amount
    /// finer than the jetton can represent rather than rounding it. USD₮ on TON has 6 decimals while
    /// most jettons have 9; rounding down loses the remainder silently, rounding up spends more than
    /// the user typed.
    /// </summary>
    public static BigInteger ToUnits(decimal amount, int decimals) =>
        Chains.Erc20Transfer.ToBaseUnits(amount, decimals);
}
