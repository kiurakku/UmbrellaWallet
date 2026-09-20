using System.Globalization;
using System.Numerics;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// Builds the calldata for an ERC-20 <c>transfer(address,uint256)</c>.
///
/// A token transfer does not look like a coin transfer. The transaction goes TO the token's contract
/// with a value of zero, and the real recipient and amount live in the calldata. Two things here lose
/// money if they are wrong, so both are pure and pinned by tests:
///
/// <list type="number">
/// <item><b>Decimals.</b> USDT has 6, most tokens have 18. Sending "1" with the wrong decimals is off
/// by a factor of a trillion — in one direction the transfer is dust, in the other it is the whole
/// balance. The amount is therefore converted exactly, in integer arithmetic, never through a
/// floating-point step.</item>
/// <item><b>Precision.</b> An amount with more decimal places than the token can represent is
/// REFUSED, not rounded. Rounding down quietly loses the remainder; rounding up spends more than the
/// user typed. Neither is acceptable, so the wallet asks rather than guesses.</item>
/// </list>
/// </summary>
public static class Erc20Transfer
{
    /// <summary>First four bytes of keccak256("transfer(address,uint256)"). Fixed by the ERC-20
    /// standard — every token on every EVM chain uses this same selector.</summary>
    public const string TransferSelector = "a9059cbb";

    /// <summary>A token transfer executes contract code, so it needs far more gas than the flat
    /// 21,000 of a plain coin send. 100,000 covers the common implementations with headroom;
    /// unused gas is refunded, so erring high costs nothing but erring low strands the transfer.</summary>
    public const long DefaultGasLimit = 100_000;

    /// <summary>Largest value a uint256 can hold.</summary>
    public static readonly BigInteger MaxUint256 = BigInteger.Pow(2, 256) - 1;

    /// <summary>
    /// The full calldata for a transfer, as a 0x-prefixed hex string: selector, then the recipient
    /// left-padded to 32 bytes, then the amount left-padded to 32 bytes.
    /// </summary>
    public static string EncodeCallData(string recipient, BigInteger baseUnits)
    {
        var address = NormaliseAddress(recipient);
        if (baseUnits <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseUnits), "A transfer must be a positive amount.");
        if (baseUnits > MaxUint256)
            throw new ArgumentOutOfRangeException(nameof(baseUnits), "Amount does not fit in a uint256.");

        return "0x" + TransferSelector + address.PadLeft(64, '0') + ToHex32(baseUnits);
    }

    /// <summary>First four bytes of keccak256("balanceOf(address)") — how the wallet asks the
    /// contract itself what it holds, instead of trusting a cached row when money is about to
    /// move.</summary>
    public const string BalanceOfSelector = "70a08231";

    /// <summary>Calldata for <c>balanceOf(owner)</c>, for an <c>eth_call</c>.</summary>
    public static string EncodeBalanceOf(string owner) =>
        "0x" + BalanceOfSelector + NormaliseAddress(owner).PadLeft(64, '0');

    /// <summary>
    /// Converts a human amount into the token's smallest unit, exactly.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a positive amount, or the decimals are absurd.</exception>
    /// <exception cref="ArgumentException">
    /// The amount is finer than the token can represent. Refusing is deliberate: rounding down loses
    /// the remainder silently and rounding up spends money the user did not agree to.
    /// </exception>
    public static BigInteger ToBaseUnits(decimal amount, int decimals)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "A transfer must be a positive amount.");
        if (decimals is < 0 or > 36)
            throw new ArgumentOutOfRangeException(nameof(decimals), "Token decimals outside any sane range.");

        // Work from decimal's own integer representation rather than multiplying: a token with 18
        // decimals and a large supply (SHIB, PEPE) overflows decimal long before it overflows uint256.
        var bits = decimal.GetBits(amount);
        var scale = (bits[3] >> 16) & 0xFF;
        var unscaled = (new BigInteger((uint)bits[2]) << 64)
                       | (new BigInteger((uint)bits[1]) << 32)
                       | new BigInteger((uint)bits[0]);

        if (decimals >= scale)
            return unscaled * BigInteger.Pow(10, decimals - scale);

        // The typed amount is finer than the token: only allowed when the extra digits are zeros.
        var divisor = BigInteger.Pow(10, scale - decimals);
        var result = BigInteger.DivRem(unscaled, divisor, out var remainder);
        if (!remainder.IsZero)
            throw new ArgumentException(
                $"This token holds {decimals} decimal places; that amount needs {scale}.", nameof(amount));

        return result;
    }

    /// <summary>Convenience: human amount straight to calldata.</summary>
    public static string EncodeCallData(string recipient, decimal amount, int decimals) =>
        EncodeCallData(recipient, ToBaseUnits(amount, decimals));

    /// <summary>Turns a token's smallest unit back into a human amount, for display only.</summary>
    public static decimal FromBaseUnits(BigInteger baseUnits, int decimals)
    {
        if (decimals is < 0 or > 36)
            throw new ArgumentOutOfRangeException(nameof(decimals));

        var divisor = BigInteger.Pow(10, decimals);
        var whole = BigInteger.DivRem(baseUnits, divisor, out var fraction);

        // Rebuilt through a string so a token with 18 decimals does not lose the low digits to
        // decimal's 28-digit limit any earlier than it has to.
        var text = whole.ToString(CultureInfo.InvariantCulture);
        if (!fraction.IsZero && decimals > 0)
            text += "." + fraction.ToString(CultureInfo.InvariantCulture).PadLeft(decimals, '0').TrimEnd('0');

        return decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    /// <summary>Lower-case, 0x-stripped, and verified to be 20 bytes of hex.</summary>
    private static string NormaliseAddress(string? address)
    {
        var a = (address ?? string.Empty).Trim();
        if (a.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) a = a[2..];
        if (a.Length != 40 || !a.All(Uri.IsHexDigit))
            throw new ArgumentException("Not a 20-byte hex address.", nameof(address));

        return a.ToLowerInvariant();
    }

    private static string ToHex32(BigInteger value)
    {
        var hex = value.ToString("x", CultureInfo.InvariantCulture).TrimStart('0');
        if (hex.Length == 0) hex = "0";
        if (hex.Length > 64) throw new ArgumentOutOfRangeException(nameof(value), "Value exceeds 32 bytes.");
        return hex.PadLeft(64, '0');
    }
}
