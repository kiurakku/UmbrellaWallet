using System.Text;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace Umbrella.Wallet.Core.Psbt;

/// <summary>
/// Reads a PSBT (BIP-174) in any of the three shapes other wallets hand them over in: the binary
/// <c>.psbt</c> file, base64 text, or hex text. Bitcoin mainnet only (roadmap H.1).
/// </summary>
public static class PsbtCodec
{
    /// <summary>"psbt" followed by 0xff — the BIP-174 magic every PSBT begins with.</summary>
    private static readonly byte[] Magic = { 0x70, 0x73, 0x62, 0x74, 0xff };

    /// <summary>A PSBT for a handful of inputs is a few kilobytes. Anything past this is not one we
    /// should be parsing.</summary>
    public const int MaxBytes = 1_000_000;

    public static bool TryRead(byte[] data, out PSBT? psbt, out string? error)
    {
        psbt = null;
        error = null;

        if (data.Length == 0) { error = "The file is empty."; return false; }
        if (data.Length > MaxBytes) { error = "The file is too large to be a PSBT."; return false; }

        if (data.AsSpan().StartsWith(Magic)) return TryLoad(data, out psbt, out error);

        // Many tools save the base64 text with a .psbt extension instead of the binary form.
        string text;
        try { text = Encoding.UTF8.GetString(data); }
        catch { error = "This is not a PSBT."; return false; }

        return TryRead(text, out psbt, out error);
    }

    public static bool TryRead(string? text, out PSBT? psbt, out string? error)
    {
        psbt = null;
        error = null;

        var trimmed = new string((text ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (trimmed.Length == 0) { error = "Paste a PSBT or open a .psbt file."; return false; }
        if (trimmed.Length > MaxBytes * 2) { error = "That is too large to be a PSBT."; return false; }

        // Hex: "70736274ff…"
        if (trimmed.StartsWith("70736274ff", StringComparison.OrdinalIgnoreCase))
        {
            try { return TryLoad(Encoders.Hex.DecodeData(trimmed), out psbt, out error); }
            catch { error = "That looks like hex but does not decode."; return false; }
        }

        // Base64: "cHNidP8…"
        byte[] bytes;
        try { bytes = Convert.FromBase64String(trimmed); }
        catch { error = "This is not a PSBT (neither base64 nor hex)."; return false; }

        if (!bytes.AsSpan().StartsWith(Magic)) { error = "This is not a PSBT."; return false; }
        return TryLoad(bytes, out psbt, out error);
    }

    private static bool TryLoad(byte[] bytes, out PSBT? psbt, out string? error)
    {
        try
        {
            psbt = PSBT.Load(bytes, Network.Main);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            psbt = null;
            error = $"The PSBT could not be read: {ex.Message}";
            return false;
        }
    }
}
