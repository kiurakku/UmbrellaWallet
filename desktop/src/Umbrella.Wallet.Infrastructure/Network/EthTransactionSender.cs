using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using Nethereum.Signer;

namespace Umbrella.Wallet.Infrastructure.Network;

public sealed record EthSendQuote(
    string From,
    string To,
    decimal AmountEth,
    BigInteger AmountWei,
    BigInteger Nonce,
    BigInteger GasPriceWei,
    decimal MaxFeeEth,
    string Rpc,
    long ChainId = 1,
    string Symbol = "ETH",
    IReadOnlyList<string>? Rpcs = null,
    // Contract-call fields, set only for a THORChain router deposit (ETH-from swap). Data is the
    // encoded depositWithExpiry calldata; GasLimit covers the contract call, not a 21k transfer.
    string? Data = null,
    long GasLimit = 21_000);

public sealed record EthSendResult(bool Ok, string? TxHash, string? Error);

/// <summary>An EVM network the wallet can send native coin on, sharing the same 0x address as Ethereum.</summary>
public sealed record EvmChain(string Symbol, string Name, long ChainId, string ExplorerTx, IReadOnlyList<string> Rpcs);

/// <summary>
/// Real Ethereum mainnet send: nonce + gas price come from public RPCs, the transaction is
/// signed LOCALLY (EIP-155, chainId 1) with the key derived from the vault mnemonic, and only
/// the signed raw bytes are broadcast. The private key never leaves the process.
/// The signer is pinned to the official EIP-155 test vector in the test suite.
/// </summary>
public sealed class EthTransactionSender
{
    private const long ChainId = 1;
    private const long TransferGasLimit = 21_000;

    private static readonly string[] Rpcs =
    [
        "https://cloudflare-eth.com",
        "https://rpc.ankr.com/eth",
        "https://eth.drpc.org",
    ];

    /// <summary>The EVM networks a native send is supported on (same 0x address, EIP-155 signing).</summary>
    public static readonly IReadOnlyDictionary<string, EvmChain> Chains =
        new Dictionary<string, EvmChain>(StringComparer.OrdinalIgnoreCase)
        {
            ["ETH"] = new("ETH", "Ethereum", 1, "etherscan.io/tx/", Rpcs),
            ["BNB"] = new("BNB", "BSC", 56, "bscscan.com/tx/",
                ["https://bsc-dataseed.binance.org", "https://bsc-dataseed1.defibit.io", "https://rpc.ankr.com/bsc"]),
            ["MATIC"] = new("MATIC", "Polygon", 137, "polygonscan.com/tx/",
                ["https://polygon-rpc.com", "https://rpc.ankr.com/polygon"]),
            ["AVAX"] = new("AVAX", "Avalanche", 43114, "snowtrace.io/tx/",
                ["https://api.avax.network/ext/bc/C/rpc", "https://rpc.ankr.com/avalanche"]),
            ["FTM"] = new("FTM", "Fantom", 250, "ftmscan.com/tx/",
                ["https://rpc.ftm.tools", "https://rpc.ankr.com/fantom"]),
            ["CRO"] = new("CRO", "Cronos", 25, "cronoscan.com/tx/",
                ["https://evm.cronos.org", "https://cronos-evm-rpc.publicnode.com"]),
            // Ethereum L2 rollups. Their native coin IS ETH (not a separate token), and they share the
            // SAME 0x address and key as Ethereum — so the coin Symbol stays "ETH" and only the network,
            // chain id and explorer differ. Keyed by the network (ARB/BASE/OP), never by "ETH", so the
            // Ethereum-mainnet entry above is untouched. Signing is identical EIP-155 with the chain id.
            ["ARB"] = new("ETH", "Arbitrum One", 42161, "arbiscan.io/tx/",
                ["https://arb1.arbitrum.io/rpc", "https://rpc.ankr.com/arbitrum"]),
            ["BASE"] = new("ETH", "Base", 8453, "basescan.org/tx/",
                ["https://mainnet.base.org", "https://base.publicnode.com"]),
            ["OP"] = new("ETH", "Optimism", 10, "optimistic.etherscan.io/tx/",
                ["https://mainnet.optimism.io", "https://rpc.ankr.com/optimism"]),
        };

    /// <summary>The explorer-tx base for a chain id (used at confirm time, when a quote carries only its
    /// chain id — the L2 rollups all report Symbol "ETH", so they can't be told apart by symbol).</summary>
    public static string ExplorerTxForChainId(long chainId) =>
        Chains.Values.FirstOrDefault(c => c.ChainId == chainId)?.ExplorerTx ?? "etherscan.io/tx/";

    private static HttpClient Http => PublicHttp.Shared;

    /// <summary>
    /// Prepares a send: validates inputs, fetches balance / nonce / gas price, and returns a
    /// quote for explicit user confirmation. Nothing is signed here.
    /// </summary>
    public async Task<(EthSendQuote? Quote, string? Error)> PrepareAsync(
        string fromAddress,
        string toAddress,
        decimal amountEth,
        EvmChain? chain = null,
        CancellationToken ct = default)
    {
        chain ??= Chains["ETH"];
        if (!IsHexAddress(toAddress))
        {
            return (null, "Destination must be a 0x… (EVM) address of 42 characters.");
        }

        if (amountEth <= 0)
        {
            return (null, "Amount must be positive.");
        }

        var amountWei = new BigInteger(amountEth * 1_000_000_000_000_000_000m);

        foreach (var rpc in chain.Rpcs)
        {
            try
            {
                var balanceHex = await CallAsync(rpc, "eth_getBalance", new object[] { fromAddress, "latest" }, ct);
                var nonceHex = await CallAsync(rpc, "eth_getTransactionCount", new object[] { fromAddress, "pending" }, ct);
                var gasHex = await CallAsync(rpc, "eth_gasPrice", Array.Empty<object>(), ct);
                if (balanceHex is null || nonceHex is null || gasHex is null) continue;

                var balance = FromHex(balanceHex);
                var nonce = FromHex(nonceHex);
                var gasPrice = FromHex(gasHex);
                // 5% headroom: enough that a small gas-price move between quote and broadcast
                // doesn't strand the transaction, without overpaying the way a 20% pad did.
                var paddedGasPrice = gasPrice * 105 / 100;
                var maxFeeWei = paddedGasPrice * TransferGasLimit;

                if (balance < amountWei + maxFeeWei)
                {
                    var have = (decimal)balance / 1_000_000_000_000_000_000m;
                    return (null,
                        $"Insufficient funds: balance {have:0.######} {chain.Symbol}, " +
                        $"need {amountEth:0.######} {chain.Symbol} + ~{(decimal)maxFeeWei / 1_000_000_000_000_000_000m:0.######} {chain.Symbol} fee.");
                }

                return (new EthSendQuote(
                    fromAddress, toAddress, amountEth, amountWei, nonce, paddedGasPrice,
                    (decimal)maxFeeWei / 1_000_000_000_000_000_000m, rpc,
                    chain.ChainId, chain.Symbol, chain.Rpcs), null);
            }
            catch
            {
                // try next RPC
            }
        }

        return (null, $"All public {chain.Name} RPCs are unreachable — check your connection (or Tor).");
    }

    /// <summary>Signs the quoted transfer with the given private key and broadcasts it.</summary>
    public async Task<EthSendResult> SignAndBroadcastAsync(
        EthSendQuote quote,
        byte[] privateKey,
        CancellationToken ct = default)
    {
        string signedHex;
        try
        {
            signedHex = SignTransfer(
                privateKey, quote.ChainId, quote.To, quote.AmountWei, quote.Nonce, quote.GasPriceWei, TransferGasLimit);
        }
        catch (Exception ex)
        {
            return new EthSendResult(false, null, $"Signing failed: {ex.Message}");
        }

        foreach (var rpc in quote.Rpcs ?? Rpcs)
        {
            try
            {
                using var res = await Http.PostAsJsonAsync(rpc, new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "eth_sendRawTransaction",
                    @params = new object[] { "0x" + signedHex },
                }, ct);
                if (!res.IsSuccessStatusCode) continue;

                using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("result", out var result) &&
                    result.ValueKind == JsonValueKind.String)
                {
                    return new EthSendResult(true, result.GetString(), null);
                }

                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    var message = error.TryGetProperty("message", out var m) ? m.GetString() : "RPC rejected the transaction";
                    // A node-level rejection (bad nonce, underpriced, insufficient funds) is
                    // final — retrying another RPC with the same bytes will fail the same way.
                    return new EthSendResult(false, null, message);
                }
            }
            catch
            {
                // network issue — try next RPC
            }
        }

        return new EthSendResult(false, null, "Broadcast failed: no RPC accepted the transaction.");
    }

    /// <summary>
    /// EIP-155 legacy transfer signature (chainId 1). Public so the test suite can pin it to the
    /// official EIP-155 example transaction byte-for-byte.
    /// </summary>
    public static string SignTransfer(
        byte[] privateKey,
        string to,
        BigInteger amountWei,
        BigInteger nonce,
        BigInteger gasPriceWei,
        BigInteger gasLimit) =>
        SignTransfer(privateKey, ChainId, to, amountWei, nonce, gasPriceWei, gasLimit);

    /// <summary>EIP-155 legacy transfer signature for any EVM chain id (same signing across chains).</summary>
    public static string SignTransfer(
        byte[] privateKey,
        long chainId,
        string to,
        BigInteger amountWei,
        BigInteger nonce,
        BigInteger gasPriceWei,
        BigInteger gasLimit)
    {
        var signer = new LegacyTransactionSigner();
        return signer.SignTransaction(privateKey, chainId, to, amountWei, nonce, gasPriceWei, gasLimit);
    }

    /// <summary>THORChain deposits touch a contract, so they need more gas than a 21k transfer.</summary>
    private const long SwapGasLimit = 120_000;
    private const string EthZeroAsset = "0x0000000000000000000000000000000000000000";

    /// <summary>
    /// ABI-encodes a THORChain router <c>depositWithExpiry(address vault, address asset, uint256 amount,
    /// string memo, uint256 expiry)</c> call. For native ETH the asset is the zero address and the ETH is
    /// carried as the tx value. Hand-rolled (no extra package) and pure, so the test suite pins the 4-byte
    /// selector (0x44bc937b) and the exact word layout — the one place a mistake would matter.
    /// </summary>
    public static string EncodeDepositWithExpiry(
        string vault, string asset, BigInteger amount, string memo, BigInteger expiry)
    {
        var hash = new Nethereum.Util.Sha3Keccack()
            .CalculateHash("depositWithExpiry(address,address,uint256,string,uint256)");
        if (hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hash = hash[2..];
        var selector = hash[..8];

        var memoBytes = System.Text.Encoding.UTF8.GetBytes(memo);
        var pad = (32 - (memoBytes.Length % 32)) % 32;

        var sb = new System.Text.StringBuilder();
        sb.Append(Word(AddressBytes(vault)));       // head[0] vault
        sb.Append(Word(AddressBytes(asset)));       // head[1] asset
        sb.Append(Word(UIntBytes(amount)));         // head[2] amount
        sb.Append(Word(UIntBytes(new BigInteger(160)))); // head[3] offset to memo = 5*32
        sb.Append(Word(UIntBytes(expiry)));         // head[4] expiry
        sb.Append(Word(UIntBytes(new BigInteger(memoBytes.Length)))); // tail: memo length
        sb.Append(Convert.ToHexString(memoBytes).ToLowerInvariant()); // tail: memo bytes
        sb.Append(new string('0', pad * 2));        // right-pad to a 32-byte boundary

        return "0x" + selector + sb;
    }

    private static byte[] AddressBytes(string addr)
    {
        var h = addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? addr[2..] : addr;
        return Convert.FromHexString(h.PadLeft(40, '0')); // 20 bytes
    }

    private static byte[] UIntBytes(BigInteger v) => v.ToByteArray(isUnsigned: true, isBigEndian: true);

    /// <summary>A 32-byte ABI word: the value right-aligned, left-padded with zeros, as 64 hex chars.</summary>
    private static string Word(byte[] value) =>
        Convert.ToHexString(value).ToLowerInvariant().PadLeft(64, '0');

    /// <summary>
    /// Prepares an ETH-from THORChain swap: a router.depositWithExpiry call carrying the ETH as value and
    /// the swap memo as calldata. Balance/nonce/gas come from the same public RPCs as a transfer; nothing
    /// is signed here. <paramref name="router"/> is the THORChain router, <paramref name="vault"/> the
    /// inbound asgard vault (both from the quote).
    /// </summary>
    public async Task<(EthSendQuote? Quote, string? Error)> PrepareSwapAsync(
        string fromAddress, string router, string vault, decimal amountEth, string memo, BigInteger expiry,
        CancellationToken ct = default)
    {
        if (!IsHexAddress(router) || !IsHexAddress(vault))
            return (null, "THORChain returned an invalid router/vault address for an ETH swap.");
        if (amountEth <= 0) return (null, "Amount must be positive.");

        var amountWei = new BigInteger(amountEth * 1_000_000_000_000_000_000m);
        string data;
        try { data = EncodeDepositWithExpiry(vault, EthZeroAsset, amountWei, memo, expiry); }
        catch (Exception ex) { return (null, $"Could not encode the swap call: {ex.Message}"); }

        var eth = Chains["ETH"];
        foreach (var rpc in eth.Rpcs)
        {
            try
            {
                var balanceHex = await CallAsync(rpc, "eth_getBalance", new object[] { fromAddress, "latest" }, ct);
                var nonceHex = await CallAsync(rpc, "eth_getTransactionCount", new object[] { fromAddress, "pending" }, ct);
                var gasHex = await CallAsync(rpc, "eth_gasPrice", Array.Empty<object>(), ct);
                if (balanceHex is null || nonceHex is null || gasHex is null) continue;

                var balance = FromHex(balanceHex);
                var nonce = FromHex(nonceHex);
                var paddedGasPrice = FromHex(gasHex) * 105 / 100;
                var maxFeeWei = paddedGasPrice * SwapGasLimit;

                if (balance < amountWei + maxFeeWei)
                {
                    var have = (decimal)balance / 1_000_000_000_000_000_000m;
                    return (null, $"Insufficient ETH: balance {have:0.######}, need {amountEth:0.######} + " +
                                  $"~{(decimal)maxFeeWei / 1_000_000_000_000_000_000m:0.######} fee.");
                }

                return (new EthSendQuote(
                    fromAddress, router, amountEth, amountWei, nonce, paddedGasPrice,
                    (decimal)maxFeeWei / 1_000_000_000_000_000_000m, rpc, 1, "ETH", eth.Rpcs, data, SwapGasLimit), null);
            }
            catch
            {
                // try next RPC
            }
        }
        return (null, "All public Ethereum RPCs are unreachable — check your connection (or Tor).");
    }

    /// <summary>Signs and broadcasts a prepared router-call (swap) quote, using its data + gas-limit.</summary>
    public async Task<EthSendResult> SignAndBroadcastSwapAsync(
        EthSendQuote quote, byte[] privateKey, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(quote.Data))
            return new EthSendResult(false, null, "This quote carries no swap calldata.");

        string signedHex;
        try
        {
            signedHex = new LegacyTransactionSigner().SignTransaction(
                privateKey, quote.ChainId, quote.To, quote.AmountWei, quote.Nonce, quote.GasPriceWei,
                quote.GasLimit, quote.Data);
        }
        catch (Exception ex)
        {
            return new EthSendResult(false, null, $"Signing failed: {ex.Message}");
        }

        return await BroadcastAsync(signedHex, quote.Rpcs ?? Rpcs, ct);
    }

    private static async Task<EthSendResult> BroadcastAsync(
        string signedHex, IReadOnlyList<string> rpcs, CancellationToken ct)
    {
        foreach (var rpc in rpcs)
        {
            try
            {
                using var res = await Http.PostAsJsonAsync(rpc, new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "eth_sendRawTransaction",
                    @params = new object[] { "0x" + signedHex },
                }, ct);
                if (!res.IsSuccessStatusCode) continue;

                using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
                    return new EthSendResult(true, result.GetString(), null);
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    var message = error.TryGetProperty("message", out var m) ? m.GetString() : "RPC rejected the transaction";
                    return new EthSendResult(false, null, message);
                }
            }
            catch
            {
                // network issue — try next RPC
            }
        }
        return new EthSendResult(false, null, "Broadcast failed: no RPC accepted the transaction.");
    }

    private static async Task<string?> CallAsync(string rpc, string method, object[] args, CancellationToken ct)
    {
        using var res = await Http.PostAsJsonAsync(rpc, new
        {
            jsonrpc = "2.0",
            id = 1,
            method,
            @params = args,
        }, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.TryGetProperty("result", out var result) ? result.GetString() : null;
    }

    private static BigInteger FromHex(string hex) =>
        BigInteger.Parse("0" + hex.TrimStart('0', 'x', 'X').PadLeft(1, '0'), NumberStyles.HexNumber);

    private static bool IsHexAddress(string value) =>
        value is { Length: 42 } &&
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
        value.Skip(2).All(Uri.IsHexDigit);
}
