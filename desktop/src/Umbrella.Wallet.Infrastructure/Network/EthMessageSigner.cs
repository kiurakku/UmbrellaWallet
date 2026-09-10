using System;
using Nethereum.Signer;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// Ethereum "personal_sign" (EIP-191) message signing — proves control of an address without moving any
/// funds. The signed payload is prefixed with "\x19Ethereum Signed Message:\n", so the signature can
/// never be replayed as a transaction; it is a statement of ownership, not a spend.
///
/// Signing needs the private key only for the moment of signing; the caller derives it, passes the bytes,
/// and zeroes them afterwards. Verification is public — address, message and signature only.
/// </summary>
public static class EthMessageSigner
{
    /// <summary>Signs <paramref name="message"/> with the given 32-byte private key; returns the 0x… signature.</summary>
    public static string Sign(byte[] privateKey, string message)
    {
        var key = new EthECKey(privateKey, true);
        return new Nethereum.Signer.EthereumMessageSigner().EncodeUTF8AndSign(message ?? string.Empty, key);
    }

    /// <summary>The 0x address that signed <paramref name="message"/> to produce <paramref name="signature"/>.
    /// Throws on a malformed signature.</summary>
    public static string RecoverAddress(string message, string signature) =>
        new Nethereum.Signer.EthereumMessageSigner().EncodeUTF8AndEcRecover(message ?? string.Empty, signature);

    /// <summary>True when <paramref name="signature"/> over <paramref name="message"/> was produced by the
    /// key behind <paramref name="address"/>. Case-insensitive on the hex (EIP-55 checksum ignored), and
    /// never throws — a malformed signature is simply "not valid".</summary>
    public static bool Verify(string address, string message, string signature)
    {
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(signature)) return false;
        try
        {
            var recovered = RecoverAddress(message, signature);
            return string.Equals(Normalize(recovered), Normalize(address), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string Normalize(string a)
    {
        a = (a ?? string.Empty).Trim();
        return a.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? a : "0x" + a;
    }
}
