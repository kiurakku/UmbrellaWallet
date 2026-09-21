using System.Globalization;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>A native-XLM transfer as reviewed: everything <see cref="StellarTransactionSender.SignAndBroadcastAsync"/> will sign.</summary>
public sealed record XlmSendQuote(
    string From,
    string To,
    decimal AmountXlm,
    long Stroops,
    bool CreatesAccount,
    uint Fee,
    long Sequence,
    StellarMemo Memo,
    string Horizon)
{
    public decimal FeeXlm => StellarTransactions.ToXlm(Fee);
}

/// <summary>How a Stellar send ended, in the terms the Send screen needs.</summary>
public sealed record XlmSendOutcome(StellarSubmitOutcome Outcome, string? Hash, string? Message);

/// <summary>
/// Real Stellar transfers (roadmap N.5). Reads the sending account and the destination from Horizon,
/// builds the transaction with <see cref="StellarTransactions"/> — pinned byte-for-byte to the Stellar
/// Go SDK — signs it locally and submits it once.
///
/// Every read goes to the user's chosen Horizon (or the SDF's), through <see cref="PublicHttp"/>, so
/// Tor, the proxy and the kill-switch apply. Nothing about the transaction is assumed: an unreadable
/// account, destination or reserve stops the send before anything is signed.
/// </summary>
public sealed class StellarTransactionSender
{
    private static HttpClient Read => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);
    private static HttpClient Submit => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    /// <summary>The Horizon to use: the user's chosen server, or the Stellar Development Foundation's.</summary>
    public static string Root => ChainEndpoints.Resolve("XLM", "https://horizon.stellar.org").TrimEnd('/');

    /// <summary>What the fee bid falls back to when Horizon's fee statistics cannot be read: 0.0001 XLM.
    /// Stellar charges what the ledger needs, not the bid.</summary>
    public const uint FallbackFee = 1_000;

    /// <summary>How long a signed transaction stays valid. After this it can never be included, which is
    /// what settles a submission whose answer never came.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public async Task<(XlmSendQuote? Quote, string? Error)> PrepareAsync(
        string from, string to, decimal amountXlm, string? memoText, CancellationToken ct = default)
    {
        to = to.Trim();
        if (to.Length == 69 && to.StartsWith('M'))
            return (null, "Muxed (M…) addresses are not supported yet. Ask for the G… address and its memo instead.");
        if (!StellarKeys.IsValidAccountId(to)) return (null, "That is not a valid Stellar address (G…).");
        if (string.Equals(to, from, StringComparison.Ordinal)) return (null, "That is this wallet's own Stellar address.");
        if (!StellarMemo.TryParse(memoText, out var memo, out var memoError)) return (null, memoError);
        if (!StellarTransactions.TryToStroops(amountXlm, out var stroops))
            return (null, "Enter a positive amount with at most 7 decimal places.");

        var root = Root;
        var host = new Uri(root).Host;

        // The sending account: its sequence number, balance and the reserve it must keep.
        var (sourceStatus, sourceBody) = await GetAsync($"{root}/accounts/{from}", ct);
        if (sourceStatus == 404)
            return (null, "This Stellar address has not been funded yet. It needs at least 1 XLM before it can send.");
        if (sourceStatus != 200 || sourceBody is null)
            return (null, $"Could not read this account from {host}. Nothing was sent.");
        var state = StellarSendRules.ParseAccount(sourceBody.Value);
        if (state is null) return (null, $"{host} answered with account data this wallet does not understand. Nothing was sent.");

        // The destination: an account already, or an address the first payment has to create.
        var (destStatus, _) = await GetAsync($"{root}/accounts/{to}", ct);
        bool creates;
        if (destStatus == 200) creates = false;
        else if (destStatus == 404) creates = true;
        else return (null, $"Could not check whether the destination exists on {host}. Nothing was sent.");

        if (creates && stroops < StellarSendRules.MinimumNewAccountStroops)
        {
            return (null,
                "The destination is not a Stellar account yet. The first payment to it must be at least 1 XLM, " +
                "which creates the account.");
        }

        var (feeStatus, feeBody) = await GetAsync($"{root}/fee_stats", ct);
        var fee = feeStatus == 200 && feeBody is { } fb && StellarSendRules.ParseFeeBid(fb) is { } bid ? bid : FallbackFee;

        if (stroops + fee > state.SpendableStroops)
        {
            return (null,
                $"Not enough XLM. Spendable: {Xlm(state.SpendableStroops)} XLM — the balance of {Xlm(state.BalanceStroops)} " +
                $"minus the {Xlm(state.LockedStroops)} XLM this account must keep. Needed: {Xlm(stroops + fee)} XLM including the fee.");
        }

        return (new XlmSendQuote(from, to, amountXlm, stroops, creates, fee, state.Sequence + 1, memo, root), null);
    }

    public async Task<XlmSendOutcome> SignAndBroadcastAsync(XlmSendQuote quote, byte[] seed, CancellationToken ct = default)
    {
        var maxTime = (ulong)DateTimeOffset.UtcNow.Add(Window).ToUnixTimeSeconds();
        var transfer = new StellarTransfer(
            quote.From, quote.To, quote.Stroops, quote.CreatesAccount, quote.Fee, quote.Sequence, quote.Memo, maxTime);

        string envelope, hash;
        try
        {
            (envelope, hash) = StellarTransactions.Sign(transfer, seed, StellarTransactions.PublicNetwork);
        }
        catch (Exception ex)
        {
            return new XlmSendOutcome(StellarSubmitOutcome.Rejected, null, $"Could not sign the transaction: {ex.Message}");
        }

        StellarSubmitResult result;
        try
        {
            using var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("tx", envelope)]);
            using var res = await Submit.PostAsync($"{quote.Horizon}/transactions", form, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            result = StellarSubmit.Parse((int)res.StatusCode, body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The request may have reached Horizon before the connection failed.
            result = new StellarSubmitResult(StellarSubmitOutcome.Unknown, null, null, []);
        }

        if (result.Outcome == StellarSubmitOutcome.Included)
        {
            // The hash is computed here, from what was signed; a server answering with another is not believed.
            return string.Equals(result.Hash, hash, StringComparison.OrdinalIgnoreCase)
                ? new XlmSendOutcome(StellarSubmitOutcome.Included, hash, null)
                : new XlmSendOutcome(StellarSubmitOutcome.Unknown, hash,
                    $"The server confirmed a different transaction than the one signed. Check {hash} on an explorer before sending again.");
        }

        if (result.Outcome == StellarSubmitOutcome.Rejected)
            return new XlmSendOutcome(StellarSubmitOutcome.Rejected, null, result.Reason);

        // No answer either way: ask Horizon about THIS transaction for a while before saying so.
        for (var i = 0; i < 6; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            var (status, body) = await GetAsync($"{quote.Horizon}/transactions/{hash}", ct);
            if (status == 200 && body is { } b)
            {
                var ok = !b.TryGetProperty("successful", out var s) || s.ValueKind != JsonValueKind.False;
                return ok
                    ? new XlmSendOutcome(StellarSubmitOutcome.Included, hash, null)
                    : new XlmSendOutcome(StellarSubmitOutcome.Rejected, hash, "The transaction was included but failed; only the fee was charged.");
            }
        }

        var deadline = DateTimeOffset.FromUnixTimeSeconds((long)maxTime).ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
        return new XlmSendOutcome(StellarSubmitOutcome.Unknown, hash,
            $"The network did not confirm the transaction in time. It can still be included until {deadline}, and never " +
            $"after. Check {hash} on an explorer before sending again — a new send would pay twice.");
    }

    private static string Xlm(long stroops) =>
        StellarTransactions.ToXlm(stroops).ToString("0.#######", CultureInfo.InvariantCulture);

    /// <summary>GET a Horizon resource: the status, and the JSON body when there was one.</summary>
    private static async Task<(int Status, JsonElement? Body)> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var res = await ExplorerHttp.GetAsync(Read, url, ct);
            if (!res.IsSuccessStatusCode) return ((int)res.StatusCode, null);
            var text = await res.Content.ReadAsStringAsync(ct);
            return ((int)res.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (0, null);
        }
    }
}
