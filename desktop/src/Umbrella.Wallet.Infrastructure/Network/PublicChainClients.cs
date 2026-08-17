using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Infrastructure.Network;

public sealed record ChainBalance(ChainId Chain, string Address, decimal NativeAmount, string Symbol);

/// <summary>24-hour market stats for a coin, in USD (Binance quote).</summary>
public sealed record MarketStats(decimal High, decimal Low, decimal QuoteVolume);

/// <summary>Richer token market data (USD) from the optional CoinGecko connector.</summary>
public sealed record TokenMarketData(decimal MarketCap, decimal Fdv, decimal Volume24h);

/// <summary>
/// Shared, reconfigurable HTTP client for every public endpoint. All balance/price traffic goes
/// through <see cref="Shared"/>, so enabling Tor swaps one client and routes everything at once.
/// </summary>
public static class PublicHttp
{
    /// <summary>
    /// HttpClient sends no User-Agent by default, and CoinGecko answers such requests with
    /// 403 "Please add a descriptive User-Agent to your request." That turned every price
    /// lookup into an empty result, which surfaced as $0.00 balances and a dead market list.
    /// </summary>
    public const string UserAgent = "UmbrellaWallet/1.0 (desktop; non-custodial)";

    /// <summary>Which IP family outbound connections may use when going direct (no proxy).</summary>
    public enum IpMode { Auto, V4Only, V6Only }

    private static IpMode _ipMode = IpMode.Auto;
    private static bool _requireProxy;
    private static HttpClient _shared = Build(null);

    public static HttpClient Shared => _shared;

    /// <summary>The active SOCKS5 proxy URI (Tor or a user proxy), or null when going direct.</summary>
    public static string? ActiveProxy { get; private set; }

    /// <summary>The active IP-family preference for direct connections.</summary>
    public static IpMode IpPreference => _ipMode;

    /// <summary>
    /// Tor-only kill-switch. When true, any outbound request that would go direct (no proxy) is
    /// refused at the transport layer instead of leaking to clearnet — so if Tor drops or is turned
    /// off, the wallet fails closed rather than silently de-anonymising. Covers every request on the
    /// shared client (balances, prices, history, swap quotes, broadcasts).
    /// </summary>
    public static bool RequireProxy => _requireProxy;

    /// <summary>Turns the Tor-only kill-switch on/off and rebuilds the shared client.</summary>
    public static void SetRequireProxy(bool require)
    {
        if (_requireProxy == require) return;
        _requireProxy = require;
        var old = _shared;
        _shared = Build(ActiveProxy);
        try { old.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>
    /// Route all public requests through a SOCKS5 proxy (e.g. Tor at socks5://127.0.0.1:9050),
    /// or pass null/empty to go direct. Rebuilds the shared client.
    /// </summary>
    public static void SetProxy(string? socks5Uri)
    {
        var normalized = string.IsNullOrWhiteSpace(socks5Uri) ? null : socks5Uri.Trim();
        var old = _shared;
        _shared = Build(normalized);
        ActiveProxy = normalized;
        try { old.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>
    /// Force outbound connections onto IPv4 or IPv6 only (or Auto). Only takes effect on direct
    /// connections — when a proxy is active the proxy decides addressing. Rebuilds the client.
    /// </summary>
    public static void SetIpPreference(IpMode mode)
    {
        _ipMode = mode;
        var old = _shared;
        _shared = Build(ActiveProxy);
        try { old.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>Parses "auto" / "ipv4" / "ipv6" (case-insensitive) into an <see cref="IpMode"/>.</summary>
    public static IpMode ParseIpMode(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "ipv4" or "v4" or "4" => IpMode.V4Only,
        "ipv6" or "v6" or "6" => IpMode.V6Only,
        _ => IpMode.Auto,
    };

    /// <summary>Quick TCP reachability check for a proxy host:port (does not prove it is Tor).</summary>
    public static async Task<bool> IsProxyReachableAsync(string host, int port, CancellationToken ct = default)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connect = client.ConnectAsync(host, port);
            var done = await Task.WhenAny(connect, Task.Delay(2500, ct));
            return done == connect && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public static HttpClient Create(int timeoutSeconds) => Shared;

    private static HttpClient Build(string? socks5Uri)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        if (!string.IsNullOrWhiteSpace(socks5Uri))
        {
            // .NET 6+ SocketsHttpHandler understands socks5:// proxies via WebProxy.
            handler.Proxy = new System.Net.WebProxy(socks5Uri);
            handler.UseProxy = true;
        }
        else if (_requireProxy)
        {
            // Tor-only mode with no proxy active → refuse every connection (fail closed). This is the
            // kill-switch: a dropped or disabled Tor can never silently fall back to clearnet.
            handler.ConnectCallback = (_, _) => ValueTask.FromException<System.IO.Stream>(
                new HttpRequestException(
                    "Tor-only mode is on but Tor is not connected — clearnet request blocked."));
        }
        else if (_ipMode != IpMode.Auto)
        {
            // Pin the IP family for direct connections only. With a proxy in play the proxy owns
            // addressing, so we leave the default resolver alone there.
            var family = _ipMode == IpMode.V6Only
                ? System.Net.Sockets.AddressFamily.InterNetworkV6
                : System.Net.Sockets.AddressFamily.InterNetwork;
            handler.ConnectCallback = async (context, ct) =>
            {
                var entries = await System.Net.Dns.GetHostAddressesAsync(
                    context.DnsEndPoint.Host, family, ct);
                if (entries.Length == 0)
                {
                    throw new System.Net.Sockets.SocketException(
                        (int)System.Net.Sockets.SocketError.HostNotFound);
                }
                var socket = new System.Net.Sockets.Socket(
                    family, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)
                {
                    NoDelay = true,
                };
                try
                {
                    await socket.ConnectAsync(entries, context.DnsEndPoint.Port, ct);
                    return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        }

        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        return http;
    }
}

/// <summary>
/// Public-RPC / explorer balances — no API keys required.
/// </summary>
public sealed class PublicChainBalanceClient
{
    private static HttpClient Http => PublicHttp.Shared;

    private static readonly string[] EthRpcs =
    [
        "https://cloudflare-eth.com",
        "https://rpc.ankr.com/eth",
        "https://eth.drpc.org",
    ];

    public async Task<ChainBalance?> GetBalanceAsync(
        ChainId chain,
        string address,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address) ||
            address.StartsWith("Unlock", StringComparison.Ordinal) ||
            address.StartsWith("Adapter", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return chain switch
            {
                ChainId.Btc => await GetBtcAsync(address, cancellationToken),
                ChainId.Ltc => await GetEsploraAsync(
                    "https://litecoinspace.org/api", ChainId.Ltc, address, "LTC", 8, cancellationToken),
                ChainId.Doge => await GetBlockcypherAsync("doge", ChainId.Doge, address, "DOGE", 8, cancellationToken),
                ChainId.Eth => await GetEthAsync(address, cancellationToken),
                ChainId.Tron => await GetTronAsync(address, cancellationToken),
                ChainId.Sol => await GetSolAsync(address, cancellationToken),
                ChainId.Ton => await GetTonAsync(address, cancellationToken),
                ChainId.Ada => await GetAdaAsync(address, cancellationToken),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>TON native balance via toncenter (returns nanoTON as a string).</summary>
    private static async Task<ChainBalance?> GetTonAsync(string address, CancellationToken ct)
    {
        using var res = await Http.GetAsync(
            $"https://toncenter.com/api/v2/getAddressBalance?address={Uri.EscapeDataString(address)}", ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("result", out var r)) return null;
        var nano = r.ValueKind == JsonValueKind.String ? r.GetString() : r.GetRawText();
        if (!decimal.TryParse(nano, NumberStyles.Any, CultureInfo.InvariantCulture, out var raw)) return null;
        return new ChainBalance(ChainId.Ton, address, raw / 1_000_000_000m, "TON");
    }

    /// <summary>Cardano native (ADA) balance via Koios (returns lovelace as a string).</summary>
    private static async Task<ChainBalance?> GetAdaAsync(string address, CancellationToken ct)
    {
        using var body = new StringContent(
            $"{{\"_addresses\":[\"{address}\"]}}", Encoding.UTF8, "application/json");
        using var res = await Http.PostAsync("https://api.koios.rest/api/v1/address_info", body, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return null;
        if (!doc.RootElement[0].TryGetProperty("balance", out var b)) return null;
        var lovelace = b.ValueKind == JsonValueKind.String ? b.GetString() : b.GetRawText();
        if (!decimal.TryParse(lovelace, NumberStyles.Any, CultureInfo.InvariantCulture, out var raw)) return null;
        return new ChainBalance(ChainId.Ada, address, raw / 1_000_000m, "ADA");
    }

    private static async Task<ChainBalance?> GetBtcAsync(string address, CancellationToken ct)
    {
        using var res = await Http.GetAsync(
            $"https://blockstream.info/api/address/{Uri.EscapeDataString(address)}", ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var funded = doc.RootElement.GetProperty("chain_stats").GetProperty("funded_txo_sum").GetInt64();
        var spent = doc.RootElement.GetProperty("chain_stats").GetProperty("spent_txo_sum").GetInt64();
        var sats = funded - spent;
        return new ChainBalance(ChainId.Btc, address, sats / 100_000_000m, "BTC");
    }

    private static async Task<ChainBalance?> GetEsploraAsync(
        string apiBase, ChainId chain, string address, string symbol, int decimals, CancellationToken ct)
    {
        using var res = await Http.GetAsync($"{apiBase}/address/{Uri.EscapeDataString(address)}", ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var funded = doc.RootElement.GetProperty("chain_stats").GetProperty("funded_txo_sum").GetInt64();
        var spent = doc.RootElement.GetProperty("chain_stats").GetProperty("spent_txo_sum").GetInt64();
        var raw = funded - spent;
        var amount = raw / (decimal)Math.Pow(10, decimals);
        return new ChainBalance(chain, address, amount, symbol);
    }

    private static async Task<ChainBalance?> GetBlockcypherAsync(
        string coin, ChainId chain, string address, string symbol, int decimals, CancellationToken ct)
    {
        using var res = await Http.GetAsync(
            $"https://api.blockcypher.com/v1/{coin}/main/addrs/{Uri.EscapeDataString(address)}/balance", ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var bal = doc.RootElement.GetProperty("balance").GetInt64();
        return new ChainBalance(chain, address, bal / (decimal)Math.Pow(10, decimals), symbol);
    }

    private static async Task<ChainBalance?> GetEthAsync(string address, CancellationToken ct)
    {
        foreach (var rpc in EthRpcs)
        {
            try
            {
                var payload = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "eth_getBalance",
                    @params = new object[] { address, "latest" },
                };
                using var res = await Http.PostAsJsonAsync(rpc, payload, ct);
                if (!res.IsSuccessStatusCode) continue;
                using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (!doc.RootElement.TryGetProperty("result", out var result)) continue;
                var hex = result.GetString();
                if (string.IsNullOrWhiteSpace(hex)) continue;
                var wei = System.Numerics.BigInteger.Parse(hex.AsSpan(2), NumberStyles.HexNumber);
                var eth = (decimal)wei / 1_000_000_000_000_000_000m;
                return new ChainBalance(ChainId.Eth, address, eth, "ETH");
            }
            catch
            {
                /* try next RPC */
            }
        }

        return null;
    }

    // Every EVM network that shares the same 0x address as Ethereum, with public RPC fallbacks. The
    // L2s (Arbitrum / Optimism / Base) use ETH as their native coin, shown per-network.
    private static readonly (string Symbol, string Network, string[] Rpcs)[] EvmSideChains =
    [
        ("BNB",   "BSC",       ["https://bsc-dataseed.binance.org", "https://bsc-dataseed1.defibit.io", "https://rpc.ankr.com/bsc"]),
        ("MATIC", "Polygon",   ["https://polygon-rpc.com", "https://rpc.ankr.com/polygon"]),
        ("AVAX",  "Avalanche", ["https://api.avax.network/ext/bc/C/rpc", "https://rpc.ankr.com/avalanche"]),
        ("FTM",   "Fantom",    ["https://rpc.ftm.tools", "https://rpc.ankr.com/fantom"]),
        ("CRO",   "Cronos",    ["https://evm.cronos.org", "https://cronos-evm-rpc.publicnode.com"]),
        ("ETH",   "Arbitrum",  ["https://arb1.arbitrum.io/rpc", "https://rpc.ankr.com/arbitrum"]),
        ("ETH",   "Optimism",  ["https://mainnet.optimism.io", "https://rpc.ankr.com/optimism"]),
        ("ETH",   "Base",      ["https://mainnet.base.org", "https://base.publicnode.com"]),
    ];

    /// <summary>
    /// Native balances of every major EVM network (BNB/MATIC/AVAX/FTM/CRO, plus ETH on the Arbitrum,
    /// Optimism and Base L2s) at the SAME 0x address, so a MetaMask-imported wallet shows all of them —
    /// not just Ethereum mainnet. Queried in parallel; only non-zero balances are returned.
    /// </summary>
    public async Task<IReadOnlyList<(string Symbol, decimal Amount, string Network)>> GetEvmSideBalancesAsync(
        string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address) || !address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return [];

        var tasks = EvmSideChains.Select(async chain =>
        {
            var amount = await EvmNativeBalanceAsync(chain.Rpcs, address, cancellationToken);
            return (chain.Symbol, Amount: amount ?? 0m, chain.Network);
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(r => r.Amount > 0m).ToList();
    }

    private static async Task<decimal?> EvmNativeBalanceAsync(string[] rpcs, string address, CancellationToken ct)
    {
        foreach (var rpc in rpcs)
        {
            try
            {
                var payload = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "eth_getBalance",
                    @params = new object[] { address, "latest" },
                };
                using var res = await Http.PostAsJsonAsync(rpc, payload, ct);
                if (!res.IsSuccessStatusCode) continue;
                using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (!doc.RootElement.TryGetProperty("result", out var result)) continue;
                var hex = result.GetString();
                if (string.IsNullOrWhiteSpace(hex) || hex.Length < 3) continue;
                var wei = System.Numerics.BigInteger.Parse(hex.AsSpan(2), NumberStyles.HexNumber);
                return (decimal)wei / 1_000_000_000_000_000_000m;
            }
            catch
            {
                /* try next RPC */
            }
        }

        return null;
    }

    private static async Task<ChainBalance?> GetSolAsync(string address, CancellationToken ct)
    {
        // Solana public JSON-RPC: getBalance returns lamports (1 SOL = 1e9 lamports).
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "getBalance",
            @params = new object[] { address },
        };
        using var res = await Http.PostAsJsonAsync("https://api.mainnet-beta.solana.com", payload, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("result", out var result)) return null;
        if (!result.TryGetProperty("value", out var value)) return null;
        var lamports = value.GetInt64();
        return new ChainBalance(ChainId.Sol, address, lamports / 1_000_000_000m, "SOL");
    }

    private static async Task<ChainBalance?> GetTronAsync(string address, CancellationToken ct)
    {
        using var res = await Http.GetAsync(
            $"https://apilist.tronscanapi.com/api/account?address={Uri.EscapeDataString(address)}", ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("balance", out var balEl)) return null;
        var sun = balEl.GetInt64();
        return new ChainBalance(ChainId.Tron, address, sun / 1_000_000m, "TRX");
    }

    /// <summary>
    /// USDT-TRC20 balance for a TRON address (the "TRC20" a user usually means — Tether on TRON).
    /// Reads the token list from tronscan; returns null if it can't be determined.
    /// </summary>
    public async Task<decimal?> GetTronUsdtAsync(string address, CancellationToken cancellationToken = default)
    {
        const string usdtContract = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";
        if (string.IsNullOrWhiteSpace(address) || !address.StartsWith('T')) return null;

        try
        {
            using var res = await Http.GetAsync(
                $"https://apilist.tronscanapi.com/api/account?address={Uri.EscapeDataString(address)}",
                cancellationToken);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            // tronscan returns "trc20token_balances": [{ tokenId, balance, tokenDecimal, tokenAbbr }]
            if (!doc.RootElement.TryGetProperty("trc20token_balances", out var tokens) ||
                tokens.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var token in tokens.EnumerateArray())
            {
                var id = token.TryGetProperty("tokenId", out var tid) ? tid.GetString() : null;
                var abbr = token.TryGetProperty("tokenAbbr", out var ta) ? ta.GetString() : null;
                var isUsdt = string.Equals(id, usdtContract, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(abbr, "USDT", StringComparison.OrdinalIgnoreCase);
                if (!isUsdt) continue;

                var decimals = token.TryGetProperty("tokenDecimal", out var td) ? td.GetInt32() : 6;
                var rawStr = token.TryGetProperty("balance", out var b) ? b.GetString() : null;
                if (!System.Numerics.BigInteger.TryParse(rawStr, out var raw)) continue;
                return (decimal)raw / (decimal)Math.Pow(10, decimals);
            }

            return 0m; // address exists but holds no USDT
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Every TRC-20 token held at a TRON address (not just USDT), so reward tokens, other stablecoins
    /// and any TRC-20 asset show up — this is what most "my balance is missing" cases on TRON are.
    /// </summary>
    public async Task<IReadOnlyList<TokenBalance>> GetTronTokensAsync(
        string address, CancellationToken cancellationToken = default)
    {
        var result = new List<TokenBalance>();
        if (string.IsNullOrWhiteSpace(address) || !address.StartsWith('T')) return result;

        try
        {
            using var res = await Http.GetAsync(
                $"https://apilist.tronscanapi.com/api/account?address={Uri.EscapeDataString(address)}",
                cancellationToken);
            if (!res.IsSuccessStatusCode) return result;
            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("trc20token_balances", out var tokens) ||
                tokens.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var token in tokens.EnumerateArray())
            {
                if (result.Count >= 40) break;
                var abbr = token.TryGetProperty("tokenAbbr", out var ta) ? ta.GetString() : null;
                var name = token.TryGetProperty("tokenName", out var tn) ? tn.GetString() : abbr;
                var contract = token.TryGetProperty("tokenId", out var tid) ? tid.GetString() : null;
                var decimals = token.TryGetProperty("tokenDecimal", out var td) ? td.GetInt32() : 6;
                var rawStr = token.TryGetProperty("balance", out var b) ? b.GetString() : null;
                if (string.IsNullOrWhiteSpace(abbr) || string.IsNullOrWhiteSpace(contract)) continue;
                if (!System.Numerics.BigInteger.TryParse(rawStr, out var raw) || raw <= 0) continue;

                decimal divisor = 1m;
                for (var i = 0; i < Math.Clamp(decimals, 0, 28); i++) divisor *= 10m;
                decimal amount;
                try { amount = (decimal)raw / divisor; }
                catch (OverflowException) { continue; }

                result.Add(new TokenBalance(abbr!.ToUpperInvariant(), name ?? abbr!, amount, contract!, decimals));
            }
        }
        catch
        {
            // treated as "no tokens"
        }

        return result;
    }

    /// <summary>
    /// Every ERC-20 token held at an Ethereum address, via Blockscout's public (keyless) API. Covers
    /// any token — stablecoins, reward tokens, on-chain tokenised assets — not just the native ETH.
    /// </summary>
    public async Task<IReadOnlyList<TokenBalance>> GetEthTokensAsync(
        string address, CancellationToken cancellationToken = default)
    {
        var result = new List<TokenBalance>();
        if (string.IsNullOrWhiteSpace(address) || !address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return result;

        try
        {
            using var res = await Http.GetAsync(
                $"https://eth.blockscout.com/api/v2/addresses/{Uri.EscapeDataString(address)}/token-balances",
                cancellationToken);
            if (!res.IsSuccessStatusCode) return result;
            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (result.Count >= 40) break; // some addresses carry thousands of airdropped spam tokens
                if (!entry.TryGetProperty("token", out var token)) continue;
                var type = token.TryGetProperty("type", out var ty) ? ty.GetString() : null;
                if (!string.Equals(type, "ERC-20", StringComparison.OrdinalIgnoreCase)) continue; // skip NFTs

                var symbol = token.TryGetProperty("symbol", out var sy) ? sy.GetString() : null;
                var name = token.TryGetProperty("name", out var nm) ? nm.GetString() : symbol;
                var contract = token.TryGetProperty("address", out var ad) ? ad.GetString() : null;
                var decimals = token.TryGetProperty("decimals", out var de) && int.TryParse(de.GetString(), out var d) ? d : 18;
                var rawStr = entry.TryGetProperty("value", out var v) ? v.GetString() : null;
                if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(contract)) continue;
                if (!System.Numerics.BigInteger.TryParse(rawStr, out var raw) || raw <= 0) continue;

                decimal divisor = 1m;
                for (var i = 0; i < Math.Clamp(decimals, 0, 28); i++) divisor *= 10m;
                decimal amount;
                try { amount = (decimal)raw / divisor; }
                catch (OverflowException) { continue; }

                result.Add(new TokenBalance(symbol!.ToUpperInvariant(), name ?? symbol!, amount, contract!, decimals));
            }
        }
        catch
        {
            // treated as "no tokens"
        }

        return result;
    }

    /// <summary>
    /// The NFT collections (ERC-721 / ERC-1155) held at an Ethereum address, via Blockscout. Returns
    /// names and counts only — no image URLs are fetched, so viewing NFTs never leaks the user's IP.
    /// </summary>
    public async Task<IReadOnlyList<NftHolding>> GetEthNftsAsync(
        string address, CancellationToken cancellationToken = default)
    {
        var result = new List<NftHolding>();
        if (string.IsNullOrWhiteSpace(address) || !address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return result;

        try
        {
            using var res = await Http.GetAsync(
                $"https://eth.blockscout.com/api/v2/addresses/{Uri.EscapeDataString(address)}/nft/collections?type=ERC-721%2CERC-1155",
                cancellationToken);
            if (!res.IsSuccessStatusCode) return result;
            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in items.EnumerateArray())
            {
                if (result.Count >= 60) break;
                if (!item.TryGetProperty("token", out var token)) continue;
                var name = token.TryGetProperty("name", out var n) ? n.GetString() : null;
                var symbol = token.TryGetProperty("symbol", out var s) ? s.GetString() : null;
                var type = token.TryGetProperty("type", out var ty) ? ty.GetString() : "NFT";

                var count = item.TryGetProperty("amount", out var am) && int.TryParse(am.GetString(), out var c) ? c
                    : item.TryGetProperty("token_instances", out var ti) && ti.ValueKind == JsonValueKind.Array ? ti.GetArrayLength()
                    : 1;
                if (count <= 0) count = 1;

                result.Add(new NftHolding(name ?? symbol ?? "Unnamed collection", symbol ?? "", count, type ?? "NFT", "Ethereum"));
            }
        }
        catch
        {
            // treated as "no NFTs"
        }

        return result;
    }
}

/// <summary>A fungible token balance (TRC-20 / ERC-20) held at an address.</summary>
public sealed record TokenBalance(string Symbol, string Name, decimal Amount, string Contract, int Decimals);

/// <summary>An NFT collection held at an address (name + count only — no image fetch, for privacy).</summary>
public sealed record NftHolding(string Name, string Symbol, int Count, string Standard, string Network);

/// <summary>
/// USD prices from public endpoints, no API key. CoinGecko first, Binance as a fallback so a
/// single provider being down or rate-limiting does not blank the whole wallet.
/// </summary>
/// <summary>One OHLC candle for the candlestick chart.</summary>
public readonly record struct PriceCandle(double Open, double High, double Low, double Close);

public sealed class PublicMarketRatesClient
{
    private static HttpClient Http => PublicHttp.Shared;

    private static readonly Dictionary<string, string> CoinIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = "bitcoin",
        ["ETH"] = "ethereum",
        ["LTC"] = "litecoin",
        ["DOGE"] = "dogecoin",
        ["TRX"] = "tron",
        ["SOL"] = "solana",
        ["TON"] = "the-open-network",
        ["XMR"] = "monero",
        ["ADA"] = "cardano",
        ["BNB"] = "binancecoin",
        ["MATIC"] = "matic-network",
        ["AVAX"] = "avalanche-2",
        ["FTM"] = "fantom",
        ["CRO"] = "crypto-com-chain",
        ["USDC"] = "usd-coin",
        ["LINK"] = "chainlink",
        ["UNI"] = "uniswap",
        ["XRP"] = "ripple",
        ["DOT"] = "polkadot",
        ["BCH"] = "bitcoin-cash",
        // Tether trades a cent either side of $1; quoting it beats assuming exactly 1.00.
        ["USDT"] = "tether",
    };

    /// <summary>Binance USDT pairs. XMR is absent — Binance delisted it in 2024.</summary>
    private static readonly Dictionary<string, string> BinancePairs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = "BTCUSDT",
        ["ETH"] = "ETHUSDT",
        ["LTC"] = "LTCUSDT",
        ["DOGE"] = "DOGEUSDT",
        ["TRX"] = "TRXUSDT",
        ["SOL"] = "SOLUSDT",
        ["TON"] = "TONUSDT",
        ["ADA"] = "ADAUSDT",
        ["BNB"] = "BNBUSDT",
        ["MATIC"] = "MATICUSDT",
        ["AVAX"] = "AVAXUSDT",
        ["FTM"] = "FTMUSDT",
        ["LINK"] = "LINKUSDT",
        ["UNI"] = "UNIUSDT",
        ["XRP"] = "XRPUSDT",
        ["DOT"] = "DOTUSDT",
        ["BCH"] = "BCHUSDT",
        ["USDC"] = "USDCUSDT",
        // CRO has no Binance USDT pair — it prices via CoinGecko only.
    };

    /// <summary>Chart windows offered in the Market view.</summary>
    public static IReadOnlyList<string> ChartRanges { get; } = ["1H", "24H", "7D", "30D", "1Y"];

    /// <summary>24-hour high, low and quote (USD) volume for a coin, or null when there's no Binance
    /// USDT pair for it. Uses the same public Binance endpoint as the price feed — no new data source
    /// and no extra tracking, and it rides the same Tor/proxy route.</summary>
    public async Task<MarketStats?> GetMarketStatsAsync(string symbol, CancellationToken ct = default)
    {
        if (!BinancePairs.TryGetValue(symbol, out var pair)) return null;
        try
        {
            var url = $"https://api.binance.com/api/v3/ticker/24hr?symbol={pair}";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            decimal High = Dec(root, "highPrice"), Low = Dec(root, "lowPrice"), Vol = Dec(root, "quoteVolume");
            return new MarketStats(High, Low, Vol);
        }
        catch
        {
            return null;
        }

        static decimal Dec(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.GetString() is { } s &&
            decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }

    /// <summary>
    /// Richer token stats (market cap, fully-diluted valuation, 24h volume) from CoinGecko. This is
    /// the OPTIONAL market-data connector — only called when the user turns it on, since it's a
    /// third-party the privacy-first default deliberately never contacts. Rides the shared Tor/proxy
    /// client like everything else. Returns null when the coin isn't mapped or the call fails.
    /// </summary>
    public async Task<TokenMarketData?> GetTokenMarketDataAsync(string symbol, CancellationToken ct = default)
    {
        if (!CoinIds.TryGetValue(symbol, out var id)) return null;
        try
        {
            var url = "https://api.coingecko.com/api/v3/coins/markets?vs_currency=usd" +
                      $"&ids={Uri.EscapeDataString(id)}&per_page=1&page=1&sparkline=false";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return null;
            var json = await res.Content.ReadAsStringAsync(ct);
            return ParseTokenMarketData(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parses a CoinGecko /coins/markets array (static + testable).</summary>
    public static TokenMarketData? ParseTokenMarketData(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            return new TokenMarketData(
                Num(row, "market_cap"),
                Num(row, "fully_diluted_valuation"),
                Num(row, "total_volume"));
        }
        return null;

        static decimal Num(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number &&
            p.TryGetDecimal(out var v) ? v : 0m;
    }

    /// <summary>
    /// Real price history at a resolution that matches the window.
    ///
    /// Binance klines are the primary source: they go down to one-minute candles, so a 1H chart is
    /// actually 60 real points rather than the handful of daily closes the old sparkline drew.
    /// CoinGecko is the fallback for coins with no Binance USDT pair (e.g. XMR on some regions).
    /// </summary>
    public async Task<IReadOnlyList<double>> GetPriceSeriesAsync(
        string symbol, string range, CancellationToken cancellationToken = default)
    {
        var (interval, limit, days) = range.ToUpperInvariant() switch
        {
            "1H" => ("1m", 60, "1"),
            "24H" => ("15m", 96, "1"),
            "7D" => ("2h", 84, "7"),
            "30D" => ("8h", 90, "30"),
            _ => ("1d", 365, "365"),
        };

        var pair = BinancePairs.GetValueOrDefault(symbol.ToUpperInvariant());
        if (pair is not null)
        {
            try
            {
                var url = $"https://api.binance.com/api/v3/klines?symbol={pair}&interval={interval}&limit={limit}";
                using var res = await Http.GetAsync(url, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(
                        await res.Content.ReadAsStreamAsync(cancellationToken),
                        cancellationToken: cancellationToken);

                    var candles = new List<double>();
                    foreach (var candle in doc.RootElement.EnumerateArray())
                    {
                        // [openTime, open, high, low, close, ...] — close is index 4.
                        if (candle.GetArrayLength() > 4 &&
                            double.TryParse(candle[4].GetString(), NumberStyles.Any,
                                CultureInfo.InvariantCulture, out var close))
                        {
                            candles.Add(close);
                        }
                    }

                    if (candles.Count > 1) return candles;
                }
            }
            catch
            {
                // fall through to CoinGecko
            }
        }

        return await GetGeckoSeriesAsync(symbol, days, cancellationToken);
    }

    /// <summary>Real OHLC candles for a coin/window from Binance klines, for the candlestick chart.
    /// Falls back to degenerate candles (O=H=L=C) from the CoinGecko close series when there is no
    /// Binance pair, so the chart still renders (as thin marks) rather than going blank.</summary>
    public async Task<IReadOnlyList<PriceCandle>> GetCandlesAsync(
        string symbol, string range, CancellationToken cancellationToken = default)
    {
        var (interval, limit, days) = range.ToUpperInvariant() switch
        {
            "1H" => ("1m", 60, "1"),
            "24H" => ("15m", 96, "1"),
            "7D" => ("2h", 84, "7"),
            "30D" => ("8h", 90, "30"),
            _ => ("1d", 365, "365"),
        };

        var pair = BinancePairs.GetValueOrDefault(symbol.ToUpperInvariant());
        if (pair is not null)
        {
            try
            {
                var url = $"https://api.binance.com/api/v3/klines?symbol={pair}&interval={interval}&limit={limit}";
                using var res = await Http.GetAsync(url, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(
                        await res.Content.ReadAsStreamAsync(cancellationToken),
                        cancellationToken: cancellationToken);

                    var candles = new List<PriceCandle>();
                    foreach (var k in doc.RootElement.EnumerateArray())
                    {
                        // [openTime, open(1), high(2), low(3), close(4), ...]
                        if (k.GetArrayLength() > 4 &&
                            double.TryParse(k[1].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var o) &&
                            double.TryParse(k[2].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var h) &&
                            double.TryParse(k[3].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var l) &&
                            double.TryParse(k[4].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var c))
                        {
                            candles.Add(new PriceCandle(o, h, l, c));
                        }
                    }

                    if (candles.Count > 1) return candles;
                }
            }
            catch
            {
                // fall through to CoinGecko
            }
        }

        var series = await GetGeckoSeriesAsync(symbol, days, cancellationToken);
        return series.Select(c => new PriceCandle(c, c, c, c)).ToList();
    }

    private async Task<IReadOnlyList<double>> GetGeckoSeriesAsync(
        string symbol, string days, CancellationToken cancellationToken)
    {
        var id = CoinIds.GetValueOrDefault(symbol.ToUpperInvariant());
        if (id is null) return Array.Empty<double>();

        try
        {
            var url = $"https://api.coingecko.com/api/v3/coins/{id}/market_chart?vs_currency=usd&days={days}";
            using var res = await Http.GetAsync(url, cancellationToken);
            if (!res.IsSuccessStatusCode) return Array.Empty<double>();

            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("prices", out var prices)) return Array.Empty<double>();

            var series = new List<double>();
            foreach (var point in prices.EnumerateArray())
            {
                if (point.GetArrayLength() >= 2) series.Add(point[1].GetDouble());
            }

            return series;
        }
        catch
        {
            return Array.Empty<double>();
        }
    }

    /// <summary>
    /// 7-day USD price series for one coin (CoinGecko market_chart). Returns an empty list on any
    /// failure so the caller can just show "no chart data" rather than crash.
    /// </summary>
    public async Task<IReadOnlyList<double>> GetSparklineAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        var id = CoinIds.GetValueOrDefault(symbol.ToUpperInvariant());
        if (id is null) return Array.Empty<double>();

        try
        {
            var url =
                $"https://api.coingecko.com/api/v3/coins/{id}/market_chart?vs_currency=usd&days=7&interval=daily";
            using var res = await Http.GetAsync(url, cancellationToken);
            if (!res.IsSuccessStatusCode) return Array.Empty<double>();

            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("prices", out var prices)) return Array.Empty<double>();

            var series = new List<double>();
            foreach (var point in prices.EnumerateArray())
            {
                // Each entry is [timestampMs, price].
                if (point.GetArrayLength() >= 2)
                {
                    series.Add(point[1].GetDouble());
                }
            }

            return series;
        }
        catch
        {
            return Array.Empty<double>();
        }
    }

    public async Task<IReadOnlyDictionary<string, (decimal Usd, decimal Change24h)>> GetUsdPricesAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default)
    {
        var list = symbols.Select(s => s.ToUpperInvariant()).Distinct().ToList();
        if (list.Count == 0)
        {
            return new Dictionary<string, (decimal, decimal)>();
        }

        // Binance first: it is far less rate-limited than CoinGecko's free tier (which frequently 429s),
        // and covers most coins. CoinGecko then fills only what Binance lacks (XMR, CRO, …). Merging the
        // two — rather than falling back all-or-nothing — is why every listed coin actually gets a price.
        var map = new Dictionary<string, (decimal Usd, decimal Change24h)>(
            await TryBinanceAsync(list, cancellationToken), StringComparer.OrdinalIgnoreCase);

        var missing = list.Where(s => !map.ContainsKey(s)).ToList();
        if (missing.Count > 0)
        {
            foreach (var kv in await TryCoinGeckoAsync(missing, cancellationToken))
                map[kv.Key] = kv.Value;
        }

        // Stablecoins that still have no quote (e.g. USDT has no Binance USDT pair and CoinGecko was
        // rate-limited) sit at ~$1 rather than showing nothing.
        foreach (var stable in new[] { "USDT", "USDC", "DAI", "TUSD", "USDD" })
            if (list.Contains(stable) && !map.ContainsKey(stable))
                map[stable] = (1m, 0m);

        return map;
    }

    /// <summary>
    /// USD → <paramref name="code"/> fiat rate (EUR, UAH, RUB, …), so balances can display in the user's
    /// currency. Keyless via open.er-api.com; returns 1 on any failure so amounts degrade to USD rather
    /// than vanish.
    /// </summary>
    public async Task<decimal> GetFiatRateAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase))
            return 1m;

        try
        {
            using var res = await Http.GetAsync("https://open.er-api.com/v6/latest/USD", cancellationToken);
            if (!res.IsSuccessStatusCode) return 1m;
            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("rates", out var rates) &&
                rates.TryGetProperty(code.ToUpperInvariant(), out var r) &&
                r.TryGetDecimal(out var rate) && rate > 0m)
            {
                return rate;
            }
        }
        catch
        {
            // fall through to 1.0 (USD)
        }

        return 1m;
    }

    private static async Task<Dictionary<string, (decimal, decimal)>> TryCoinGeckoAsync(
        List<string> list,
        CancellationToken ct)
    {
        var empty = new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase);
        var ids = list.Select(s => CoinIds.GetValueOrDefault(s)).Where(id => id is not null).Distinct();
        var idParam = string.Join(",", ids!);
        if (string.IsNullOrEmpty(idParam)) return empty;

        try
        {
            var url =
                $"https://api.coingecko.com/api/v3/simple/price?ids={idParam}&vs_currencies=usd&include_24hr_change=true";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return empty;

            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var byId = CoinIds.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);
            var map = new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!byId.TryGetValue(prop.Name, out var symbol)) continue;
                var usd = prop.Value.TryGetProperty("usd", out var u) ? u.GetDecimal() : 0m;
                var ch = prop.Value.TryGetProperty("usd_24h_change", out var c) ? c.GetDecimal() : 0m;
                map[symbol] = (usd, ch);
            }

            return map;
        }
        catch
        {
            return empty;
        }
    }

    private static async Task<Dictionary<string, (decimal, decimal)>> TryBinanceAsync(
        List<string> list,
        CancellationToken ct)
    {
        var map = new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase);
        var pairs = list
            .Select(s => (Symbol: s, Pair: BinancePairs.GetValueOrDefault(s)))
            .Where(x => x.Pair is not null)
            .ToList();
        if (pairs.Count == 0) return map;

        try
        {
            var quoted = string.Join(",", pairs.Select(p => $"\"{p.Pair}\""));
            var url = "https://api.binance.com/api/v3/ticker/24hr?symbols=" +
                      Uri.EscapeDataString("[" + quoted + "]");

            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return map;

            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var byPair = pairs.ToDictionary(p => p.Pair!, p => p.Symbol, StringComparer.OrdinalIgnoreCase);
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                var pair = row.GetProperty("symbol").GetString();
                if (pair is null || !byPair.TryGetValue(pair, out var symbol)) continue;
                var price = decimal.Parse(
                    row.GetProperty("lastPrice").GetString()!, CultureInfo.InvariantCulture);
                var change = decimal.Parse(
                    row.GetProperty("priceChangePercent").GetString()!, CultureInfo.InvariantCulture);
                map[symbol] = (price, change);
            }

            return map;
        }
        catch
        {
            return map;
        }
    }
}

/// <summary>Local watch-only addresses linked from external wallets / explorers.</summary>
public sealed class WatchAddressStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public WatchAddressStore(string? path = null)
    {
        _path = path ?? AppPaths.WatchAddressesFile;
    }

    public async Task<IReadOnlyList<WatchAddress>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return Array.Empty<WatchAddress>();
        await using var stream = File.OpenRead(_path);
        var rows = await JsonSerializer.DeserializeAsync<List<WatchAddress>>(stream, JsonOptions, ct);
        return rows ?? [];
    }

    public async Task SaveAsync(IEnumerable<WatchAddress> rows, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, rows.ToList(), JsonOptions, ct);
    }
}

public sealed record WatchAddress(string Chain, string Address, string Label);
