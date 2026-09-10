using System.Text;
using System.Text.Json;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Infrastructure.Network;

public sealed record BtcSendQuote(
    string Symbol,
    string From,
    string To,
    decimal Amount,
    long AmountSat,
    long FeeSat,
    decimal FeeAmount,
    int InputCount,
    string Explorer,
    string? DevFeeAddress = null,
    long DevFeeSat = 0,
    // Optional OP_RETURN data (<=80 bytes) — carries a THORChain swap memo, without which a swap
    // deposit would be seen as a plain transfer and the funds lost.
    string? Memo = null);

/// <summary>
/// Real BTC / LTC sending over Esplora-style public explorers. UTXOs are discovered across EVERY
/// address the wallet controls (external + internal) by the caller's scan, the transaction is built
/// and signed LOCALLY with NBitcoin — each input with its own HD key — and only the signed hex is
/// broadcast. Change returns to a fresh internal (change) address, not a reused public one.
/// </summary>
public sealed class BitcoinTransactionSender
{
    private static HttpClient Http => PublicHttp.Shared;

    private readonly HdAddressDeriver _deriver;
    private readonly HdUtxoSpender _spender;

    public BitcoinTransactionSender(HdAddressDeriver? deriver = null)
    {
        _deriver = deriver ?? new HdAddressDeriver();
        _spender = new HdUtxoSpender(_deriver);
    }

    private static ChainId ChainOf(string symbol) => symbol.ToUpperInvariant() switch
    {
        "BTC" => ChainId.Btc,
        "LTC" => ChainId.Ltc,
        "DOGE" => ChainId.Doge,
        "BCH" => ChainId.Bch,
        _ => throw new NotSupportedException($"{symbol} is not a UTXO chain handled here."),
    };

    public static string ExplorerFor(string symbol) => symbol.ToUpperInvariant() switch
    {
        "BTC" => "https://blockstream.info/api",
        "LTC" => "https://litecoinspace.org/api",
        // Dogecoin has no Esplora instance; BlockCypher supplies fee + broadcast (and UTXOs elsewhere).
        "DOGE" => "https://api.blockcypher.com/v1/doge/main",
        // Bitcoin Cash has no Esplora either, and Blockchair rate-limits; Haskoin supplies UTXOs + broadcast.
        "BCH" => "https://api.haskoin.com/bch",
        _ => throw new NotSupportedException($"No explorer for {symbol}."),
    };

    private static bool IsBlockCypher(string symbol) => symbol.ToUpperInvariant() == "DOGE";

    /// <summary>BCH broadcasts + fee go through Haskoin (its own POST /transactions and a fixed fee).</summary>
    private static bool IsHaskoin(string symbol) => symbol.ToUpperInvariant() == "BCH";

    /// <summary>
    /// HD-aware quote: plans a spend over UTXOs already discovered across EVERY address the wallet
    /// controls (supplied by the caller's scan), fetching only the live fee rate. Change is planned
    /// for a fresh internal address, reserved later at broadcast time. Nothing is signed here.
    /// </summary>
    public async Task<(BtcSendQuote? Quote, UtxoSpendPlan? Plan, UtxoSpendRequest? Request, string? Error)>
        PrepareHdAsync(
            string symbol,
            IReadOnlyList<OwnedUtxo> utxos,
            string primaryFromAddress,
            string toAddress,
            decimal amount,
            string? devFeeAddress = null,
            decimal devFeeAmount = 0m,
            string? memo = null,
            FeeLevel feeLevel = FeeLevel.Standard,
            CancellationToken ct = default)
    {
        ChainId chain;
        string explorer;
        try
        {
            chain = ChainOf(symbol);
            explorer = ExplorerFor(symbol);
        }
        catch (NotSupportedException ex)
        {
            return (null, null, null, ex.Message);
        }

        if (amount <= 0) return (null, null, null, "Amount must be positive.");

        var feeRate = await FetchFeeRateAsync(symbol.ToUpperInvariant(), explorer, feeLevel, ct);
        var amountSat = (long)(amount * 100_000_000m);
        var devFeeSat = devFeeAmount > 0 ? (long)(devFeeAmount * 100_000_000m) : 0;

        var request = new UtxoSpendRequest(chain, toAddress, amountSat, feeRate, devFeeSat, devFeeAddress, memo);
        var (plan, error) = _spender.PlanSpend(chain, utxos, request);
        if (plan is null) return (null, null, null, error);

        var quote = new BtcSendQuote(
            symbol.ToUpperInvariant(), primaryFromAddress, toAddress, amount, plan.AmountSat,
            plan.FeeSat, plan.FeeSat / 100_000_000m, plan.Inputs.Count, explorer,
            plan.DevFeeSat > 0 ? devFeeAddress : null, plan.DevFeeSat,
            string.IsNullOrEmpty(memo) ? null : memo);

        return (quote, plan, request, null);
    }

    /// <summary>
    /// Signs the planned spend across all its input addresses and broadcasts it. If the plan has
    /// change, the next internal index is durably reserved BEFORE broadcast (throws on write failure)
    /// and the change is sent there — a fresh address, never a reused public one.
    /// </summary>
    public async Task<(bool Ok, string? TxId, string? Error)> SignAndBroadcastHdAsync(
        string mnemonic,
        string walletId,
        AddressIndexStore store,
        string symbol,
        UtxoSpendPlan plan,
        UtxoSpendRequest request,
        CancellationToken ct = default)
    {
        try
        {
            string? changeAddress = null;
            if (plan.NeedsChange)
            {
                var index = store.ReserveNextChangeIndex(walletId, symbol);
                changeAddress = _deriver.DeriveBitcoinLikeAt(mnemonic, plan.Chain, change: 1, index: index).Address;
            }

            var (tx, error) = _spender.BuildSigned(mnemonic, plan, request, changeAddress);
            if (tx is null) return (false, null, error);

            var hex = tx.ToHex();

            if (IsHaskoin(symbol))
            {
                // Haskoin broadcast: POST the raw transaction hex as the body; the txid returns at "txid".
                using var bchContent = new StringContent(hex, Encoding.ASCII, "text/plain");
                using var bchRes = await Http.PostAsync($"{ExplorerFor(symbol)}/transactions", bchContent, ct);
                var bchBody = (await bchRes.Content.ReadAsStringAsync(ct)).Trim();
                if (!bchRes.IsSuccessStatusCode)
                    return (false, null, $"Explorer rejected the transaction: {bchBody}");
                try
                {
                    using var doc = JsonDocument.Parse(bchBody);
                    if (doc.RootElement.TryGetProperty("txid", out var th) && th.GetString() is { } h)
                        return (true, h, null);
                }
                catch { /* fall back to the locally-computed hash below */ }
                return (true, tx.GetHash().ToString(), null);
            }

            if (IsBlockCypher(symbol))
            {
                // BlockCypher broadcast: POST {"tx":"<hex>"} to /txs/push; the txid returns at tx.hash.
                using var dogeContent = new StringContent($"{{\"tx\":\"{hex}\"}}", Encoding.UTF8, "application/json");
                using var dogeRes = await Http.PostAsync($"{ExplorerFor(symbol)}/txs/push", dogeContent, ct);
                var dogeBody = (await dogeRes.Content.ReadAsStringAsync(ct)).Trim();
                if (!dogeRes.IsSuccessStatusCode)
                    return (false, null, $"Explorer rejected the transaction: {dogeBody}");
                try
                {
                    using var doc = JsonDocument.Parse(dogeBody);
                    if (doc.RootElement.TryGetProperty("tx", out var txEl) &&
                        txEl.TryGetProperty("hash", out var hh) && hh.GetString() is { } h)
                        return (true, h, null);
                }
                catch { /* fall back to the locally-computed hash below */ }
                return (true, tx.GetHash().ToString(), null);
            }

            using var content = new StringContent(hex, Encoding.UTF8, "text/plain");
            using var res = await Http.PostAsync($"{ExplorerFor(symbol)}/tx", content, ct);
            var body = (await res.Content.ReadAsStringAsync(ct)).Trim();
            if (!res.IsSuccessStatusCode)
                return (false, null, $"Explorer rejected the transaction: {body}");

            return (true, body, null);
        }
        catch (Exception ex)
        {
            return (false, null, $"Send failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Economical sat/vB for the chosen <paramref name="level"/>. The network gives one "standard"
    /// estimate (Esplora's 6-block ~1h target, BlockCypher's medium rate, or a safe fixed rate for BCH);
    /// <see cref="FeeLevels.Adjust"/> scales it to Economy/Priority within the SAME safe band that clamps
    /// the standard rate for that chain, so no level can drop below the relay floor (which would strand
    /// the tx) or overpay past the cap. Standard is exactly the rate the wallet used before this selector.
    /// </summary>
    private static async Task<double> FetchFeeRateAsync(string symbol, string explorer, FeeLevel level, CancellationToken ct)
    {
        // Bitcoin Cash blocks are rarely full, so a low fixed rate confirms reliably and cheaply — no fee
        // API needed. 2 sat/vB sits safely above the 1 sat/byte relay minimum. A too-low fee only ever
        // gets a tx stuck (recoverable), never lost.
        if (IsHaskoin(symbol)) return FeeLevels.Adjust(2.0, level, 1.0, 20.0);

        if (IsBlockCypher(symbol))
        {
            // BlockCypher returns fee-per-kB in satoshi. Convert to sat/vB and clamp to a safe Dogecoin
            // band: ~0.01 DOGE/kB (1000 sat/vB) is the floor today's nodes reliably accept; cap at
            // ~0.1 DOGE/kB so a spiky estimate can't overpay wildly. A too-low fee only gets the tx
            // stuck (recoverable), never lost. Levels scale within that same band.
            double doge = 1000.0; // 0.01 DOGE/kB default
            try
            {
                using var res = await Http.GetAsync(explorer, ct);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                    if (doc.RootElement.TryGetProperty("medium_fee_per_kb", out var f))
                        doge = Math.Clamp(f.GetDouble() / 1000.0, 1000.0, 10000.0);
                }
            }
            catch
            {
                // fall through to the safe default
            }
            return FeeLevels.Adjust(doge, level, 1000.0, 10000.0);
        }

        double standard = 2.0; // safe Esplora default
        try
        {
            using var res = await Http.GetAsync($"{explorer}/fee-estimates", ct);
            if (res.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                foreach (var target in new[] { "6", "10", "12", "3" })
                {
                    if (doc.RootElement.TryGetProperty(target, out var rate))
                    {
                        standard = Math.Clamp(rate.GetDouble(), 1.0, 200.0);
                        break;
                    }
                }
            }
        }
        catch
        {
            // fall through to the default
        }

        return FeeLevels.Adjust(standard, level, 1.0, 200.0);
    }
}
