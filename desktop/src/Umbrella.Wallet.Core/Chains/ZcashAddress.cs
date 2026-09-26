using NBitcoin.DataEncoders;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>What a transparent Zcash address pays to.</summary>
public enum ZcashAddressKind
{
    /// <summary>t1… — pay to public key hash.</summary>
    PublicKeyHash,

    /// <summary>t3… — pay to script hash (a multisig or other script).</summary>
    ScriptHash,
}

/// <summary>A decoded transparent address: which kind it is, and the script that pays it.</summary>
public sealed record ZcashDecodedAddress(ZcashAddressKind Kind, byte[] Hash160, byte[] ScriptPubKey);

/// <summary>
/// Transparent (t-addr) Zcash addresses.
///
/// A t-addr is an ordinary Bitcoin-style Base58Check address with a TWO-byte version prefix instead of
/// one: 0x1C 0xB8 renders as the "t1" leader (pay to public key hash) and 0x1C 0xBD as "t3" (pay to
/// script hash). Two bytes is the whole reason a Bitcoin address cannot be decoded here by accident —
/// and the reason this is a real check rather than a leading-letter test: "t1" and "t3" are what the
/// prefix looks like, not what it is.
///
/// Shielded addresses (z…, u…) are a different scheme entirely and are refused here by name rather
/// than silently failing a checksum, because the honest answer to "can I send to this z-address?" is
/// "not from this wallet", not "that address is invalid".
/// </summary>
public static class ZcashAddress
{
    private static readonly byte[] PubKeyHashPrefix = [0x1C, 0xB8];   // t1
    private static readonly byte[] ScriptHashPrefix = [0x1C, 0xBD];   // t3

    /// <summary>
    /// Decodes a transparent address, or explains why it cannot be. The error is the one the user sees,
    /// so it says what is wrong rather than "invalid address".
    /// </summary>
    public static (ZcashDecodedAddress? Address, string? Error) TryDecode(string? address)
    {
        var a = address?.Trim() ?? string.Empty;
        if (a.Length == 0) return (null, "Enter a Zcash address.");

        if (a.StartsWith('z') || a.StartsWith("u1", StringComparison.Ordinal))
            return (null, "That is a shielded Zcash address. This wallet holds transparent (t1…) Zcash only and cannot pay a shielded address.");

        byte[] raw;
        try
        {
            raw = Encoders.Base58Check.DecodeData(a);
        }
        catch (Exception)
        {
            return (null, "That is not a valid Zcash address — its checksum does not match.");
        }

        // A Zcash prefix is TWO bytes, so every other chain's Base58Check address — Bitcoin's and
        // Litecoin's single version byte included — decodes to 21 and lands here.
        if (raw.Length != 22)
            return (null, "That address belongs to another chain, not Zcash.");

        ZcashAddressKind kind;
        if (raw[0] == PubKeyHashPrefix[0] && raw[1] == PubKeyHashPrefix[1]) kind = ZcashAddressKind.PublicKeyHash;
        else if (raw[0] == ScriptHashPrefix[0] && raw[1] == ScriptHashPrefix[1]) kind = ZcashAddressKind.ScriptHash;
        else return (null, "That address belongs to another chain, not Zcash.");

        var hash = raw[2..];
        return (new ZcashDecodedAddress(kind, hash, ScriptFor(kind, hash)), null);
    }

    /// <summary>True when the address is a transparent Zcash address this wallet can pay.</summary>
    public static bool IsValid(string? address) => TryDecode(address).Address is not null;

    /// <summary>The scriptPubKey that pays a decoded address.</summary>
    public static byte[] ScriptFor(ZcashAddressKind kind, byte[] hash160) => kind switch
    {
        // OP_DUP OP_HASH160 <20> hash OP_EQUALVERIFY OP_CHECKSIG
        ZcashAddressKind.PublicKeyHash => [0x76, 0xA9, 0x14, .. hash160, 0x88, 0xAC],
        // OP_HASH160 <20> hash OP_EQUAL
        ZcashAddressKind.ScriptHash => [0xA9, 0x14, .. hash160, 0x87],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Encodes a t1… address from the Hash160 of a compressed public key.</summary>
    public static string EncodePublicKeyHash(byte[] hash160) =>
        Encoders.Base58Check.EncodeData([.. PubKeyHashPrefix, .. hash160]);
}
