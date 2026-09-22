using System.Security.Cryptography;

namespace Umbrella.Wallet.Core.Security;

/// <summary>
/// Best-effort wiping of sensitive byte buffers after use.
///
/// This raises the bar against casual memory leftovers. It does <em>not</em> defeat malware that
/// already shares the user session (THREAT_MODEL Vector 1). Prefer hardware-wallet signing for
/// serious amounts — see docs/HARDWARE_WALLETS.md.
/// </summary>
public static class SensitiveBytes
{
    /// <summary>Overwrite <paramref name="buffer"/> with zeros. No-op if null or empty.</summary>
    public static void Clear(byte[]? buffer)
    {
        if (buffer is { Length: > 0 })
            CryptographicOperations.ZeroMemory(buffer);
    }

    /// <summary>Overwrite a span with zeros.</summary>
    public static void Clear(Span<byte> buffer)
    {
        if (!buffer.IsEmpty)
            CryptographicOperations.ZeroMemory(buffer);
    }

    /// <summary>
    /// Run <paramref name="work"/> and always clear <paramref name="buffer"/> afterwards
    /// (including when <paramref name="work"/> throws).
    /// </summary>
    public static T Use<T>(byte[] buffer, Func<byte[], T> work)
    {
        try { return work(buffer); }
        finally { Clear(buffer); }
    }

    /// <summary>Same as <see cref="Use{T}"/> for void work.</summary>
    public static void Use(byte[] buffer, Action<byte[]> work)
    {
        try { work(buffer); }
        finally { Clear(buffer); }
    }
}
