using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NBitcoin;
using NBitcoin.DataEncoders;

using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Infrastructure.Network;

public sealed record TronSendQuote(
    string Symbol,
    string From,
    string To,
    decimal Amount,
    bool IsToken,
    string? RawTransactionJson);

/// <summary>
/// TRON sending: native TRX and USDT (TRC-20).
///
/// TronGrid builds the unsigned transaction (so we don't have to serialise protobuf by hand),
/// we sign its txID locally with the secp256k1 key derived from the vault, and broadcast the
/// signed result. The private key never leaves the process.
///
/// USDT-TRC20 is a contract call — <c>transfer(address,uint256)</c> on the Tether contract —
/// not a plain transfer, which is why it needs its own path.
/// </summary>
public sealed class TronTransactionSender
{
    private const string ApiBase = "https://api.trongrid.io";

    /// <summary>Tether (USDT) TRC-20 contract on TRON mainnet.</summary>
    private const string UsdtContract = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";

    private const int UsdtDecimals = 6;
    private const long TrxSun = 1_000_000;

    /// <summary>Energy budget for a USDT transfer, in SUN. Unused fee is not charged.</summary>
    private const long FeeLimitSun = 40_000_000;

    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    public async Task<(TronSendQuote? Quote, string? Error)> PrepareAsync(
        string symbol, string from, string to, decimal amount, CancellationToken ct = default)
    {
        if (amount <= 0) return (null, "Amount must be positive.");
        if (!IsTronAddress(to)) return (null, "That is not a valid TRON address (should start with T).");

        // USDT is now just the TRC-20 whose contract this build has always known; everything else
        // about it goes down the same path as any other token (roadmap N.2).
        if (symbol.Equals("USDT", StringComparison.OrdinalIgnoreCase))
        {
            return await PrepareTokenAsync(from, UsdtContract, to, amount, UsdtDecimals, "USDT", ct);
        }

        try
        {
            var raw = await BuildTrxTransferAsync(from, to, amount, ct);

            if (raw is null)
            {
                return (null, "TRON API did not return a transaction — try again in a moment.");
            }

            return (new TronSendQuote("TRX", from, to, amount, false, raw), null);
        }
        catch (Exception ex)
        {
            return (null, $"Could not prepare the TRON transaction: {ex.Message}");
        }
    }

    /// <summary>
    /// Quotes a TRC-20 transfer for ANY token, not only USDT (roadmap N.2).
    ///
    /// The shape matches the ERC-20 path deliberately: route on the contract, scale by the decimals
    /// that contract reports, and ask the contract itself what it holds before building anything. A
    /// ticker identifies neither the token nor its precision.
    ///
    /// The fee is paid in TRX (energy/bandwidth), so an address holding only the token cannot send
    /// it — which the caller says before the user reaches Confirm.
    /// </summary>
    public async Task<(TronSendQuote? Quote, string? Error)> PrepareTokenAsync(
        string from, string contract, string to, decimal amount, int decimals, string symbol,
        CancellationToken ct = default)
    {
        if (amount <= 0) return (null, "Amount must be positive.");
        if (!IsTronAddress(to)) return (null, "That is not a valid TRON address (should start with T).");
        if (!IsTronAddress(contract)) return (null, "That token's contract is not a TRON address.");
        if (decimals is < 0 or > 36)
        {
            return (null, "This token did not report how many decimals it uses, so the amount cannot be computed safely.");
        }

        BigInteger units;
        try
        {
            // Exact integer scaling, and a REFUSAL when the amount is finer than the token can
            // represent. The old USDT path multiplied through Math.Pow and truncated, which loses
            // the remainder silently — the one rounding this repository rules out.
            units = Erc20Transfer.ToBaseUnits(amount, decimals);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }

        try
        {
            var held = await GetTokenBalanceAsync(from, contract, decimals, ct);
            if (held is not null && held < amount)
            {
                return (null, $"Insufficient {symbol}: the contract reports {held:0.########}, you asked to send {amount:0.########}.");
            }

            var raw = await BuildTokenTransferAsync(from, contract, to, units, ct);
            if (raw is null) return (null, "TRON API did not return a transaction — try again in a moment.");

            return (new TronSendQuote(symbol, from, to, amount, true, raw), null);
        }
        catch (Exception ex)
        {
            return (null, $"Could not prepare the TRON transaction: {ex.Message}");
        }
    }

    /// <summary>
    /// What the token contract says this address holds, or null when the node would not answer.
    /// Null means "unknown", never "zero": a balance nobody could read must not be reported as empty,
    /// and must not block a send that is otherwise fine.
    /// </summary>
    private static async Task<decimal?> GetTokenBalanceAsync(
        string owner, string contract, int decimals, CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                owner_address = owner,
                contract_address = contract,
                function_selector = "balanceOf(address)",
                parameter = Convert.ToHexString(Base58CheckDecodeTron(owner)).ToLowerInvariant().PadLeft(64, '0'),
                visible = true,
            };

            using var res = await Http.PostAsJsonAsync($"{ApiBase}/wallet/triggerconstantcontract", payload, ct);
            if (!res.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("constant_result", out var results) ||
                results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            {
                return null;
            }

            var hex = results[0].GetString();
            if (string.IsNullOrWhiteSpace(hex)) return null;

            var raw = BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.HexNumber);
            return Erc20Transfer.FromBaseUnits(raw, decimals);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Signs the prepared transaction's txID and broadcasts it.</summary>
    public async Task<(bool Ok, string? TxId, string? Error, bool Unclear)> SignAndBroadcastAsync(
        TronSendQuote quote, Key privateKey, CancellationToken ct = default)
    {
        // The id is known before anything is sent, so a lost answer can still be settled against the chain.
        string? txId = null;
        try
        {
            if (quote.RawTransactionJson is null) return (false, null, "Nothing to sign.", false);

            using var doc = JsonDocument.Parse(quote.RawTransactionJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("txID", out var txIdEl) || txIdEl.GetString() is not { } id)
            {
                var apiError = root.TryGetProperty("Error", out var e) ? e.GetString() : null;
                return (false, null, apiError ?? "TRON API returned no txID.", false);
            }

            txId = id;

            // TRON signs the raw 32-byte txID directly (no extra hashing/prefix).
            var signature = SignTxId(Convert.FromHexString(txId), privateKey);

            // Re-emit the transaction with the signature array attached.
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in root.EnumerateObject())
                {
                    if (property.NameEquals("signature")) continue;
                    property.WriteTo(writer);
                }

                writer.WriteStartArray("signature");
                writer.WriteStringValue(signature);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            var signedJson = Encoding.UTF8.GetString(stream.ToArray());
            using var content = new StringContent(signedJson, Encoding.UTF8, "application/json");
            using var res = await Http.PostAsync($"{ApiBase}/wallet/broadcasttransaction", content, ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            using var result = JsonDocument.Parse(body);
            if (result.RootElement.TryGetProperty("result", out var okEl) && okEl.GetBoolean())
            {
                return (true, txId, null, false);
            }

            var code = result.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
            var message = result.RootElement.TryGetProperty("message", out var m)
                ? DecodeHexMessage(m.GetString())
                : body;

            // TRON says DUP_TRANSACTION_ERROR when it already has this exact transaction — which is
            // what was wanted, not a failure.
            if (code == "DUP_TRANSACTION_ERROR" || (message?.Contains("dup transaction", StringComparison.OrdinalIgnoreCase) ?? false))
                return (true, txId, null, false);

            // Everything TRON refuses on its own terms (signature, bandwidth, balance, expiry) never
            // entered a block. Anything else is unclear and settled against the chain below.
            var refused = code is "SIGERROR" or "BANDWITH_ERROR" or "TAPOS_ERROR" or "TRANSACTION_EXPIRATION_ERROR"
                or "CONTRACT_VALIDATE_ERROR" or "CONTRACT_EXE_ERROR" or "TOO_BIG_TRANSACTION_ERROR";
            if (refused) return (false, null, $"TRON rejected the transaction: {message}", false);

            return await SettleAsync(txId, $"TRON's answer was unclear: {message}", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Before the id exists nothing has been sent; after it, the broadcast may have reached TRON
            // before the connection failed.
            return txId is null
                ? (false, null, $"Send failed: {ex.Message}", false)
                : await SettleAsync(txId, $"The network did not answer ({ex.Message}).", ct);
        }
    }

    /// <summary>Asks TRON about this exact transaction before calling it anything.</summary>
    private async Task<(bool Ok, string? TxId, string? Error, bool Unclear)> SettleAsync(
        string txId, string why, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 2 : 5), ct);
            try
            {
                using var content = new StringContent($"{{\"value\":\"{txId}\"}}", Encoding.UTF8, "application/json");
                using var res = await Http.PostAsync($"{ApiBase}/wallet/gettransactionbyid", content, ct);
                if (!res.IsSuccessStatusCode) continue;
                var body = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("txID", out _)) return (true, txId, null, false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // keep asking
            }
        }

        return (false, txId,
            $"{why} The transaction {txId} may already be on TRON — check it on an explorer before sending " +
            "again, because TRON has no nonce and a second send would pay twice.",
            true);
    }

    private static async Task<string?> BuildTrxTransferAsync(
        string from, string to, decimal amount, CancellationToken ct)
    {
        var payload = new
        {
            owner_address = from,
            to_address = to,
            amount = (long)(amount * TrxSun),
            visible = true,
        };
        using var res = await Http.PostAsJsonAsync($"{ApiBase}/wallet/createtransaction", payload, ct);
        return res.IsSuccessStatusCode ? await res.Content.ReadAsStringAsync(ct) : null;
    }

    private static async Task<string?> BuildTokenTransferAsync(
        string from, string contract, string to, BigInteger units, CancellationToken ct)
    {
        // transfer(address,uint256) — ABI-encoded: 32-byte padded address, then 32-byte amount.
        // The units arrive already scaled, in integer arithmetic, so no decimal step survives here.
        var toHex = Convert.ToHexString(Base58CheckDecodeTron(to)).ToLowerInvariant();
        var parameter = toHex.PadLeft(64, '0') + units.ToString("x").PadLeft(64, '0');

        var payload = new
        {
            owner_address = from,
            contract_address = contract,
            function_selector = "transfer(address,uint256)",
            parameter,
            fee_limit = FeeLimitSun,
            call_value = 0,
            visible = true,
        };

        using var res = await Http.PostAsJsonAsync($"{ApiBase}/wallet/triggersmartcontract", payload, ct);
        if (!res.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        // triggersmartcontract wraps the unsigned tx under "transaction".
        return doc.RootElement.TryGetProperty("transaction", out var tx) ? tx.GetRawText() : null;
    }

    /// <summary>
    /// Recoverable secp256k1 signature over the txID: r ‖ s ‖ v, 65 bytes, hex encoded.
    /// </summary>
    private static string SignTxId(byte[] txId, Key privateKey)
    {
        var compact = privateKey.SignCompact(new uint256(txId), forceLowR: false);

        // NBitcoin gives r‖s (64 bytes) plus a separate recovery id; TRON wants r‖s‖v (65 bytes).
        var signature = new byte[65];
        Buffer.BlockCopy(compact.Signature, 0, signature, 0, 64);
        signature[64] = (byte)compact.RecoveryId;
        return Convert.ToHexString(signature).ToLowerInvariant();
    }

    /// <summary>Base58Check-decodes a TRON address to its 21-byte form (0x41 ‖ 20-byte hash).</summary>
    private static byte[] Base58CheckDecodeTron(string address)
    {
        var full = Encoders.Base58.DecodeData(address);
        if (full.Length != 25) throw new FormatException("Invalid TRON address length.");

        var payload = full[..21];
        var checksum = SHA256.HashData(SHA256.HashData(payload))[..4];
        if (!full[21..].SequenceEqual(checksum)) throw new FormatException("Invalid TRON address checksum.");

        // ABI expects the 20-byte address without TRON's 0x41 prefix.
        return payload[1..];
    }

    private static bool IsTronAddress(string value)
    {
        if (value is not { Length: 34 } || !value.StartsWith('T')) return false;
        try
        {
            Base58CheckDecodeTron(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string DecodeHexMessage(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "unknown error";
        try
        {
            return Encoding.UTF8.GetString(Convert.FromHexString(hex));
        }
        catch
        {
            return hex;
        }
    }
}
