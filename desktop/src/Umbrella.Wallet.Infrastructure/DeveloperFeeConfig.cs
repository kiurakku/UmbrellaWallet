namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// The per-send platform fee. It is currently OFF: <c>BakedBps</c> is zero, so Umbrella takes no
/// cut of any transfer on any chain.
///
/// The wallet is funded by GitHub Sponsors, feature bounties and (later) a swap spread. A cut of
/// every send is the one charge that makes a self-custody wallet feel like a middleman.
///
/// <para>
/// The routing below is kept, switched off, rather than deleted: it is exercised by the spender
/// tests, and removing machinery from a path that signs real spends is riskier than leaving it at
/// zero. If it is ever switched back on, the percentage is disclosed in the send review before the
/// user confirms — only the recipient address is obfuscated, and that address is public anyway
/// (it appears on-chain in every transfer), so it is obscurity, not secrecy, and never a key.
/// </para>
/// </summary>
public sealed class DeveloperFeeConfig
{
    /// <summary>Hard ceiling: a bug can never quote more than 2%.</summary>
    public const int MaxBps = 200;

    /// <summary>
    /// Baked fee percentage, in basis points. ZERO: Umbrella takes no cut of a send.
    ///
    /// It was 50 (0.5%). The wallet is funded by GitHub Sponsors, feature bounties and (later) a
    /// swap spread instead — a per-send cut is the one thing that makes a self-custody wallet feel
    /// like it is charging you for touching your own money.
    ///
    /// Zero here disables the fee everywhere by construction: QuoteFee returns null for every chain,
    /// so no fee output is ever added and no fee line is ever shown. The routing code below is left
    /// intact and tested rather than ripped out, because deleting machinery from a path that signs
    /// real spends carries more risk than leaving it switched off.
    /// </summary>
    private const int BakedBps = 0;

    private const byte ObfKey = 0x5A;

    /// <summary>
    /// Receiving addresses per canonical chain, obfuscated (each character XOR <see cref="ObfKey"/>,
    /// then base64). Only Solana is set today; add BTC/LTC/XMR here when their addresses exist.
    /// </summary>
    private static readonly Dictionary<string, string> ObfAddresses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Solana fee recipient (routed today).
        ["SOL"] = "GxgCaG4cPhEAOGw0Iw1sPzMLaTgfbw4+AA8KPiMqHWMKaGkbAD8vLggCFm8=",
        // TRON / USDT-TRC20 recipient — the dedicated fee wallet (TNvxWSh…), receives both TRX and
        // TRC-20 USDT. Ethereum and TON recipients stored ready. These send paths do not route a
        // developer fee yet (see RoutedChains), so no fee is quoted or taken on them until that
        // routing is implemented and tested on-chain.
        ["TRX"] = "DhQsIg0JMgs3KyIpMSwcLDJoDh0DMCkxDC0MDR8zKQoZGw==",
        ["USDT"] = "DhQsIg0JMgs3KyIpMSwcLDJoDh0DMCkxDC0MDR8zKQoZGw==",
        ["ETH"] = "aiIZPGkbbWwbPh5raTljGGlpbGxobjw4bjhsYjxoaxxuGWxsbGxuYx9q",
        ["TON"] = "DwseDxYxLzRvMgIIamgqKBc1AB8rIxUUESssLDESMD4tExhrEGxjFBNrGB1iMxsS",
    };

    /// <summary>Chains whose send path actually routes the fee on-chain today.</summary>
    public static readonly IReadOnlySet<string> RoutedChains =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BTC", "LTC", "XMR", "SOL" };

    public int EffectiveBps => Math.Clamp(BakedBps, 0, MaxBps);

    public decimal FeePercent => EffectiveBps / 100m;

    /// <summary>Whether on-chain routing is implemented for this chain today.</summary>
    public static bool IsRouted(string symbol) => RoutedChains.Contains(Canon(symbol));

    /// <summary>The baked receiving address for a chain (de-obfuscated), or null if none set.</summary>
    public string? AddressFor(string symbol) =>
        ObfAddresses.TryGetValue(Canon(symbol), out var obf) ? Deobfuscate(obf) : null;

    /// <summary>Fee amount for a send, in the sent asset's units. Added on top of the amount.</summary>
    public decimal FeeAmount(decimal amount) => amount <= 0 ? 0m : amount * EffectiveBps / 10_000m;

    /// <summary>
    /// The fee to take for this send, or null if none applies — only when the fee is enabled, an
    /// address is baked for the chain, AND the chain's send path routes it today.
    /// </summary>
    public (decimal Amount, string Address)? QuoteFee(string symbol, decimal amount)
    {
        if (EffectiveBps <= 0 || amount <= 0 || !IsRouted(symbol)) return null;
        var address = AddressFor(symbol);
        if (address is null) return null;
        var fee = FeeAmount(amount);
        return fee <= 0 ? null : (fee, address);
    }

    /// <summary>Kept so existing call sites read naturally; the config is entirely baked in.</summary>
    public static DeveloperFeeConfig Load() => new();

    private static string Deobfuscate(string b64)
    {
        var bytes = Convert.FromBase64String(b64);
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++) chars[i] = (char)(bytes[i] ^ ObfKey);
        return new string(chars);
    }

    /// <summary>Canonical chain symbol so TRC-20/TRON variants collapse to one key.</summary>
    private static string Canon(string symbol)
    {
        var s = symbol.Trim().ToUpperInvariant();
        return s switch
        {
            "TRON" or "TRC20" => "TRX",
            "USDT-TRC20" or "USDT_TRC20" => "USDT",
            "MONERO" => "XMR",
            _ => s,
        };
    }
}
