using System.Text;
using System.Text.Json;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Payjoin;
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
/// How a PayJoin attempt ended (roadmap P2.2).
///
/// <see cref="UsedPayjoin"/> false with <see cref="Ok"/> true means the PayJoin failed and the original
/// payment — exactly what the user reviewed — was broadcast instead; <see cref="PayjoinFailure"/> says
/// why. <see cref="OriginalLeftDevice"/> is the fact that matters when even that broadcast fails: the
/// receiver already holds a signed copy of the payment, so it may still be broadcast, and offering a
/// "retry" would risk paying twice.
/// </summary>
public sealed record PayjoinOutcome(
    bool Ok,
    string? TxId,
    string? Error,
    bool UsedPayjoin,
    string? PayjoinFailure,
    long FeeContributionSat,
    bool OriginalLeftDevice);

/// <summary>
/// Real BTC / LTC sending over Esplora-style public explorers. UTXOs are discovered across EVERY
/// address the wallet controls (external + internal) by the caller's scan, the transaction is built
/// and signed LOCALLY with NBitcoin — each input with its own HD key — and only the signed hex is
/// broadcast. Change returns to a fresh internal (change) address, not a reused public one.
/// </summary>
public sealed class BitcoinTransactionSender
{
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

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
            var changeAddress = ReserveChangeAddress(mnemonic, walletId, store, symbol, plan);

            var (tx, error) = _spender.BuildSigned(mnemonic, plan, request, changeAddress);
            if (tx is null) return (false, null, error);

            return await BroadcastAsync(symbol, tx, ct);
        }
        catch (Exception ex)
        {
            return (false, null, $"Send failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Change returns to the same branch the inputs came from, and its index is reserved on THAT
    /// branch's counter — a Taproot change address derived from the SegWit counter would be an address
    /// the scanner does not look for (roadmap P2.1). Reserved and persisted before anything is signed.
    /// </summary>
    private string? ReserveChangeAddress(
        string mnemonic, string walletId, AddressIndexStore store, string symbol, UtxoSpendPlan plan)
    {
        if (!plan.NeedsChange) return null;

        var branch = AddressIndexStore.BranchKey(symbol, plan.ChangeKind);
        var index = store.ReserveNextChangeIndex(walletId, branch);
        return _deriver
            .DeriveBitcoinLikeAt(mnemonic, plan.Chain, change: 1, index: index, kind: plan.ChangeKind)
            .Address;
    }

    /// <summary>
    /// True when a PayJoin can be attempted for this plan: Bitcoin, and every input of one script
    /// type. A payment that already mixes types cannot be made to look uniform by the receiver, and
    /// contacting the receiver at all would hand it the inputs for nothing.
    /// </summary>
    /// <summary>The upper bound on what a PayJoin receiver may take from the change, as shown on the
    /// review screen. The same number caps the offer actually sent.</summary>
    public static long PayjoinOfferCapSat(UtxoSpendPlan plan, UtxoSpendRequest request) =>
        plan.NeedsChange && plan.Inputs.Count > 0
            ? PayjoinPlanner.OfferCapSat(
                request.FeeRateSatPerVByte, HdUtxoSpender.InputVirtualSize(plan.Chain, plan.Inputs[0].Path.Kind))
            : 0;

    public static bool CanAttemptPayjoin(string symbol, UtxoSpendPlan plan) =>
        symbol.Equals("BTC", StringComparison.OrdinalIgnoreCase) &&
        plan.Inputs.Count > 0 &&
        plan.Inputs.Select(i => i.Path.Kind).Distinct().Count() == 1;

    /// <summary>
    /// Sends a Bitcoin payment as a PayJoin (BIP-78, sender side), falling back to the reviewed payment.
    ///
    /// The receiver is sent the original — signed, complete, broadcastable — and may answer with a
    /// proposal that adds a coin of its own. That proposal is signed only if
    /// <see cref="PayjoinProposalChecker"/> accepts it, and broadcast only if every input verifies and
    /// the fee rate holds. On ANY failure along the way the original is broadcast instead: the receiver
    /// already has it and may broadcast it anyway, and it is exactly what the user reviewed.
    /// </summary>
    public async Task<PayjoinOutcome> SignAndBroadcastPayjoinAsync(
        string mnemonic,
        string walletId,
        AddressIndexStore store,
        string symbol,
        UtxoSpendPlan plan,
        UtxoSpendRequest request,
        Uri endpoint,
        CancellationToken ct = default)
    {
        PSBT? original;
        Transaction? originalTx;
        PayjoinParameters parameters;
        int inputVsize;

        try
        {
            if (!CanAttemptPayjoin(symbol, plan))
            {
                var (ok, txid, error) = await SignAndBroadcastHdAsync(mnemonic, walletId, store, symbol, plan, request, ct);
                return new PayjoinOutcome(ok, txid, error, false,
                    "PayJoin was not attempted: this payment spends more than one kind of address.", 0, false);
            }

            var changeAddress = ReserveChangeAddress(mnemonic, walletId, store, symbol, plan);

            string? buildError;
            (original, originalTx, buildError) = _spender.BuildOriginalPsbt(mnemonic, plan, request, changeAddress);
            if (original is null || originalTx is null)
                return new PayjoinOutcome(false, null, buildError, false, null, 0, false);

            inputVsize = HdUtxoSpender.InputVirtualSize(plan.Chain, plan.Inputs[0].Path.Kind);
            var changeScript = changeAddress is null
                ? null
                : BitcoinAddress.Create(changeAddress, NBitcoin.Network.Main).ScriptPubKey;
            parameters = PayjoinPlanner.ParametersFor(
                originalTx, original.GetFee().Satoshi, changeScript, inputVsize, HdUtxoSpender.DustSatFor(plan.Chain),
                offerCapSat: PayjoinOfferCapSat(plan, request));
        }
        catch (Exception ex)
        {
            // Nothing has left the device yet: the original was never sent anywhere.
            return new PayjoinOutcome(false, null, $"Send failed: {ex.Message}", false, null, 0, false);
        }

        // From here on the original has been — or is about to be — handed to the receiver.
        string failure;
        try
        {
            var (proposal, requestError) = await PayjoinClient.RequestAsync(endpoint, original, parameters, ct);
            if (proposal is null)
            {
                failure = requestError ?? "The receiver did not answer.";
            }
            else
            {
                var paymentScript = BitcoinAddress.Create(request.ToAddress, NBitcoin.Network.Main).ScriptPubKey;
                var check = PayjoinProposalChecker.Check(original, proposal, paymentScript, parameters, inputVsize);

                if (!check.Ok)
                {
                    failure = check.Reason ?? "The receiver's proposal failed a check.";
                }
                else
                {
                    var (payjoinTx, fee, signError) = _spender.SignPayjoinProposal(mnemonic, plan, original, proposal);
                    var rateError = payjoinTx is null
                        ? null
                        : PayjoinProposalChecker.CheckFinalFeeRate(payjoinTx, fee, parameters.MinFeeRateSatPerVByte);

                    if (payjoinTx is null) failure = signError ?? "The PayJoin could not be signed.";
                    else if (rateError is not null) failure = rateError;
                    else
                    {
                        var (ok, txid, broadcastError) = await BroadcastAsync(symbol, payjoinTx, ct);
                        if (ok) return new PayjoinOutcome(true, txid, null, true, null, check.FeeContributionSat, true);
                        failure = $"The network refused the PayJoin transaction: {broadcastError}";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex is OperationCanceledException ? "The receiver did not answer in time." : ex.Message;
        }

        var (sent, originalTxId, originalError) = await BroadcastAsync(symbol, originalTx, CancellationToken.None);
        return new PayjoinOutcome(sent, originalTxId, originalError, false, failure, 0, OriginalLeftDevice: true);
    }

    /// <summary>Hands a signed transaction to the chain's explorer and returns its id.</summary>
    private async Task<(bool Ok, string? TxId, string? Error)> BroadcastAsync(
        string symbol, Transaction tx, CancellationToken ct)
    {
        try
        {
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
