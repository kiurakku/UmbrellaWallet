using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Ton;

namespace Umbrella.Wallet.Infrastructure.Network;

public sealed record TonSendQuote(
    string From, string To, decimal AmountTon, BigInteger AmountNano, uint Seqno, bool Deploy, decimal FeeTon,
    /// <summary>Set for a jetton transfer: the sender's own jetton-wallet contract the message goes
    /// to, the token's ticker, and the amount in the jetton's own units. Null for a plain TON send
    /// (roadmap N.3).</summary>
    string? JettonWallet = null, string? JettonSymbol = null, BigInteger? JettonUnits = default,
    string? JettonDestination = null);

/// <summary>
/// Real TON transfers for the wallet v4R2 contract. Reads the current seqno (and whether the wallet
/// is still un-deployed) from toncenter, builds and ed25519-signs the external message with
/// <see cref="TonTransfer"/> — whose construction is pinned byte-for-byte against @ton/ton — and
/// broadcasts the BoC. The first send from a funded-but-un-deployed wallet automatically carries the
/// StateInit so the contract is deployed in the same transaction.
/// </summary>
public sealed class TonTransactionSender
{
    private const string Base = "https://toncenter.com/api/v2";
    private const decimal NanoPerTon = 1_000_000_000m;

    /// <summary>A comfortable gas buffer for a single v4 transfer (paid from the balance, not the value).</summary>
    private const decimal FeeBufferTon = 0.02m;

    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    public async Task<(TonSendQuote? Quote, string? Error)> PrepareAsync(
        string from, string to, decimal amountTon, CancellationToken ct = default)
    {
        if (amountTon <= 0) return (null, "Amount must be positive.");
        try { TonTransfer.ParseFriendlyAddress(to); }
        catch { return (null, "That is not a valid TON address."); }

        var info = await GetWalletInfoAsync(from, ct);
        if (info is null) return (null, "TON network is unreachable — check your connection (or Tor).");

        var (balanceNano, seqno, deploy) = info.Value;
        var amountNano = (BigInteger)(amountTon * NanoPerTon);
        var needed = amountNano + (BigInteger)(FeeBufferTon * NanoPerTon);
        if (balanceNano < needed)
        {
            return (null,
                $"Insufficient funds: balance {(decimal)balanceNano / NanoPerTon:0.#########} TON, " +
                $"need {(decimal)needed / NanoPerTon:0.#########} TON including fee.");
        }

        return (new TonSendQuote(from, to, amountTon, amountNano, seqno, deploy, FeeBufferTon), null);
    }

    /// <summary>
    /// Quotes a jetton transfer (roadmap N.3).
    ///
    /// The message goes to the sender's OWN jetton wallet, carrying TON for gas; that contract moves
    /// the tokens to the recipient's jetton wallet. So two different addresses matter here and they
    /// are not interchangeable — which is why the destination and the jetton wallet are separate
    /// fields all the way to the signer rather than one "to" that means different things.
    ///
    /// The TON balance has to cover the attached gas even though no TON is being sent, so an address
    /// holding only jettons cannot move them. That is stated before the user reaches Confirm.
    /// </summary>
    public async Task<(TonSendQuote? Quote, string? Error)> PrepareJettonAsync(
        string from, string jettonWallet, string to, decimal amount, int decimals, string symbol,
        CancellationToken ct = default)
    {
        if (amount <= 0) return (null, "Amount must be positive.");
        if (string.IsNullOrWhiteSpace(jettonWallet))
            return (null, "This token has no jetton-wallet address, so it cannot be sent from here.");

        try { TonTransfer.ParseFriendlyAddress(to); }
        catch { return (null, "That is not a valid TON address."); }

        try { TonTransfer.ParseFriendlyAddress(jettonWallet); }
        catch { return (null, "The token's jetton-wallet address is not a valid TON address."); }

        BigInteger units;
        try
        {
            units = Umbrella.Wallet.Core.Ton.TonJettonTransfer.ToUnits(amount, decimals);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }

        var info = await GetWalletInfoAsync(from, ct);
        if (info is null) return (null, "TON network is unreachable — check your connection (or Tor).");

        var (balanceNano, seqno, deploy) = info.Value;
        var attached = new BigInteger(Umbrella.Wallet.Core.Ton.TonJettonTransfer.DefaultAttachedNano);
        var needed = attached + (BigInteger)(FeeBufferTon * NanoPerTon);
        if (balanceNano < needed)
        {
            return (null,
                $"A jetton transfer is paid for in TON: balance {(decimal)balanceNano / NanoPerTon:0.#########} TON, " +
                $"need about {(decimal)needed / NanoPerTon:0.#########} TON for gas.");
        }

        return (new TonSendQuote(
            from, to, (decimal)attached / NanoPerTon, attached, seqno, deploy, FeeBufferTon,
            JettonWallet: jettonWallet, JettonSymbol: symbol, JettonUnits: units, JettonDestination: to), null);
    }

    /// <summary>Signs and broadcasts a jetton transfer quote.</summary>
    public async Task<(bool Ok, string? Result, string? Error)> SignAndBroadcastJettonAsync(
        TonSendQuote quote, byte[] privateKey, string? comment = null, CancellationToken ct = default)
    {
        if (quote.JettonWallet is null || quote.JettonUnits is not { } units || quote.JettonDestination is null)
            return (false, null, "This quote is not a jetton transfer.");

        try
        {
            var publicKey = TonTransfer.PublicKey(privateKey);
            var derived = TonKeys.WalletV4R2Address(publicKey, bounceable: false);
            var (fromWc, fromHash, _) = TonTransfer.ParseFriendlyAddress(quote.From);
            var (derivedWc, derivedHash, _) = TonTransfer.ParseFriendlyAddress(derived);
            if (derivedWc != fromWc || !derivedHash.SequenceEqual(fromHash))
                return (false, null, "Key does not match the sending address — refusing to sign.");

            var (destWc, destHash, _) = TonTransfer.ParseFriendlyAddress(quote.JettonDestination);

            var body = Umbrella.Wallet.Core.Ton.TonJettonTransfer.BuildTransferBody(
                units,
                destWc, destHash,
                fromWc, fromHash,                       // leftover TON returns to the sender
                new BigInteger(Umbrella.Wallet.Core.Ton.TonJettonTransfer.DefaultForwardNano),
                comment);

            var validUntil = (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 90);
            var boc = TonTransfer.BuildSignedJettonBoc(
                privateKey, publicKey, fromWc, fromHash, quote.Seqno, validUntil,
                quote.JettonWallet, quote.AmountNano, body, TonTransfer.ModePayFeesSeparately);

            using var res = await Http.PostAsJsonAsync($"{Base}/sendBoc", new { boc }, ct);
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var hash = doc.RootElement.TryGetProperty("result", out var r) &&
                           r.TryGetProperty("hash", out var h) ? h.GetString() : null;
                return (true, hash ?? "broadcast", null);
            }

            var error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "TON node rejected the transaction";
            return (false, null, error);
        }
        catch (Exception ex)
        {
            return (false, null, $"Send failed: {ex.Message}");
        }
    }

    public async Task<(bool Ok, string? Result, string? Error)> SignAndBroadcastAsync(
        TonSendQuote quote, byte[] privateKey, string? comment = null, CancellationToken ct = default)
    {
        try
        {
            var publicKey = TonTransfer.PublicKey(privateKey);
            // The key must derive the sending address, or we would sign for the wrong wallet.
            var derived = TonKeys.WalletV4R2Address(publicKey, bounceable: false);
            var (fromWc, fromHash, _) = TonTransfer.ParseFriendlyAddress(quote.From);
            var (derivedWc, derivedHash, _) = TonTransfer.ParseFriendlyAddress(derived);
            if (derivedWc != fromWc || !derivedHash.SequenceEqual(fromHash))
                return (false, null, "Key does not match the sending address — refusing to sign.");

            var validUntil = (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 90);
            var boc = TonTransfer.BuildSignedTransferBoc(
                privateKey, publicKey, fromWc, fromHash, quote.Seqno, validUntil,
                quote.To, quote.AmountNano, comment, TonTransfer.ModePayFeesSeparately);

            using var res = await Http.PostAsJsonAsync($"{Base}/sendBoc", new { boc }, ct);
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var hash = doc.RootElement.TryGetProperty("result", out var r) &&
                           r.TryGetProperty("hash", out var h) ? h.GetString() : null;
                return (true, hash ?? "broadcast", null);
            }

            var error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "TON node rejected the transaction";
            return (false, null, error);
        }
        catch (Exception ex)
        {
            return (false, null, $"Send failed: {ex.Message}");
        }
    }

    /// <summary>Balance (nanoTON), current seqno, and whether the wallet still needs deploying.</summary>
    private static async Task<(BigInteger Balance, uint Seqno, bool Deploy)?> GetWalletInfoAsync(
        string address, CancellationToken ct)
    {
        try
        {
            using var res = await Http.GetAsync(
                $"{Base}/getWalletInformation?address={Uri.EscapeDataString(address)}", ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("result", out var result)) return null;

            var balance = BigInteger.Zero;
            if (result.TryGetProperty("balance", out var bal))
            {
                var raw = bal.ValueKind == JsonValueKind.String ? bal.GetString() : bal.GetRawText();
                BigInteger.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out balance);
            }

            uint seqno = 0;
            if (result.TryGetProperty("seqno", out var sq))
            {
                if (sq.ValueKind == JsonValueKind.Number) seqno = (uint)sq.GetInt64();
                else if (sq.ValueKind == JsonValueKind.String && uint.TryParse(sq.GetString(), out var s)) seqno = s;
            }

            var deploy = seqno == 0;
            if (result.TryGetProperty("account_state", out var state) &&
                state.GetString() is { } st && st != "active")
            {
                deploy = true;
            }

            return (balance, seqno, deploy);
        }
        catch
        {
            return null;
        }
    }
}
