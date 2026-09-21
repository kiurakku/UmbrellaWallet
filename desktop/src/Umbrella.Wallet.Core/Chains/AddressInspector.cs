using System;
using NBitcoin;
using NBitcoin.Altcoins;
using NBitcoin.DataEncoders;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>How sure we are an address is well-formed.</summary>
public enum AddressValidity
{
    /// <summary>Checksum verified (EIP-55 for EVM; base58check / bech32 for the UTXO chains).</summary>
    Valid,
    /// <summary>The shape says it should validate on this network, but the checksum does not.</summary>
    Invalid,
    /// <summary>Recognised by shape, but a definitive check would need a chain-specific library we do not
    /// bundle (Cardano, TON, Monero), or the address is not recognised at all.</summary>
    Unverified,
}

/// <summary>What the checker found: the most likely network (empty when unrecognised) and how confident
/// we are the address is well-formed.</summary>
public sealed record AddressInspectResult(string Network, AddressValidity Validity)
{
    public bool Recognised => Network.Length > 0;
}

/// <summary>
/// Read-only "what is this address?" tool: names the most likely network from an address's shape and,
/// where it can be done without guessing, verifies the checksum. Pure and offline so it can be unit-tested
/// and never leaks anything. The network is a best-guess from the shape (base58 P2SH is genuinely
/// ambiguous across chains); the <see cref="AddressValidity"/> is definitive where it is not "Unverified".
/// </summary>
public static class AddressInspector
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    public static AddressInspectResult Inspect(string? address)
    {
        var a = (address ?? string.Empty).Trim();
        if (a.Length == 0) return new AddressInspectResult(string.Empty, AddressValidity.Unverified);

        // EVM (0x…): EIP-55 checksum is the strong check.
        if (a.StartsWith("0x", OIC))
        {
            return EvmAddress.Check(a) switch
            {
                EvmChecksumState.Valid or EvmChecksumState.NoChecksum => new("ETH", AddressValidity.Valid),
                EvmChecksumState.Invalid => new("ETH", AddressValidity.Invalid),
                _ => new(string.Empty, AddressValidity.Unverified),
            };
        }

        // Bitcoin-family: NBitcoin validates the base58check / bech32 checksum against the network.
        var (sym, net) = BitcoinLikeNetwork(a);
        if (net is not null)
            return new(sym!, ParsesOn(a, net) ? AddressValidity.Valid : AddressValidity.Invalid);

        // TRON / Zcash-transparent: plain base58check.
        if (a.StartsWith('T') && a.Length == 34)
            return new("TRX", Base58CheckOk(a) ? AddressValidity.Valid : AddressValidity.Invalid);
        if (a.StartsWith("t1", StringComparison.Ordinal) && a.Length == 35)
            return new("ZEC", Base58CheckOk(a) ? AddressValidity.Valid : AddressValidity.Invalid);

        // XRP: its own base58 alphabet plus a double-SHA256 checksum, so this is a definitive answer
        // rather than a guess from the shape. Worth checking early — XRP is one of the coins people
        // most often try to send over the wrong network, and the alphabet makes a false match unlikely.
        if (a.StartsWith('r') && a.Length is >= 25 and <= 35)
            return new("XRP", XrpAddress.IsValid(a) ? AddressValidity.Valid : AddressValidity.Invalid);

        // Stellar: StrKey carries a CRC16 checksum, so this is definitive too.
        if (a.StartsWith('G') && a.Length == 56)
            return new("XLM", StellarKeys.IsValidAccountId(a) ? AddressValidity.Valid : AddressValidity.Invalid);

        // Recognised by shape, but a deep check needs a chain-specific library we do not bundle.
        if (a.StartsWith("addr1", OIC)) return new("ADA", AddressValidity.Unverified);
        if (a.Length == 48 && (a.StartsWith("UQ") || a.StartsWith("EQ") || a.StartsWith("kQ") || a.StartsWith("0Q")))
            return new("TON", AddressValidity.Unverified);
        if ((a.StartsWith('4') || a.StartsWith('8')) && a.Length is 95 or 106)
            return new("XMR", AddressValidity.Unverified);

        // Solana and other bare base58 are ambiguous — do not guess.
        return new AddressInspectResult(string.Empty, AddressValidity.Unverified);
    }

    /// <summary>Best-guess Bitcoin-family network from the address prefix, or (null,null) if it isn't one.</summary>
    private static (string?, Network?) BitcoinLikeNetwork(string a)
    {
        if (a.StartsWith("bc1", OIC)) return ("BTC", Network.Main);
        if (a.StartsWith("ltc1", OIC)) return ("LTC", Litecoin.Instance.Mainnet);
        if (a.StartsWith("bitcoincash:", OIC)) return ("BCH", BCash.Instance.Mainnet);
        if (a.StartsWith('D')) return ("DOGE", Dogecoin.Instance.Mainnet);
        if (a.StartsWith('L') || a.StartsWith('M')) return ("LTC", Litecoin.Instance.Mainnet);
        if (a.StartsWith('1') || a.StartsWith('3')) return ("BTC", Network.Main);
        return (null, null);
    }

    private static bool ParsesOn(string a, Network net)
    {
        try { BitcoinAddress.Create(a, net); return true; }
        catch { return false; }
    }

    private static bool Base58CheckOk(string a)
    {
        try { Encoders.Base58Check.DecodeData(a); return true; }
        catch { return false; }
    }
}
