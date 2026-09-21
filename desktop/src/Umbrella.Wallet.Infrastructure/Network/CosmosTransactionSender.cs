using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>An ATOM transfer as reviewed: everything <see cref="CosmosTransactionSender.SignAndBroadcastAsync"/> will sign.</summary>
public sealed record AtomSendQuote(
    string From,
    byte[] PublicKey,
    string To,
    decimal AmountAtom,
    ulong Micro,
    string Memo,
    ulong AccountNumber,
    ulong Sequence,
    ulong GasLimit,
    ulong FeeMicro,
    string Server)
{
    public decimal FeeAtom => CosmosTransactions.ToAtom(FeeMicro);
}

/// <summary>How an ATOM send ended, in the terms the Send screen needs.</summary>
public sealed record AtomSendOutcome(CosmosSubmitOutcome Outcome, string? Hash, string? Message);

/// <summary>
/// Real ATOM transfers on the Cosmos Hub (roadmap N.6, send). Reads the account, the chain id, the
/// balance and the fee market from a Hub REST node, has the node simulate the transfer for its gas,
/// builds it with <see cref="CosmosTransactions"/> — pinned byte-for-byte to cosmjs — signs it locally
/// and broadcasts it once.
///
/// Every call goes to the user's chosen node, or the listed ones in order, through
/// <see cref="PublicHttp"/>. A node that says it is on any chain but cosmoshub-4 is not believed, and an
/// unreadable account, balance or gas price stops the send before anything is signed.
/// </summary>
public sealed class CosmosTransactionSender
{
    private static HttpClient Read => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);
    private static HttpClient Submit => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    private const string DefaultServer = "https://cosmos-rest.publicnode.com";

    /// <summary>The gas assumed for the provisional fee a simulation is run with; the real limit comes
    /// from what the simulation used.</summary>
    private const ulong ProvisionalGas = 200_000;

    public async Task<(AtomSendQuote? Quote, string? Error)> PrepareAsync(
        string from, byte[] publicKey, string to, decimal amountAtom, string? memoText, CancellationToken ct = default)
    {
        to = to.Trim();
        if (!CosmosHub.IsValidAddress(to)) return (null, "That is not a valid Cosmos Hub address (cosmos1…).");
        if (string.Equals(to, from, StringComparison.Ordinal)) return (null, "That is this wallet's own Cosmos address.");
        if (!CosmosTransactions.TryValidateMemo(memoText, out var memo, out var memoError)) return (null, memoError);
        if (!CosmosTransactions.TryToMicro(amountAtom, out var micro))
            return (null, "Enter a positive amount with at most 6 decimal places.");

        foreach (var root in ChainEndpoints.Candidates("ATOM", DefaultServer))
        {
            var server = root.TrimEnd('/');
            var host = new Uri(server).Host;

            var (infoStatus, info) = await GetAsync($"{server}/cosmos/base/tendermint/v1beta1/node_info", ct);
            if (infoStatus == 0) continue;   // this node did not answer; try the next
            var network = info is { } i ? CosmosSendRules.ParseNodeNetwork(i) : null;
            if (network != CosmosTransactions.HubChainId)
                return (null, $"{host} did not say it is on the Cosmos Hub ({network ?? "no answer"}). Nothing was sent.");

            var (accountStatus, accountBody) = await GetAsync($"{server}/cosmos/auth/v1beta1/accounts/{from}", ct);
            var (lookup, account) = CosmosSendRules.ParseAccount(accountStatus, accountBody);
            switch (lookup)
            {
                case CosmosAccountLookup.NotFound:
                    return (null, "This Cosmos address has not received anything yet, so there is nothing to send.");
                case CosmosAccountLookup.Unsupported:
                    return (null, "This is a vesting or special account; sending from it is not supported here. Nothing was sent.");
                case CosmosAccountLookup.Unreadable:
                    return (null, $"Could not read this account from {host}. Nothing was sent.");
            }

            if (account!.PublicKey is { } onChain && !onChain.AsSpan().SequenceEqual(publicKey))
                return (null, "The chain has a different key on record for this address. Nothing was sent.");

            var (balanceStatus, balanceBody) = await GetAsync(
                $"{server}/cosmos/bank/v1beta1/balances/{from}/by_denom?denom={CosmosHub.Denom}", ct);
            if (CosmosHub.ParseBalance(balanceStatus, balanceBody) is not { } balanceAtom)
                return (null, $"Could not read the balance from {host}. Nothing was sent.");
            var balance = (ulong)(balanceAtom * CosmosHub.MicroPerAtom);

            var (_, priceBody) = await GetAsync($"{server}/feemarket/v1/gas_price/{CosmosHub.Denom}", ct);
            if ((priceBody is { } pb ? CosmosSendRules.ParseGasPrice(pb) : null) is not { } gasPrice)
                return (null, $"Could not read the network's gas price from {host}. Nothing was sent.");

            // Enough for the amount and a provisional fee before asking the node anything more.
            var (_, provisionalFee) = CosmosSendRules.FeeFor(ProvisionalGas, gasPrice);
            if (micro + provisionalFee > balance)
                return (null, NotEnough(balance, micro + provisionalFee));

            // The node runs the transfer without applying it; its answer is the gas it needs — or the
            // reason the real one would be refused.
            var draft = new CosmosSend(from, to, micro, CosmosHub.Denom, memo, TimeoutHeight: 0, publicKey,
                account.AccountNumber, account.Sequence, provisionalFee, ProvisionalGas, CosmosTransactions.HubChainId);
            var (simStatus, simBody) = await PostAsync(Read, $"{server}/cosmos/tx/v1beta1/simulate",
                new { tx_bytes = Convert.ToBase64String(CosmosTransactions.SimulationBytes(draft)) }, ct);
            var (gasUsed, simError) = CosmosSendRules.ParseSimulation(simStatus, simBody);
            if (gasUsed is not { } used)
                return (null, simError is null
                    ? $"{host} could not work out the gas for this transfer. Nothing was sent."
                    : $"The network would refuse this transfer: {simError}. Nothing was sent.");

            var (gasLimit, fee) = CosmosSendRules.FeeFor(used, gasPrice);
            if (fee > CosmosSendRules.MaxFeeMicro)
                return (null, $"The Cosmos Hub is congested right now: the fee would be {Atom(fee)} ATOM. Try again in a few minutes. Nothing was sent.");
            if (micro + fee > balance) return (null, NotEnough(balance, micro + fee));

            return (new AtomSendQuote(from, publicKey, to, amountAtom, micro, memo, account.AccountNumber,
                account.Sequence, gasLimit, fee, server), null);
        }

        return (null, "No Cosmos Hub node answered. Check your connection (or Tor). Nothing was sent.");
    }

    public async Task<AtomSendOutcome> SignAndBroadcastAsync(AtomSendQuote quote, Key key, CancellationToken ct = default)
    {
        // The timeout is counted from the block now, not from when the review was prepared, and the
        // same moment confirms the account has sent nothing since: the reviewed sequence is what makes
        // this transfer impossible to include twice, so it is never quietly replaced.
        var (_, latestBody) = await GetAsync($"{quote.Server}/cosmos/base/tendermint/v1beta1/blocks/latest", ct);
        var latest = latestBody is { } lb ? CosmosSendRules.ParseLatestBlock(lb) : null;
        if (latest is not { } block || block.ChainId != CosmosTransactions.HubChainId)
            return new AtomSendOutcome(CosmosSubmitOutcome.Rejected, null, "Could not read the chain's latest block just before signing. Nothing was sent.");

        var (accountStatus, accountBody) = await GetAsync($"{quote.Server}/cosmos/auth/v1beta1/accounts/{quote.From}", ct);
        var (_, account) = CosmosSendRules.ParseAccount(accountStatus, accountBody);
        if (account is null)
            return new AtomSendOutcome(CosmosSubmitOutcome.Rejected, null, "Could not read the account just before signing. Nothing was sent.");
        if (account.Sequence != quote.Sequence || account.AccountNumber != quote.AccountNumber)
            return new AtomSendOutcome(CosmosSubmitOutcome.Rejected, null, "This account has sent something since the review. Review the transfer again. Nothing was sent.");

        var timeout = block.Height + CosmosSendRules.TimeoutBlocks;
        var send = new CosmosSend(quote.From, quote.To, quote.Micro, CosmosHub.Denom, quote.Memo, timeout, quote.PublicKey,
            quote.AccountNumber, quote.Sequence, quote.FeeMicro, quote.GasLimit, CosmosTransactions.HubChainId);

        byte[] tx;
        string hash;
        try
        {
            (tx, hash) = CosmosTransactions.Sign(send, key);
        }
        catch (Exception ex)
        {
            return new AtomSendOutcome(CosmosSubmitOutcome.Rejected, null, $"Could not sign the transfer: {ex.Message}");
        }

        // No answer (status 0) reads as Unknown: the transaction may have reached the node.
        var (submitStatus, submitBody) = await PostAsync(Submit, $"{quote.Server}/cosmos/tx/v1beta1/txs",
            new { tx_bytes = Convert.ToBase64String(tx), mode = "BROADCAST_MODE_SYNC" }, ct);
        var broadcast = CosmosSendRules.ParseBroadcast(submitStatus, submitBody);

        if (broadcast.Outcome == CosmosSubmitOutcome.Rejected)
            return new AtomSendOutcome(CosmosSubmitOutcome.Rejected, null, broadcast.Reason);

        // In the mempool, or no answer at all: ask about THIS transaction until a block settles it —
        // included, failed, or past its timeout height and never included.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(4), ct);
            var (status, body) = await GetAsync($"{quote.Server}/cosmos/tx/v1beta1/txs/{hash}", ct);
            var (outcome, reason) = CosmosSendRules.ParseLookup(status, body);
            if (outcome is CosmosSubmitOutcome.Included or CosmosSubmitOutcome.FailedFeeCharged)
                return new AtomSendOutcome(outcome, hash, reason);

            if (outcome == CosmosSubmitOutcome.Pending)
            {
                var (_, nowBody) = await GetAsync($"{quote.Server}/cosmos/base/tendermint/v1beta1/blocks/latest", ct);
                if (nowBody is { } nb && CosmosSendRules.ParseLatestBlock(nb) is { } now && now.Height > timeout + 1)
                {
                    return new AtomSendOutcome(CosmosSubmitOutcome.Rejected, hash,
                        "The network did not include the transfer before its timeout height. Nothing was sent.");
                }
            }
        }

        return new AtomSendOutcome(CosmosSubmitOutcome.Unknown, hash,
            $"The network has not confirmed the transfer yet. It can only be included up to block {timeout} — about five " +
            $"minutes after it was sent — and never after. Check {hash} on an explorer before sending again.");
    }

    private static string NotEnough(ulong balance, ulong needed) =>
        $"Not enough ATOM. Available: {Atom(balance)} ATOM (staked ATOM is not included). Needed: {Atom(needed)} ATOM including the fee.";

    private static string Atom(ulong micro) =>
        CosmosTransactions.ToAtom(micro).ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>GET: the status (0 when the node did not answer) and the JSON body when there was one.</summary>
    private static async Task<(int Status, JsonElement? Body)> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var res = await ExplorerHttp.GetAsync(Read, url, ct);
            return ((int)res.StatusCode, Parse(await res.Content.ReadAsStringAsync(ct)));
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

    /// <summary>POST: the status (0 when there was no answer — which for a broadcast means it may or
    /// may not have arrived) and the JSON body when there was one.</summary>
    private static async Task<(int Status, JsonElement? Body)> PostAsync(HttpClient http, string url, object request, CancellationToken ct)
    {
        try
        {
            using var res = await http.PostAsJsonAsync(url, request, ct);
            return ((int)res.StatusCode, Parse(await res.Content.ReadAsStringAsync(ct)));
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

    private static JsonElement? Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
