using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using Umbrella.Wallet.Core.Polkadot;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>A DOT transfer as reviewed: everything <see cref="PolkadotTransactionSender.SignAndBroadcastAsync"/> will sign.</summary>
public sealed record DotSendQuote(
    string From,
    byte[] FromKey,
    string To,
    byte[] ToKey,
    decimal AmountDot,
    BigInteger Planck,
    BigInteger FeePlanck,
    bool CreatesAccount,
    BigInteger ExistentialDeposit,
    uint Nonce,
    string Server)
{
    public decimal FeeDot => PolkadotSendRules.ToDot(FeePlanck);
    public decimal ExistentialDepositDot => PolkadotSendRules.ToDot(ExistentialDeposit);
}

/// <summary>How a DOT send ended, in the terms the Send screen needs.</summary>
public sealed record DotSendOutcome(DotSubmitOutcome Outcome, string? Hash, string? Message);

/// <summary>
/// Real DOT transfers on Polkadot Asset Hub (roadmap N.8, send). The running runtime's metadata says
/// how a transfer is built (<see cref="PolkadotTransactions"/>); the account, its nonce, the existential
/// deposit and the fee come from the node; the transfer is signed with sr25519 on this PC, validated by
/// the node before it is broadcast, submitted once, and followed through finalized blocks until its
/// events say it succeeded or failed — or its era ends without it.
/// </summary>
public sealed class PolkadotTransactionSender
{
    private static HttpClient Read => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);
    private static HttpClient Submit => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    private const string DefaultServer = "https://polkadot-asset-hub-rpc.polkadot.io";
    private const string SystemEventsKey = "0x26aa394eea5630e07c48ae0c9558cef780d41e5e16056765bc8461851072c9d7";

    private static readonly SemaphoreSlim MetadataLock = new(1, 1);
    private static (uint Spec, RuntimeMetadata Metadata)? _metadata;

    public async Task<(DotSendQuote? Quote, string? Error)> PrepareAsync(string from, string to, decimal amountDot, CancellationToken ct = default)
    {
        to = to.Trim();
        if (!Ss58.TryDecode(to, out var toPrefix, out var toKey) || toPrefix != Ss58.PolkadotPrefix)
            return (null, "That is not a Polkadot address (it should start with 1).");
        if (!Ss58.TryDecode(from, out _, out var fromKey)) return (null, "This wallet's Polkadot address could not be read. Nothing was sent.");
        if (toKey.AsSpan().SequenceEqual(fromKey)) return (null, "That is this wallet's own Polkadot address.");
        if (!PolkadotSendRules.TryToPlanck(amountDot, out var planck))
            return (null, "Enter a positive amount with at most 10 decimal places.");

        foreach (var server in ChainEndpoints.Candidates("DOT", DefaultServer))
        {
            var host = new Uri(server).Host;
            var chain = await ChainStateAsync(server, ct);
            if (chain is null) continue;   // this node did not answer; try the next
            var (md, spec, txVersion, genesis, head, headNumber) = chain.Value;

            if (PolkadotTransactions.CheckExtensions(md) is { } changed)
                return (null, $"Polkadot changed how transactions are built ({changed}). This wallet will not guess — nothing was sent.");
            if (!md.PalletNamed("Balances")!.Constants.TryGetValue("ExistentialDeposit", out var edBytes) || edBytes.Length != 16)
                return (null, $"Could not read the existential deposit from {host}. Nothing was sent.");
            var ed = new BigInteger(edBytes, isUnsigned: true, isBigEndian: false);

            var (fromExists, account) = await AccountAsync(server, fromKey, head, ct);
            if (account is null) return (null, $"Could not read this account from {host}. Nothing was sent.");
            if (!fromExists) return (null, "This address holds no DOT on Asset Hub, where Polkadot balances now live.");

            var (toExists, toAccount) = await AccountAsync(server, toKey, head, ct);
            if (toAccount is null) return (null, $"Could not check the destination on {host}. Nothing was sent.");
            if (!toExists && planck < ed)
            {
                return (null,
                    $"The destination holds no DOT yet, so the first transfer to it must be at least {Dot(ed)} DOT — " +
                    "the existential deposit an account needs to exist.");
            }

            var (nonceResult, _) = await RpcAsync(server, "system_accountNextIndex", [from], ct);
            if (nonceResult is not { ValueKind: JsonValueKind.Number } n || !n.TryGetUInt32(out var nonce))
                return (null, $"Could not read this account's next nonce from {host}. Nothing was sent.");

            // The fee, for exactly this transfer (with an empty signature — a fee does not depend on it).
            var draft = new DotTransfer(fromKey, toKey, planck, nonce, spec, txVersion, genesis, headNumber, head);
            if (PolkadotTransactions.UnsignedForFee(md, draft) is not { } unsigned)
                return (null, "Polkadot changed how transfers are built. Nothing was sent.");
            var (feeHex, _) = await RpcAsync(server, "state_call",
                ["TransactionPaymentApi_query_info", Hex([.. unsigned, .. Scale.U32((uint)unsigned.Length)])], ct);
            if (PolkadotSendRules.ParseQueryInfoFee(feeHex?.GetString()) is not { } fee)
                return (null, $"Could not get the fee from {host}. Nothing was sent.");

            var spendable = account.Spendable(ed);
            var needed = planck + fee + (fee / 10);   // a tenth more fee: it can move between review and block
            if (needed > spendable)
            {
                return (null,
                    $"Not enough DOT on Asset Hub. Spendable: {Dot(spendable)} DOT (the balance less the {Dot(ed)} DOT " +
                    $"an account must keep, and anything locked). Needed: {Dot(needed)} DOT including the fee.");
            }

            return (new DotSendQuote(from, fromKey, to, toKey, amountDot, planck, fee, !toExists, ed, nonce, server), null);
        }

        return (null, "No Polkadot Asset Hub node answered. Check your connection (or Tor). Nothing was sent.");
    }

    public async Task<DotSendOutcome> SignAndBroadcastAsync(DotSendQuote quote, Sr25519.Keypair key, CancellationToken ct = default)
    {
        // The era is counted from the block now, and the nonce is read again: it is what makes this
        // transfer impossible to include twice, so it is never quietly replaced.
        var chain = await ChainStateAsync(quote.Server, ct);
        if (chain is null) return new DotSendOutcome(DotSubmitOutcome.Rejected, null, "Could not read the chain just before signing. Nothing was sent.");
        var (md, spec, txVersion, genesis, head, headNumber) = chain.Value;

        var (nonceResult, _) = await RpcAsync(quote.Server, "system_accountNextIndex", [quote.From], ct);
        if (nonceResult is not { ValueKind: JsonValueKind.Number } n || !n.TryGetUInt32(out var nonce))
            return new DotSendOutcome(DotSubmitOutcome.Rejected, null, "Could not read the account's nonce just before signing. Nothing was sent.");
        if (nonce != quote.Nonce)
            return new DotSendOutcome(DotSubmitOutcome.Rejected, null, "This account has sent something since the review. Review the transfer again. Nothing was sent.");

        byte[] extrinsic, hash;
        try
        {
            (extrinsic, hash) = PolkadotTransactions.Sign(md,
                new DotTransfer(quote.FromKey, quote.ToKey, quote.Planck, nonce, spec, txVersion, genesis, headNumber, head), key);
        }
        catch (Exception ex)
        {
            return new DotSendOutcome(DotSubmitOutcome.Rejected, null, $"Could not sign the transfer: {ex.Message}");
        }

        var hashHex = "0x" + Convert.ToHexString(hash).ToLowerInvariant();

        // The node checks it first — the signature, the era, the nonce, the fee — without broadcasting.
        var (validity, _) = await RpcAsync(quote.Server, "state_call",
            ["TaggedTransactionQueue_validate_transaction", Hex([0x02, .. extrinsic, .. head])], ct);
        var (valid, reason) = PolkadotSendRules.ParseValidity(validity?.GetString());
        if (!valid) return new DotSendOutcome(DotSubmitOutcome.Rejected, null, $"{reason} Nothing was sent.");

        var (submitted, refused) = await RpcAsync(quote.Server, "author_submitExtrinsic", [Hex(extrinsic)], ct, broadcast: true);
        if (refused is not null) return new DotSendOutcome(DotSubmitOutcome.Rejected, null, $"The node refused the transfer: {refused}");
        _ = submitted;   // its answer is the same hash; no answer at all is followed up below just the same

        // Follow finalized blocks from the era's first until the transfer is found or the era is over.
        var lastEraBlock = headNumber + PolkadotTransactions.EraPeriod;
        var scanned = headNumber;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(6), ct);
            var (finalizedHash, _) = await RpcAsync(quote.Server, "chain_getFinalizedHead", [], ct);
            if (finalizedHash?.GetString() is not { } fh) continue;
            var (finalizedHeader, _) = await RpcAsync(quote.Server, "chain_getHeader", [fh], ct);
            if (BlockNumber(finalizedHeader) is not { } finalized) continue;

            for (var number = scanned + 1; number <= finalized && number <= lastEraBlock; number++)
            {
                var (blockHash, _) = await RpcAsync(quote.Server, "chain_getBlockHash", [number], ct);
                var (block, _) = await RpcAsync(quote.Server, "chain_getBlock", [blockHash?.GetString() ?? ""], ct);
                if (blockHash is null || block is null) goto nextAttempt;   // retry this block next round

                var extrinsics = block.Value.GetProperty("block").GetProperty("extrinsics").EnumerateArray().Select(e => e.GetString() ?? "");
                var index = PolkadotSendRules.IndexIn(extrinsics, extrinsic);
                scanned = number;
                if (index < 0) continue;

                var (events, _) = await RpcAsync(quote.Server, "state_getStorage", [SystemEventsKey, blockHash.Value.GetString()!], ct);
                return (events?.GetString() is { } ev ? PolkadotSendRules.ExtrinsicSucceeded(md, ev, index) : null) switch
                {
                    true => new DotSendOutcome(DotSubmitOutcome.Included, hashHex, null),
                    false => new DotSendOutcome(DotSubmitOutcome.FailedFeeCharged, hashHex,
                        "The transfer was included in a block but failed; only the fee was charged."),
                    null => new DotSendOutcome(DotSubmitOutcome.Unknown, hashHex,
                        $"The transfer is in block {number}, but its result could not be read. Check {hashHex} on an explorer."),
                };
            }

            if (scanned >= lastEraBlock)
                return new DotSendOutcome(DotSubmitOutcome.Rejected, hashHex, "The network did not include the transfer before its era ended. Nothing was sent.");
            nextAttempt:;
        }

        return new DotSendOutcome(DotSubmitOutcome.Unknown, hashHex,
            $"The network has not finalized the transfer yet. It can only be included up to block {lastEraBlock} — about " +
            $"six minutes after it was signed — and never after. Check {hashHex} on an explorer before sending again.");
    }

    /// <summary>Metadata (cached per runtime version), versions, genesis, and the finalized head.</summary>
    private static async Task<(RuntimeMetadata Md, uint Spec, uint TxVersion, byte[] Genesis, byte[] Head, uint HeadNumber)?> ChainStateAsync(
        string server, CancellationToken ct)
    {
        var (version, _) = await RpcAsync(server, "state_getRuntimeVersion", [], ct);
        if (version is not { ValueKind: JsonValueKind.Object } v ||
            !v.TryGetProperty("specVersion", out var sv) || !sv.TryGetUInt32(out var spec) ||
            !v.TryGetProperty("transactionVersion", out var tv) || !tv.TryGetUInt32(out var txVersion))
            return null;

        var md = await MetadataAsync(server, spec, ct);
        if (md is null) return null;

        var (genesisHash, _) = await RpcAsync(server, "chain_getBlockHash", [0], ct);
        var (headHash, _) = await RpcAsync(server, "chain_getFinalizedHead", [], ct);
        if (genesisHash?.GetString() is not { } g || headHash?.GetString() is not { } h) return null;
        var (header, _) = await RpcAsync(server, "chain_getHeader", [h], ct);
        if (BlockNumber(header) is not { } number) return null;

        return (md, spec, txVersion, Convert.FromHexString(g[2..]), Convert.FromHexString(h[2..]), number);
    }

    private static async Task<RuntimeMetadata?> MetadataAsync(string server, uint spec, CancellationToken ct)
    {
        if (_metadata is { } cached && cached.Spec == spec) return cached.Metadata;
        await MetadataLock.WaitAsync(ct);
        try
        {
            if (_metadata is { } again && again.Spec == spec) return again.Metadata;
            var (hex, _) = await RpcAsync(server, "state_getMetadata", [], ct);
            if (hex?.GetString() is not { } text || !text.StartsWith("0x", StringComparison.Ordinal)) return null;
            var md = RuntimeMetadata.Parse(Convert.FromHexString(text[2..]));
            _metadata = (spec, md);
            return md;
        }
        catch (FormatException)
        {
            return null;
        }
        finally
        {
            MetadataLock.Release();
        }
    }

    private static async Task<(bool Exists, DotAccount? Account)> AccountAsync(string server, byte[] accountId, byte[] at, CancellationToken ct)
    {
        var (result, error) = await RpcAsync(server, "state_getStorage", [PolkadotAccounts.SystemAccountKey(accountId), Hex(at)], ct);
        if (error is not null) return (false, null);
        if (result is null) return (false, null);   // no answer
        return result.Value.ValueKind == JsonValueKind.Null
            ? PolkadotSendRules.ParseAccount(null, missing: true)
            : PolkadotSendRules.ParseAccount(result.Value.GetString(), missing: false);
    }

    private static uint? BlockNumber(JsonElement? header) =>
        header is { ValueKind: JsonValueKind.Object } hd && hd.TryGetProperty("number", out var num) &&
        num.GetString() is { } s && s.StartsWith("0x", StringComparison.Ordinal) &&
        uint.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;

    /// <summary>A JSON-RPC call: its result (a JSON null result is returned as a null-valued element),
    /// its error message, or neither when the node did not answer.</summary>
    private static async Task<(JsonElement? Result, string? Error)> RpcAsync(
        string server, string method, object[] parameters, CancellationToken ct, bool broadcast = false)
    {
        try
        {
            using var res = await (broadcast ? Submit : Read).PostAsJsonAsync(server,
                new { jsonrpc = "2.0", id = 1, method, @params = parameters }, ct);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("error", out var err))
                return (null, err.TryGetProperty("message", out var m) ? m.GetString() : "The node refused the request.");
            return doc.RootElement.TryGetProperty("result", out var result) ? (result.Clone(), null) : (null, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (null, null);
        }
    }

    private static string Hex(byte[] bytes) => "0x" + Convert.ToHexString(bytes).ToLowerInvariant();

    private static string Dot(BigInteger planck) =>
        PolkadotSendRules.ToDot(planck).ToString("0.##########", CultureInfo.InvariantCulture);
}
