using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>An XRP payment as reviewed: everything <see cref="XrpTransactionSender.SignAndBroadcastAsync"/> will sign.</summary>
public sealed record XrpSendQuote(
    string From,
    string To,
    decimal AmountXrp,
    long Drops,
    uint? DestinationTag,
    bool CreatesAccount,
    long FeeDrops,
    uint Sequence,
    long ReserveBaseDrops,
    string Server)
{
    public decimal FeeXrp => XrpTransactions.ToXrp(FeeDrops);
    public decimal ReserveXrp => XrpTransactions.ToXrp(ReserveBaseDrops);
}

/// <summary>How an XRP send ended, in the terms the Send screen needs.</summary>
public sealed record XrpSendOutcome(XrpSubmitOutcome Outcome, string? Hash, string? Message);

/// <summary>
/// Real XRP payments (roadmap N.4, send). Reads the sending account, the network's reserve and fee and
/// the destination from rippled, builds the payment with <see cref="XrpTransactions"/> — pinned
/// byte-for-byte to xrpl.js — signs it locally, submits it once, and waits for a validated ledger,
/// the only thing on the XRP Ledger that is final.
///
/// Every call goes to the user's chosen server, or the listed ones in order, through
/// <see cref="PublicHttp"/>. An unreadable account, reserve or destination stops the send before
/// anything is signed.
/// </summary>
public sealed class XrpTransactionSender
{
    private static HttpClient Read => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);
    private static HttpClient Submit => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    private const string DefaultServer = "https://xrplcluster.com";

    public async Task<(XrpSendQuote? Quote, string? Error)> PrepareAsync(
        string from, string to, decimal amountXrp, string? tagText, CancellationToken ct = default)
    {
        to = to.Trim();
        if (to.Length > 40 && (to.StartsWith('X') || to.StartsWith('T')))
            return (null, "X-addresses are not supported yet. Ask for the classic r… address and its destination tag instead.");
        if (!XrpAddress.IsValid(to)) return (null, "That is not a valid XRP address (r…).");
        if (string.Equals(to, from, StringComparison.Ordinal)) return (null, "That is this wallet's own XRP address.");
        if (!XrpDestinationTag.TryParse(tagText, out var tag, out var tagError)) return (null, tagError);
        if (!XrpTransactions.TryToDrops(amountXrp, out var drops))
            return (null, "Enter a positive amount with at most 6 decimal places.");

        foreach (var server in ChainEndpoints.Candidates("XRP", DefaultServer))
        {
            var host = new Uri(server).Host;

            // The sending account, from the open ledger: that is where the next sequence number is.
            var source = await RpcAsync(server, XrpSendRules.AccountInfoRequest(from, "current"), ct);
            if (source is null) continue;   // this server did not answer; try the next
            var (lookup, state, _) = XrpSendRules.ParseAccount(source.Value, current: true);
            if (lookup == XrpAccountLookup.NotFound)
                return (null, "This XRP address has not been activated yet: it needs to receive the network's reserve before it can send.");
            if (state is null) return (null, $"{host} answered with account data this wallet does not understand. Nothing was sent.");

            var info = await RpcAsync(server, XrpSendRules.ServerInfoRequest(), ct);
            var ledger = info is { } i ? XrpSendRules.ParseServerInfo(i) : null;
            if (ledger is null) return (null, $"Could not read the network's reserve from {host}. Nothing was sent.");

            var feeAnswer = await RpcAsync(server, XrpSendRules.FeeRequest(), ct);
            var fee = feeAnswer is { } f && XrpSendRules.ParseFee(f) is { } open ? open : ledger.BaseFeeDrops;
            if (fee > XrpSendRules.MaxFeeDrops)
            {
                return (null,
                    $"The XRP Ledger is congested right now: getting in would cost {Xrp(fee)} XRP. " +
                    "Try again in a few minutes. Nothing was sent.");
            }

            // The destination, from the last validated ledger.
            var dest = await RpcAsync(server, XrpSendRules.AccountInfoRequest(to, "validated"), ct);
            var (destLookup, destState, _) = dest is { } d
                ? XrpSendRules.ParseAccount(d, current: false)
                : (XrpAccountLookup.Unreadable, null, 0u);
            if (destLookup == XrpAccountLookup.Unreadable)
                return (null, $"Could not check the destination on {host}. Nothing was sent.");

            var creates = destLookup == XrpAccountLookup.NotFound;
            if (creates && drops < ledger.ReserveBaseDrops)
            {
                return (null,
                    $"The destination is not an XRP account yet. The first payment to it must be at least {Xrp(ledger.ReserveBaseDrops)} XRP, " +
                    "which the ledger keeps locked there as the account's reserve.");
            }

            if (destState is { } ds)
            {
                if (ds.RequiresDestinationTag && tag is null)
                {
                    return (null,
                        "This address requires a destination tag. Exchanges use it to tell whose deposit a payment is — " +
                        "enter exactly the tag the recipient gave you.");
                }

                if (ds.DisallowsXrp)
                    return (null, "The owner of this address has asked not to be sent XRP. Nothing was sent.");

                if (ds.RequiresDepositAuth)
                {
                    var auth = await RpcAsync(server, XrpSendRules.DepositAuthorizedRequest(from, to), ct);
                    if ((auth is { } a ? XrpSendRules.ParseDepositAuthorized(a) : null) != true)
                        return (null, "This account only accepts payments from senders it has approved, and this wallet is not one of them. Nothing was sent.");
                }
            }

            var locked = ledger.LockedDrops(state.OwnerCount);
            var spendable = Math.Max(0, state.BalanceDrops - locked);
            if (drops + fee > spendable)
            {
                return (null,
                    $"Not enough XRP. Spendable: {Xrp(spendable)} XRP — the balance of {Xrp(state.BalanceDrops)} " +
                    $"minus the {Xrp(locked)} XRP this account must keep as its reserve. Needed: {Xrp(drops + fee)} XRP including the fee.");
            }

            return (new XrpSendQuote(from, to, amountXrp, drops, tag, creates, fee, state.Sequence, ledger.ReserveBaseDrops, server), null);
        }

        return (null, "No XRP Ledger server answered. Check your connection (or Tor). Nothing was sent.");
    }

    public async Task<XrpSendOutcome> SignAndBroadcastAsync(XrpSendQuote quote, Key key, CancellationToken ct = default)
    {
        // The payment's life is counted from the ledger open NOW, not from when the review was
        // prepared: a review left open for two minutes must not sign something already expired. The
        // same read confirms the account has sent nothing since — the reviewed sequence number is what
        // makes this payment impossible to apply twice, so it is never quietly replaced.
        var now = await RpcAsync(quote.Server, XrpSendRules.AccountInfoRequest(quote.From, "current"), ct);
        var (_, state, first) = now is { } n
            ? XrpSendRules.ParseAccount(n, current: true)
            : (XrpAccountLookup.Unreadable, null, 0u);
        if (state is null)
            return new XrpSendOutcome(XrpSubmitOutcome.Rejected, null, "Could not read the account from the server just before signing. Nothing was sent.");
        if (state.Sequence != quote.Sequence)
            return new XrpSendOutcome(XrpSubmitOutcome.Rejected, null, "This account has sent something since the review. Review the payment again. Nothing was sent.");
        var last = first + XrpSendRules.LedgerWindow;

        string blob, hash;
        try
        {
            (blob, hash) = XrpTransactions.SignPayment(
                new XrpPayment(quote.From, quote.To, quote.Drops, quote.FeeDrops, quote.Sequence, last, quote.DestinationTag), key);
        }
        catch (Exception ex)
        {
            return new XrpSendOutcome(XrpSubmitOutcome.Rejected, null, $"Could not sign the payment: {ex.Message}");
        }

        (XrpSubmitOutcome Outcome, string? Reason) submitted;
        try
        {
            using var res = await Submit.PostAsJsonAsync(quote.Server, XrpSendRules.SubmitRequest(blob), ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            submitted = res.IsSuccessStatusCode && ResultOf(body) is { } result
                ? XrpSubmit.ParseSubmit(result)
                : (XrpSubmitOutcome.Unknown, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            submitted = (XrpSubmitOutcome.Unknown, null);   // it may have reached the server
        }

        if (submitted.Outcome == XrpSubmitOutcome.Rejected)
            return new XrpSendOutcome(XrpSubmitOutcome.Rejected, null, submitted.Reason);

        // Accepted provisionally, or no answer at all: only a validated ledger settles it. Ask about
        // THIS payment until one does — included, failed, or past its last ledger and never included.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(4), ct);
            var lookup = await RpcAsync(quote.Server, XrpSendRules.TxRequest(hash, first, last), ct);
            if (lookup is null) continue;

            var (outcome, reason) = XrpSubmit.ParseLookup(lookup.Value);
            switch (outcome)
            {
                case XrpSubmitOutcome.Included:
                    return new XrpSendOutcome(XrpSubmitOutcome.Included, hash, null);
                case XrpSubmitOutcome.FailedFeeCharged:
                    return new XrpSendOutcome(XrpSubmitOutcome.FailedFeeCharged, hash, reason);
                case XrpSubmitOutcome.Rejected:
                    var why = submitted.Reason is null ? reason : $"{reason} The server had answered {submitted.Reason}.";
                    return new XrpSendOutcome(XrpSubmitOutcome.Rejected, hash, why);
            }
        }

        return new XrpSendOutcome(XrpSubmitOutcome.Unknown, hash,
            $"The network has not confirmed the payment yet. It can only be included up to ledger {last} — about a minute " +
            $"and a half after it was sent — and never after. Check {hash} on an explorer before sending again.");
    }

    private static string Xrp(long drops) =>
        XrpTransactions.ToXrp(drops).ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>A JSON-RPC call: the <c>result</c> object, or null when the server did not answer
    /// usefully. A <c>result</c> carrying an error (like "no such account") is an answer and is returned.</summary>
    private static async Task<JsonElement?> RpcAsync(string server, object request, CancellationToken ct)
    {
        try
        {
            using var res = await Read.PostAsJsonAsync(server, request, ct);
            if (!res.IsSuccessStatusCode) return null;
            return ResultOf(await res.Content.ReadAsStringAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement? ResultOf(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object
                ? result.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
