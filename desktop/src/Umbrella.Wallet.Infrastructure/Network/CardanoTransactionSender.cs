using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Umbrella.Wallet.Core.Cardano;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Infrastructure.Network;

public sealed record AdaSendQuote(
    string From,
    string To,
    decimal Amount,
    ulong AmountLovelace,
    ulong Fee,
    ulong Ttl,
    IReadOnlyList<AdaTransfer.TxInput> Inputs,
    IReadOnlyList<AdaTransfer.TxOutput> Outputs);

/// <summary>
/// Real Cardano (ADA) sending over Koios public API. UTXOs and the chain tip are read from Koios, the
/// transaction is built and signed LOCALLY (<see cref="AdaTransfer"/>, pinned byte-for-byte to
/// cardano-serialization-lib), and only the signed CBOR is submitted. Change returns to the sender.
/// </summary>
public sealed class CardanoTransactionSender
{
    private const string Koios = "https://api.koios.rest/api/v1";
    private const ulong MinUtxo = 1_000_000;   // safe ADA-only floor for a valid change output (1 ADA)
    private const ulong MinFeeA = 44;          // linear fee coefficient (lovelace per byte)
    private const ulong MinFeeB = 155_381;     // constant fee term (lovelace)
    private const ulong TtlBufferSlots = 7_200; // ~2 h validity window

    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    private readonly record struct Utxo(byte[] TxHash, ulong Index, ulong Value);

    public async Task<(AdaSendQuote? Quote, string? Error)> PrepareAsync(
        string from, string to, decimal amountAda, CancellationToken ct = default)
    {
        byte[] toAddr, fromAddr;
        try { toAddr = AdaTransfer.DecodeAddress(to); }
        catch { return (null, "That is not a valid Cardano address."); }
        try { fromAddr = AdaTransfer.DecodeAddress(from); }
        catch { return (null, "The wallet's own Cardano address is invalid."); }

        if (amountAda <= 0) return (null, "Amount must be positive.");
        var amount = (ulong)decimal.Truncate(amountAda * 1_000_000m);
        if (amount < MinUtxo) return (null, "The minimum ADA transfer is 1 ADA.");

        var utxos = await FetchUtxosAsync(from, ct);
        if (utxos is null) return (null, "Cardano API (Koios) is unreachable — check your connection (or Tor).");
        if (utxos.Count == 0) return (null, "No spendable ADA at this address.");

        var ttl = await FetchTtlAsync(ct);
        if (ttl is null) return (null, "Could not fetch the Cardano chain tip for a validity window.");

        var ordered = utxos.OrderByDescending(u => u.Value).ToList();

        // Iterate coin-selection and fee until the fee covers the actual signed-tx size. The signed
        // size is measured with placeholder witness bytes (same length as a real vkey + signature),
        // so it is exact without the private key.
        ulong fee = MinFeeB + (MinFeeA * 300);
        List<AdaTransfer.TxInput> inputs = [];
        List<AdaTransfer.TxOutput> outputs = [];

        for (var iteration = 0; iteration < 12; iteration++)
        {
            inputs = [];
            ulong total = 0;
            foreach (var u in ordered)
            {
                inputs.Add(new AdaTransfer.TxInput(u.TxHash, u.Index));
                total += u.Value;
                if (total >= amount + fee + MinUtxo) break; // leave room for a valid change output
            }

            if (total < amount + fee)
            {
                return (null,
                    $"Insufficient funds: have {total / 1_000_000m:0.######} ADA, " +
                    $"need {(amount + fee) / 1_000_000m:0.######} ADA including fee.");
            }

            var change = total - amount - fee;
            if (change >= MinUtxo)
            {
                outputs = [new AdaTransfer.TxOutput(toAddr, amount), new AdaTransfer.TxOutput(fromAddr, change)];
            }
            else
            {
                // No room for a valid change output: everything not sent becomes the fee (one output).
                outputs = [new AdaTransfer.TxOutput(toAddr, amount)];
                fee = total - amount;
            }

            var size = MeasureSignedSize(inputs, outputs, fee, ttl.Value);
            var required = MinFeeB + (MinFeeA * (ulong)size);

            if (change >= MinUtxo)
            {
                if (fee == required) break;        // converged: fee exact, change absorbs the rest
                fee = required;
            }
            else
            {
                if (fee >= required) break;         // one-output tx, remainder-as-fee already covers it
                fee = required;                     // need more inputs next round
            }
        }

        return (new AdaSendQuote(from, to, amountAda, amount, fee, ttl.Value, inputs, outputs), null);
    }

    public async Task<(bool Ok, string? TxId, string? Error)> SignAndBroadcastAsync(
        AdaSendQuote quote, byte[] extendedKey, CancellationToken ct = default)
    {
        try
        {
            var body = AdaTransfer.BuildBody(quote.Inputs, quote.Outputs, quote.Fee, quote.Ttl);
            var hash = AdaTransfer.HashBody(body);
            var signed = AdaTransfer.BuildSignedTx(body, AdaKeys.PublicKey(extendedKey), AdaTransfer.Sign(extendedKey, hash));

            using var content = new ByteArrayContent(signed);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/cbor");
            using var res = await Http.PostAsync($"{Koios}/submittx", content, ct);
            var respBody = (await res.Content.ReadAsStringAsync(ct)).Trim();
            if (!res.IsSuccessStatusCode)
            {
                return (false, null, $"Koios rejected the transaction: {respBody}");
            }

            // submittx returns the transaction id (hash), quoted.
            return (true, respBody.Trim('"'), null);
        }
        catch (Exception ex)
        {
            return (false, null, $"Send failed: {ex.Message}");
        }
    }

    private static int MeasureSignedSize(
        IReadOnlyList<AdaTransfer.TxInput> inputs, IReadOnlyList<AdaTransfer.TxOutput> outputs, ulong fee, ulong ttl)
    {
        var body = AdaTransfer.BuildBody(inputs, outputs, fee, ttl);
        // 32-byte vkey + 64-byte signature placeholders: same CBOR length as the real witness.
        return AdaTransfer.BuildSignedTx(body, new byte[32], new byte[64]).Length;
    }

    private static async Task<List<Utxo>?> FetchUtxosAsync(string address, CancellationToken ct)
    {
        try
        {
            using var body = new StringContent(
                $"{{\"_addresses\":[\"{address}\"]}}", Encoding.UTF8, "application/json");
            using var res = await Http.PostAsync($"{Koios}/address_info", body, ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return [];

            var result = new List<Utxo>();
            if (doc.RootElement[0].TryGetProperty("utxo_set", out var set) && set.ValueKind == JsonValueKind.Array)
            {
                foreach (var u in set.EnumerateArray())
                {
                    var hashHex = u.GetProperty("tx_hash").GetString();
                    var index = u.GetProperty("tx_index").GetUInt64();
                    var valueStr = u.GetProperty("value").ValueKind == JsonValueKind.String
                        ? u.GetProperty("value").GetString()
                        : u.GetProperty("value").GetRawText();
                    if (hashHex is null || !ulong.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                        continue;
                    result.Add(new Utxo(Convert.FromHexString(hashHex), index, value));
                }
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ulong?> FetchTtlAsync(CancellationToken ct)
    {
        try
        {
            using var res = await Http.GetAsync($"{Koios}/tip", ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return null;
            var slot = doc.RootElement[0].GetProperty("abs_slot").GetUInt64();
            return slot + TtlBufferSlots;
        }
        catch
        {
            return null;
        }
    }
}
