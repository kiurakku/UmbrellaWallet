namespace Umbrella.Wallet.Core.Safety;

/// <summary>One line of "here is exactly what will happen".</summary>
public enum SendEffectKind
{
    /// <summary>The amount the recipient receives.</summary>
    Recipient,
    /// <summary>What the network's miners/validators take.</summary>
    NetworkFee,
    /// <summary>Amount + fee: the number that actually leaves the wallet.</summary>
    TotalLeaving,
    /// <summary>What the wallet holds once this confirms.</summary>
    BalanceAfter,
    /// <summary>Change coming back to an address the user owns (UTXO chains).</summary>
    ChangeReturned,
}

/// <summary>Something worth pausing over. Advisory — none of these block a send.</summary>
public enum SendWarningKind
{
    /// <summary>The send empties the wallet on this chain.</summary>
    EmptiesBalance,
    /// <summary>The fee is a large share of what is being sent.</summary>
    FeeIsLargeShareOfAmount,
    /// <summary>Change is small enough that spending it later may cost more than it is worth.</summary>
    ChangeIsDust,
    /// <summary>Amount plus fee is more than the balance — the send cannot succeed as quoted.</summary>
    ExceedsBalance,
}

public sealed record SendEffect(SendEffectKind Kind, decimal Amount);

public sealed record SendWarning(SendWarningKind Kind, decimal Value);

/// <summary>
/// What a send will actually do, worked out before anything is signed.
///
/// The review screen already showed the amount and the fee. That is not the same as answering the
/// question people actually have, which is "what happens to MY money if I press this". The most common
/// surprises are that the fee comes on top of the amount rather than out of it, that the balance
/// afterwards is not what was assumed, and — on Bitcoin-family chains — the moment of panic when most
/// of the balance appears to move somewhere unfamiliar, which is change returning to an address the
/// wallet owns.
///
/// Pure arithmetic on values the quote already produced: no network, no keys, nothing signed. It
/// describes, it never decides — every warning is advisory.
/// </summary>
public static class SendSimulation
{
    /// <summary>A fee above this share of the amount is worth pointing out.</summary>
    public const decimal LargeFeeShare = 0.10m;

    public sealed record Result(
        IReadOnlyList<SendEffect> Effects,
        IReadOnlyList<SendWarning> Warnings,
        decimal TotalLeaving,
        decimal BalanceAfter);

    /// <summary>
    /// Works out the effects of a send.
    /// </summary>
    /// <param name="balance">Spendable balance on this chain, before the send.</param>
    /// <param name="amount">What the recipient is meant to receive.</param>
    /// <param name="networkFee">The network's fee, paid on top of the amount.</param>
    /// <param name="changeReturned">
    /// Change coming back to the user's own address; zero on account-model chains, which have no change.
    /// </param>
    /// <param name="dustThreshold">Below this, change costs more to spend later than it is worth.</param>
    public static Result Build(
        decimal balance,
        decimal amount,
        decimal networkFee,
        decimal changeReturned = 0m,
        decimal dustThreshold = 0m)
    {
        if (amount < 0) amount = 0;
        if (networkFee < 0) networkFee = 0;
        if (changeReturned < 0) changeReturned = 0;

        var totalLeaving = amount + networkFee;
        var balanceAfter = balance - totalLeaving;

        var effects = new List<SendEffect>
        {
            new(SendEffectKind.Recipient, amount),
            new(SendEffectKind.NetworkFee, networkFee),
            new(SendEffectKind.TotalLeaving, totalLeaving),
        };

        // Only worth a line when there actually is change — saying "change: 0" on Ethereum is noise.
        if (changeReturned > 0) effects.Add(new SendEffect(SendEffectKind.ChangeReturned, changeReturned));

        effects.Add(new SendEffect(SendEffectKind.BalanceAfter, balanceAfter < 0 ? 0 : balanceAfter));

        var warnings = new List<SendWarning>();

        // Ordered by how much they should alarm the reader: impossible first, then irreversible-ish.
        if (totalLeaving > balance)
            warnings.Add(new SendWarning(SendWarningKind.ExceedsBalance, totalLeaving - balance));
        else if (balanceAfter == 0 && balance > 0)
            warnings.Add(new SendWarning(SendWarningKind.EmptiesBalance, balance));

        if (amount > 0 && networkFee > 0 && networkFee >= amount * LargeFeeShare)
            warnings.Add(new SendWarning(SendWarningKind.FeeIsLargeShareOfAmount, networkFee / amount));

        if (changeReturned > 0 && dustThreshold > 0 && changeReturned < dustThreshold)
            warnings.Add(new SendWarning(SendWarningKind.ChangeIsDust, changeReturned));

        return new Result(effects, warnings, totalLeaving, balanceAfter < 0 ? 0 : balanceAfter);
    }
}
