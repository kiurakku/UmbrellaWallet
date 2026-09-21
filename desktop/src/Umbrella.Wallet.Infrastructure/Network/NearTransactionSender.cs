using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>A NEAR transfer as reviewed: everything <see cref="NearTransactionSender.SignAndBroadcastAsync"/> will sign.</summary>
public sealed record NearSendQuote(
    string From,
    byte[] PublicKey,
    string To,
    decimal AmountNear,
    BigInteger Yocto,
    bool CreatesAccount,
    ulong Nonce,
    byte[] BlockHash,
    string Rpc)
{
    public decimal FeeCapNear => NearTransactions.ToNear(NearRpc.FeeReserveYocto);
}

public sealed record NearSendOutcome(NearSubmitOutcome Outcome, string? Hash, string? Message);

/// <summary>
/// Real NEAR transfers from the wallet's implicit account (roadmap N.7). Reads the account, its access
/// key and the destination from the RPC, builds the transaction with <see cref="NearTransactions"/> —
/// pinned byte-for-byte to near-api-js — signs it locally and submits it once.
///
/// The RPC is the user's chosen server, or the listed ones in order, all through
/// <see cref="PublicHttp"/>. An unreadable account, key, nonce or destination stops the send before
/// anything is signed.
/// </summary>
public sealed class NearTransactionSender
{
    private static HttpClient Read => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);
    private static HttpClient Submit => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    private const string DefaultRpc = "https://rpc.mainnet.near.org";

    /// <summary>What a new implicit account must receive to pay for its own storage (about 0.00182 NEAR);
    /// a smaller first transfer fails on chain.</summary>
    public static readonly BigInteger MinimumNewImplicitYocto = BigInteger.Pow(10, 21) * 2;   // 0.002 NEAR

    public async Task<(NearSendQuote? Quote, string? Error)> PrepareAsync(
        string from, byte[] publicKey, string to, decimal amountNear, CancellationToken ct = default)
    {
        to = to.Trim();
        if (!NearTransactions.IsValidAccountId(to))
            return (null, "That is not a valid NEAR account (for example alice.near, or a 64-character implicit account).");
        if (string.Equals(to, from, StringComparison.Ordinal)) return (null, "That is this wallet's own NEAR account.");
        if (!NearTransactions.TryToYocto(amountNear, out var yocto)) return (null, "Enter a positive NEAR amount.");

        foreach (var rpc in ChainEndpoints.Candidates("NEAR", DefaultRpc))
        {
            var host = new Uri(rpc).Host;

            var account = await QueryAsync(rpc, new { request_type = "view_account", finality = "final", account_id = from }, ct);
            if (account is null) continue;   // this server did not answer; try the next
            if (NearRpc.IsUnknownAccount(account.Value))
                return (null, "This NEAR account has not received anything yet, so it does not exist on chain and cannot send.");
            var state = NearRpc.ParseViewAccount(account.Value);
            if (state is null) return (null, $"{host} answered with account data this wallet does not understand. Nothing was sent.");

            var key = await QueryAsync(rpc, new
            {
                request_type = "view_access_key", finality = "final", account_id = from,
                public_key = NearTransactions.PublicKeyString(publicKey),
            }, ct);
            var access = key is { } k ? NearRpc.ParseAccessKey(k) : null;
            if (access is null) return (null, $"Could not read this account's key from {host}. Nothing was sent.");

            var dest = await QueryAsync(rpc, new { request_type = "view_account", finality = "final", account_id = to }, ct);
            if (dest is null) return (null, $"Could not check the destination on {host}. Nothing was sent.");
            var exists = NearRpc.ParseViewAccount(dest.Value) is not null;
            var missing = !exists && NearRpc.IsUnknownAccount(dest.Value);
            if (!exists && !missing) return (null, $"{host} gave an answer about the destination this wallet does not understand. Nothing was sent.");

            if (missing && !NearTransactions.IsImplicit(to))
            {
                return (null, to.StartsWith("0x", StringComparison.Ordinal)
                    ? "That 0x… NEAR account does not exist yet, and creating one is not supported here. Nothing was sent."
                    : $"The NEAR account \"{to}\" does not exist. A transfer to it would fail. Check the name.");
            }

            if (missing && yocto < MinimumNewImplicitYocto)
                return (null, "The destination has never received anything. The first transfer to it creates it and must be at least 0.002 NEAR, which pays for its storage.");

            var needed = yocto + NearRpc.FeeReserveYocto;
            if (needed > state.Spendable)
            {
                return (null,
                    $"Not enough NEAR. Spendable: {Near(state.Spendable)} NEAR (the balance minus the storage this account must keep paid for). " +
                    $"Needed: {Near(needed)} NEAR including up to {Near(NearRpc.FeeReserveYocto)} NEAR of gas.");
            }

            return (new NearSendQuote(from, publicKey, to, amountNear, yocto, missing, access.Value.Nonce + 1, access.Value.BlockHash, rpc), null);
        }

        return (null, "No NEAR server answered. Check your connection (or Tor). Nothing was sent.");
    }

    public async Task<NearSendOutcome> SignAndBroadcastAsync(NearSendQuote quote, byte[] seed, CancellationToken ct = default)
    {
        var transfer = new NearTransfer(quote.From, quote.PublicKey, quote.Nonce, quote.To, quote.BlockHash, quote.Yocto);

        string signed, hash;
        try
        {
            (signed, hash, _) = NearTransactions.Sign(transfer, seed);
        }
        catch (Exception ex)
        {
            return new NearSendOutcome(NearSubmitOutcome.Rejected, null, $"Could not sign the transaction: {ex.Message}");
        }

        NearSubmitResult result;
        try
        {
            using var res = await Submit.PostAsJsonAsync(quote.Rpc, new
            {
                jsonrpc = "2.0", id = "umbrella", method = "send_tx",
                @params = new { signed_tx_base64 = signed, wait_until = "EXECUTED_OPTIMISTIC" },
            }, ct);
            result = NearSubmit.Parse(await res.Content.ReadAsStringAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            result = new NearSubmitResult(NearSubmitOutcome.Unknown, null);   // it may have reached the RPC
        }

        if (result.Outcome == NearSubmitOutcome.Included) return new NearSendOutcome(NearSubmitOutcome.Included, hash, null);
        if (result.Outcome == NearSubmitOutcome.Rejected) return new NearSendOutcome(NearSubmitOutcome.Rejected, hash, result.Reason);

        // No answer either way: ask about THIS transaction for a while before saying so.
        for (var i = 0; i < 6; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            try
            {
                using var res = await Read.PostAsJsonAsync(quote.Rpc, new
                {
                    jsonrpc = "2.0", id = "umbrella", method = "tx",
                    @params = new { tx_hash = hash, sender_account_id = quote.From, wait_until = "EXECUTED_OPTIMISTIC" },
                }, ct);
                var status = NearSubmit.Parse(await res.Content.ReadAsStringAsync(ct));
                if (status.Outcome != NearSubmitOutcome.Unknown)
                    return new NearSendOutcome(status.Outcome, hash, status.Reason);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // keep asking until the loop ends
            }
        }

        return new NearSendOutcome(NearSubmitOutcome.Unknown, hash,
            $"The network did not confirm the transaction in time. A NEAR transaction can still be included for about a day. " +
            $"Check {hash} on an explorer before sending again — a new send could pay twice.");
    }

    private static string Near(BigInteger yocto) =>
        NearTransactions.ToNear(yocto).ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>An RPC <c>query</c>. Null when the server did not answer at all; the JSON otherwise,
    /// including an error object (which may be a definite "does not exist").</summary>
    private static async Task<JsonElement?> QueryAsync(string rpc, object parameters, CancellationToken ct)
    {
        try
        {
            using var res = await Read.PostAsJsonAsync(rpc, new { jsonrpc = "2.0", id = "umbrella", method = "query", @params = parameters }, ct);
            if (!res.IsSuccessStatusCode) return null;
            var text = await res.Content.ReadAsStringAsync(ct);
            return JsonDocument.Parse(text).RootElement.Clone();
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
}
