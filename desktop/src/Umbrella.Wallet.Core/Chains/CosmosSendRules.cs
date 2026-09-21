using System.Globalization;
using System.Text.Json;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>A Cosmos account as read before a send: the numbers its signature must name.</summary>
public sealed record CosmosAccountState(ulong AccountNumber, ulong Sequence, byte[]? PublicKey);

/// <summary>What an account lookup said.</summary>
public enum CosmosAccountLookup
{
    Found,
    /// <summary>The chain has never seen the address: it has never received anything.</summary>
    NotFound,
    /// <summary>A vesting or module account: not something this wallet signs for.</summary>
    Unsupported,
    Unreadable,
}

/// <summary>Where a broadcast Cosmos transaction stands.</summary>
public enum CosmosSubmitOutcome
{
    /// <summary>In a block and successful: final.</summary>
    Included,
    /// <summary>Refused before any block — or past its timeout height and never included. Nothing was sent.</summary>
    Rejected,
    /// <summary>In a block but failed: final, only the fee was charged.</summary>
    FailedFeeCharged,
    /// <summary>Accepted into the mempool; not in a block yet.</summary>
    Pending,
    /// <summary>No answer either way.</summary>
    Unknown,
}

/// <summary>
/// Reading Cosmos SDK REST answers for a send (roadmap N.6). Kept apart from the network so each
/// decision is tested offline on the answers a Hub node gives today. Anything unreadable is unknown,
/// never a default — a guessed account number or sequence signs a transaction no node will take.
/// </summary>
public static class CosmosSendRules
{
    private const string BaseAccountType = "/cosmos.auth.v1beta1.BaseAccount";
    private const string PubKeyType = "/cosmos.crypto.secp256k1.PubKey";

    /// <summary>How many blocks a signed transfer stays valid for — about five minutes. After that it
    /// can never be included, which is what settles a broadcast whose answer never came.</summary>
    public const ulong TimeoutBlocks = 50;

    /// <summary>The most this wallet pays in fees without the network being plainly congested: 0.05 ATOM.</summary>
    public const ulong MaxFeeMicro = 50_000;

    /// <summary><c>/cosmos/auth/v1beta1/accounts/{address}</c>.</summary>
    public static (CosmosAccountLookup Lookup, CosmosAccountState? State) ParseAccount(int status, JsonElement? body)
    {
        if (body is not { ValueKind: JsonValueKind.Object } root) return (CosmosAccountLookup.Unreadable, null);

        // gRPC NotFound (code 5), which the gateway sends with a 404.
        if (status == 404 && Code(root) == 5) return (CosmosAccountLookup.NotFound, null);
        if (status != 200 || !root.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object)
            return (CosmosAccountLookup.Unreadable, null);

        if (Str(account, "@type") != BaseAccountType) return (CosmosAccountLookup.Unsupported, null);

        if (!ULong(account, "account_number", out var number) || !ULong(account, "sequence", out var sequence))
            return (CosmosAccountLookup.Unreadable, null);

        byte[]? key = null;
        if (account.TryGetProperty("pub_key", out var pk) && pk.ValueKind == JsonValueKind.Object)
        {
            if (Str(pk, "@type") != PubKeyType) return (CosmosAccountLookup.Unsupported, null);
            try { key = Convert.FromBase64String(Str(pk, "key") ?? ""); }
            catch (FormatException) { return (CosmosAccountLookup.Unreadable, null); }
            if (key.Length != 33) return (CosmosAccountLookup.Unreadable, null);
        }

        return (CosmosAccountLookup.Found, new CosmosAccountState(number, sequence, key));
    }

    /// <summary>The chain id a node says it is on (<c>/cosmos/base/tendermint/v1beta1/node_info</c>).</summary>
    public static string? ParseNodeNetwork(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object &&
        body.TryGetProperty("default_node_info", out var info) && info.ValueKind == JsonValueKind.Object
            ? Str(info, "network")
            : null;

    /// <summary>The latest block's height and chain id (<c>…/blocks/latest</c>).</summary>
    public static (ulong Height, string? ChainId)? ParseLatestBlock(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object ||
            !body.TryGetProperty("block", out var block) || block.ValueKind != JsonValueKind.Object ||
            !block.TryGetProperty("header", out var header) || header.ValueKind != JsonValueKind.Object ||
            !ULong(header, "height", out var height) || height == 0)
            return null;
        return (height, Str(header, "chain_id"));
    }

    /// <summary>The fee market's current price of gas in uatom (<c>/feemarket/v1/gas_price/uatom</c>).</summary>
    public static decimal? ParseGasPrice(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object ||
            !body.TryGetProperty("price", out var price) || price.ValueKind != JsonValueKind.Object ||
            Str(price, "denom") != CosmosHub.Denom)
            return null;
        return decimal.TryParse(Str(price, "amount"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var p) && p > 0
            ? p
            : null;
    }

    /// <summary>The gas a simulation used (<c>/cosmos/tx/v1beta1/simulate</c>), or the node's reason for
    /// refusing it — which is the reason the real transaction would be refused too.</summary>
    public static (ulong? GasUsed, string? Error) ParseSimulation(int status, JsonElement? body)
    {
        if (body is not { ValueKind: JsonValueKind.Object } root) return (null, null);
        if (status == 200 &&
            root.TryGetProperty("gas_info", out var gas) && gas.ValueKind == JsonValueKind.Object &&
            ULong(gas, "gas_used", out var used) && used > 0)
            return (used, null);
        return (null, Str(root, "message"));
    }

    /// <summary>The gas limit and fee for a simulated amount of gas at a price: 30% more gas than the
    /// simulation used, and half again the current price, both rounded up. The fee market can move
    /// between the simulation and the block; a transaction that runs out of gas still pays.</summary>
    public static (ulong GasLimit, ulong Fee) FeeFor(ulong gasUsed, decimal gasPrice)
    {
        var limit = (ulong)Math.Ceiling(gasUsed * 1.3m);
        var fee = (ulong)Math.Ceiling(limit * gasPrice * 1.5m);
        return (limit, Math.Max(fee, 1));
    }

    /// <summary>A SYNC broadcast: code 0 means the mempool took it (not final); anything else means it
    /// was refused before any block and nothing was charged.</summary>
    public static (CosmosSubmitOutcome Outcome, string? Hash, string? Reason) ParseBroadcast(int status, JsonElement? body)
    {
        if (status != 200 || body is not { ValueKind: JsonValueKind.Object } root ||
            !root.TryGetProperty("tx_response", out var r) || r.ValueKind != JsonValueKind.Object ||
            !r.TryGetProperty("code", out var c) || !c.TryGetUInt32(out var code))
            return (CosmosSubmitOutcome.Unknown, null, null);

        var hash = Str(r, "txhash");
        return code == 0
            ? (CosmosSubmitOutcome.Pending, hash, null)
            : (CosmosSubmitOutcome.Rejected, hash, $"The network refused the transaction ({Str(r, "codespace")} {code}): {Str(r, "raw_log")}");
    }

    /// <summary>A lookup of THIS transaction (<c>/cosmos/tx/v1beta1/txs/{hash}</c>): in a block and
    /// successful, in a block and failed, or not found (yet).</summary>
    public static (CosmosSubmitOutcome Outcome, string? Reason) ParseLookup(int status, JsonElement? body)
    {
        if (body is not { ValueKind: JsonValueKind.Object } root) return (CosmosSubmitOutcome.Unknown, null);
        if (status == 404 && Code(root) == 5) return (CosmosSubmitOutcome.Pending, null);   // not in a block (yet)
        if (status != 200 || !root.TryGetProperty("tx_response", out var r) || r.ValueKind != JsonValueKind.Object ||
            !ULong(r, "height", out var height) || height == 0 ||
            !r.TryGetProperty("code", out var c) || !c.TryGetUInt32(out var code))
            return (CosmosSubmitOutcome.Unknown, null);

        return code == 0
            ? (CosmosSubmitOutcome.Included, null)
            : (CosmosSubmitOutcome.FailedFeeCharged, $"The transaction was included but failed ({Str(r, "raw_log")}); only the fee was charged.");
    }

    private static int? Code(JsonElement o) =>
        o.TryGetProperty("code", out var c) && c.TryGetInt32(out var v) ? v : null;

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>A uint64 the SDK writes as a decimal string.</summary>
    private static bool ULong(JsonElement o, string name, out ulong value)
    {
        value = 0;
        return ulong.TryParse(Str(o, name), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
