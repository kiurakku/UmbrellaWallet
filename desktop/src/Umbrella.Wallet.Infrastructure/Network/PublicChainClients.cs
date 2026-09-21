using System.Globalization;
using System.Numerics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;

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
    private static HttpClient _shared = Build(null, _requireProxy);

    public static HttpClient Shared => _shared;

    /// <summary>
    /// What a request is for. Over Tor, each purpose gets its own circuit.
    ///
    /// Tor keys a circuit on the SOCKS5 username/password pair, so handing a different pair per
    /// purpose is all it takes. Without a proxy every purpose shares one client, because isolation
    /// buys nothing there and extra connections cost something.
    ///
    /// The separation that matters most is <see cref="Broadcast"/>. On one circuit, a single exit
    /// relay sees the wallet ask an explorer about an address AND, minutes later, hand over a
    /// transaction spending it - and can tie the two together by timing. On separate circuits the
    /// relay that saw the address is not the relay that saw the broadcast.
    /// </summary>
    public enum NetworkPurpose
    {
        /// <summary>Balances, unspent outputs, history. These carry the user's addresses.</summary>
        ChainData,
        /// <summary>Coin prices and fiat rates. No addresses, but they reveal what is held.</summary>
        Prices,
        /// <summary>Handing a signed transaction to the network.</summary>
        Broadcast,
        /// <summary>Swap quotes and routing.</summary>
        Swaps,
        /// <summary>Update checks and the Tor self-test.</summary>
        Maintenance,
        /// <summary>An exchange account the user connected. This carries their own API credentials -
        /// the most identity-linked traffic the wallet makes, and the last thing that should share a
        /// circuit with address lookups.</summary>
        ExchangeAccount,
        /// <summary>A PayJoin receiver's endpoint (BIP-78). It is handed a signed payment naming every
        /// input, so it gets a circuit of its own rather than the one the explorer sees broadcasts on.</summary>
        Payjoin,
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<NetworkPurpose, HttpClient>
        Isolated = new();

    /// <summary>
    /// The client for a purpose. Over Tor this is a separate circuit; without a proxy it is the
    /// shared client.
    ///
    /// Every client handed out here is built from the SAME proxy and kill-switch state as the shared
    /// one, and all of them are torn down whenever that state changes. A cached client that outlived
    /// a kill-switch being armed would keep connecting - a hole in the exact mechanism the kill-switch
    /// exists to be, and worse than not isolating at all.
    /// </summary>
    public static HttpClient For(NetworkPurpose purpose)
    {
        var proxy = ActiveProxy;
        if (string.IsNullOrWhiteSpace(proxy)) return _shared;

        return Isolated.GetOrAdd(purpose, p => Build(IsolatedProxyUri(proxy!, p), _requireProxy));
    }

    /// <summary>
    /// The proxy URI with per-purpose SOCKS5 credentials. Tor treats a distinct username/password as
    /// a distinct circuit; the values are fixed labels, not secrets, and never leave the loopback
    /// connection to Tor itself.
    /// </summary>
    private static string IsolatedProxyUri(string socks5Uri, NetworkPurpose purpose)
    {
        try
        {
            var uri = new Uri(socks5Uri);
            var label = purpose.ToString().ToLowerInvariant();
            return $"{uri.Scheme}://umbrella-{label}:{label}@{uri.Host}:{uri.Port}";
        }
        catch
        {
            // A proxy string we cannot parse is used as-is rather than dropped: losing the proxy
            // would send the request direct, which is the one outcome that must never happen here.
            return socks5Uri;
        }
    }

    /// <summary>Disposes every per-purpose client. Called whenever the routing changes, so no client
    /// can outlive the settings it was built from.</summary>
    private static void DiscardIsolated()
    {
        foreach (var key in Isolated.Keys.ToList())
        {
            if (Isolated.TryRemove(key, out var client))
            {
                try { client.Dispose(); } catch { /* ignore */ }
            }
        }
    }

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
        _shared = Build(ActiveProxy, _requireProxy);
        DiscardIsolated();
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
        _shared = Build(normalized, _requireProxy);
        ActiveProxy = normalized;
        DiscardIsolated();
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
        _shared = Build(ActiveProxy, _requireProxy);
        DiscardIsolated();
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

    /// <summary>
    /// Builds an ISOLATED client wired exactly like <see cref="Build"/> would wire the shared client
    /// for the given proxy + kill-switch state — for a self-test that proves, on the real transport,
    /// that Tor-only mode with no proxy refuses a direct clearnet connection rather than leaking it.
    /// Touches no shared or global state, so it's safe to run anytime (including from tests) without
    /// disturbing live traffic. This runs the SAME production wiring the shared client uses.
    /// </summary>
    public static HttpClient CreateProbeClient(string? proxy, bool requireProxy) => Build(proxy, requireProxy);

    private static HttpClient Build(string? socks5Uri, bool requireProxy)
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
        else if (requireProxy)
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
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);

    /// <summary>
    /// The Ethereum RPCs to try, with the user's chosen server first when they have picked one.
    ///
    /// The shipped fallbacks stay BEHIND it rather than being replaced: a chosen server that stops
    /// answering should leave the wallet reading balances, not blank. What must never happen is the
    /// default quietly becoming first choice again, so the override is prepended and then filtered out
    /// of the rest.
    /// </summary>
    private static IEnumerable<string> EffectiveEthRpcs
    {
        get
        {
            var chosen = ChainEndpoints.OverrideFor("ETH");
            if (chosen is not null) yield return chosen;
            foreach (var fallback in EthRpcs)
            {
                if (!string.Equals(fallback, chosen, StringComparison.OrdinalIgnoreCase)) yield return fallback;
            }
        }
    }

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
                ChainId.Bch => await GetHaskoinAsync("bch", ChainId.Bch, "BCH", address, cancellationToken),
                ChainId.Zec => await GetZecAsync(address, cancellationToken),
                ChainId.Eth => await GetEthAsync(address, cancellationToken),
                ChainId.Tron => await GetTronAsync(address, cancellationToken),
                ChainId.Sol => await GetSolAsync(address, cancellationToken),
                ChainId.Ton => await GetTonAsync(address, cancellationToken),
                ChainId.Ada => await GetAdaAsync(address, cancellationToken),
                ChainId.Xrp => await GetXrpAsync(address, cancellationToken),
                ChainId.Xlm => await GetXlmAsync(address, cancellationToken),
                ChainId.Atom => await GetAtomAsync(address, cancellationToken),
                ChainId.Near => await GetNearAsync(address, cancellationToken),
                ChainId.Dot => await GetDotAsync(address, cancellationToken),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The XRP Ledger JSON-RPC root: the user's chosen server, or the XRPL Labs cluster.</summary>
    private static string XrpRoot => ChainEndpoints.Resolve("XRP", "https://xrplcluster.com");

    /// <summary>
    /// XRP balance from <c>account_info</c> at the last validated ledger (roadmap N.4). An address the
    /// ledger does not know yet is a real zero; any other failure is unknown — see
    /// <see cref="XrpLedger.ParseAccountInfo"/>.
    /// </summary>
    private static async Task<ChainBalance?> GetXrpAsync(string address, CancellationToken ct)
    {
        using var res = await Http.PostAsJsonAsync(XrpRoot, XrpLedger.AccountInfoRequest(address), ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("result", out var result)) return null;

        return XrpLedger.ParseAccountInfo(result) is { } xrp
            ? new ChainBalance(ChainId.Xrp, address, xrp, "XRP")
            : null;
    }

    private static string DotAssetHubRoot => ChainEndpoints.Resolve("DOT", "https://polkadot-asset-hub-rpc.polkadot.io");
    private static string DotRelayRoot => ChainEndpoints.Resolve("DOT-RELAY", "https://rpc.polkadot.io");

    /// <summary>
    /// DOT on the account, Asset Hub and relay chain together (roadmap N.8). Both must answer: half a
    /// balance is not a balance, so one failed read makes the whole thing unknown.
    /// </summary>
    private static async Task<ChainBalance?> GetDotAsync(string address, CancellationToken ct)
    {
        if (!Umbrella.Wallet.Core.Polkadot.Ss58.TryDecode(address, out var prefix, out var accountId) ||
            prefix != Umbrella.Wallet.Core.Polkadot.Ss58.PolkadotPrefix)
            return null;

        var key = Umbrella.Wallet.Core.Polkadot.PolkadotAccounts.SystemAccountKey(accountId);
        var hub = await ReadDotAccountAsync(DotAssetHubRoot, key, ct);
        if (hub is null) return null;
        var relay = await ReadDotAccountAsync(DotRelayRoot, key, ct);
        if (relay is null) return null;

        return new ChainBalance(ChainId.Dot, address, hub.Value + relay.Value, "DOT");
    }

    /// <summary>System.Account at the FINALIZED head of one chain — never a block that can still be
    /// reorganised away.</summary>
    private static async Task<decimal?> ReadDotAccountAsync(string root, string storageKey, CancellationToken ct)
    {
        using var headRes = await Http.PostAsJsonAsync(root,
            new { id = 1, jsonrpc = "2.0", method = "chain_getFinalizedHead", @params = Array.Empty<string>() }, ct);
        if (!headRes.IsSuccessStatusCode) return null;
        using var headDoc = await JsonDocument.ParseAsync(await headRes.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!headDoc.RootElement.TryGetProperty("result", out var h) || h.ValueKind != JsonValueKind.String) return null;

        using var res = await Http.PostAsJsonAsync(root,
            new { id = 2, jsonrpc = "2.0", method = "state_getStorage", @params = new[] { storageKey, h.GetString()! } }, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        // An "error" member instead of "result" is a failed read, not an empty account.
        if (!doc.RootElement.TryGetProperty("result", out var r)) return null;
        return r.ValueKind switch
        {
            JsonValueKind.Null => Umbrella.Wallet.Core.Polkadot.PolkadotAccounts.ParseAccountInfo(null, entryMissing: true),
            JsonValueKind.String => Umbrella.Wallet.Core.Polkadot.PolkadotAccounts.ParseAccountInfo(r.GetString(), entryMissing: false),
            _ => null,
        };
    }

    /// <summary>The NEAR JSON-RPC root: the user's chosen server, or the NEAR Foundation's.</summary>
    private static string NearRoot => ChainEndpoints.Resolve("NEAR", "https://rpc.mainnet.near.org");

    /// <summary>NEAR balance of the implicit account at final finality (roadmap N.7).</summary>
    private static async Task<ChainBalance?> GetNearAsync(string address, CancellationToken ct)
    {
        if (!NearAccounts.IsImplicitAccountId(address)) return null;

        using var res = await Http.PostAsJsonAsync(NearRoot, NearAccounts.ViewAccountRequest(address), ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        return NearAccounts.ParseViewAccount(doc.RootElement) is { } near
            ? new ChainBalance(ChainId.Near, address, near, "NEAR")
            : null;
    }

    /// <summary>The Cosmos Hub REST root: the user's chosen server, or PublicNode.</summary>
    private static string AtomRoot => ChainEndpoints.Resolve("ATOM", "https://cosmos-rest.publicnode.com");

    /// <summary>Available (not staked) ATOM from the bank module (roadmap N.6).</summary>
    private static async Task<ChainBalance?> GetAtomAsync(string address, CancellationToken ct)
    {
        if (!CosmosHub.IsValidAddress(address)) return null;

        using var res = await Http.GetAsync(
            $"{AtomRoot}/cosmos/bank/v1beta1/balances/{address}/by_denom?denom={CosmosHub.Denom}", ct);
        JsonDocument? doc = null;
        try
        {
            if (res.IsSuccessStatusCode)
                doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            return CosmosHub.ParseBalance((int)res.StatusCode, doc?.RootElement) is { } atom
                ? new ChainBalance(ChainId.Atom, address, atom, "ATOM")
                : null;
        }
        finally
        {
            doc?.Dispose();
        }
    }

    /// <summary>Stellar's Horizon root: the user's chosen server, or the SDF's.</summary>
    private static string XlmRoot => ChainEndpoints.Resolve("XLM", "https://horizon.stellar.org");

    /// <summary>
    /// Native XLM balance from Horizon (roadmap N.5). A 404 is an address nobody has funded yet — a
    /// real zero; any other failure is unknown. See <see cref="StellarHorizon.ParseAccount"/>.
    /// </summary>
    private static async Task<ChainBalance?> GetXlmAsync(string address, CancellationToken ct)
    {
        if (!StellarKeys.IsValidAccountId(address)) return null;

        using var res = await Http.GetAsync($"{XlmRoot}/accounts/{address}", ct);
        JsonDocument? doc = null;
        try
        {
            if (res.IsSuccessStatusCode)
                doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            return StellarHorizon.ParseAccount((int)res.StatusCode, doc?.RootElement) is { } xlm
                ? new ChainBalance(ChainId.Xlm, address, xlm, "XLM")
                : null;
        }
        finally
        {
            doc?.Dispose();
        }
    }

    /// <summary>The TON index root: the user's chosen server, or toncenter.</summary>
    private static string TonRoot => ChainEndpoints.Resolve("TON", "https://toncenter.com");

    /// <summary>TON native balance via toncenter (returns nanoTON as a string).</summary>
    private static async Task<ChainBalance?> GetTonAsync(string address, CancellationToken ct)
    {
        using var res = await Http.GetAsync(
            $"{TonRoot}/api/v2/getAddressBalance?address={Uri.EscapeDataString(address)}", ct);
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
        using var res = await Http.PostAsync(
            $"{ChainEndpoints.Resolve("ADA", "https://api.koios.rest")}/api/v1/address_info", body, ct);
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

    /// <summary>
    /// Native balance for a Haskoin-served UTXO chain (BCH). Keyless and reliable — Blockchair's free
    /// tier IP-blacklists a busy caller (HTTP 430) and demands a key, so BCH balance and UTXOs go through
    /// Haskoin instead. Haskoin takes a CashAddr with or without the "bitcoincash:" prefix; we strip it so
    /// the ':' never has to be URL-encoded into the path. Returns the CONFIRMED balance in satoshi.
    /// </summary>
    private static async Task<ChainBalance?> GetHaskoinAsync(
        string coin, ChainId chain, string symbol, string address, CancellationToken ct)
    {
        var query = address.StartsWith("bitcoincash:", StringComparison.OrdinalIgnoreCase)
            ? address["bitcoincash:".Length..]
            : address;
        using var res = await Http.GetAsync(
            $"https://api.haskoin.com/{coin}/address/{Uri.EscapeDataString(query)}/balance", ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("confirmed", out var c) || !c.TryGetInt64(out var sats)) return null;
        return new ChainBalance(chain, address, sats / 100_000_000m, symbol);
    }

    /// <summary>
    /// Zcash transparent balance. There is no single reliable keyless ZEC endpoint (Haskoin has no ZEC,
    /// and Blockchair rate-limits), so we try Trezor's public Blockbook first and fall back to Blockchair.
    /// If both are unreachable the balance simply reads as unknown (null) rather than wrong — never 0.
    /// </summary>
    private static async Task<ChainBalance?> GetZecAsync(string address, CancellationToken ct) =>
        await GetBlockbookAsync("https://zec1.trezor.io", ChainId.Zec, "ZEC", address, 8, ct)
        ?? await GetBlockchairAsync("zcash", ChainId.Zec, "ZEC", address, address, ct);

    /// <summary>Balance from a Blockbook v2 explorer (Trezor's public instances). The balance is a
    /// string in the coin's smallest unit; unconfirmed is reported separately and not counted here.</summary>
    private static async Task<ChainBalance?> GetBlockbookAsync(
        string baseUrl, ChainId chain, string symbol, string address, int decimals, CancellationToken ct)
    {
        using var res = await Http.GetAsync(
            $"{baseUrl}/api/v2/address/{Uri.EscapeDataString(address)}?details=basic", ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("balance", out var b)) return null;
        var raw = b.ValueKind == JsonValueKind.String ? b.GetString() : b.GetRawText();
        if (!System.Numerics.BigInteger.TryParse(raw, out var sats)) return null;
        return new ChainBalance(chain, address, (decimal)sats / (decimal)Math.Pow(10, decimals), symbol);
    }

    /// <summary>
    /// Native balance from Blockchair's keyless dashboards endpoint. Kept only as a FALLBACK (for ZEC):
    /// Blockchair's free tier IP-blacklists a busy caller (HTTP 430), so it is never a primary source.
    /// It keys the "data" object by the exact address queried, so we read the single returned entry.
    /// </summary>
    private static async Task<ChainBalance?> GetBlockchairAsync(
        string blockchairChain, ChainId chain, string symbol,
        string queryAddress, string reportAddress, CancellationToken ct)
    {
        using var res = await Http.GetAsync(
            $"https://api.blockchair.com/{blockchairChain}/dashboards/address/{Uri.EscapeDataString(queryAddress)}", ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var entry in data.EnumerateObject())
        {
            if (!entry.Value.TryGetProperty("address", out var addr)) continue;
            if (!addr.TryGetProperty("balance", out var bal)) continue;
            var sats = bal.ValueKind == JsonValueKind.Number && bal.TryGetInt64(out var s) ? s : 0L;
            return new ChainBalance(chain, reportAddress, sats / 100_000_000m, symbol);
        }
        return null;
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
        foreach (var rpc in EffectiveEthRpcs)
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
    // L2s (Arbitrum / Optimism / Base / Linea / zkSync Era) use ETH as their native coin, shown
    // per-network.
    //
    // CanSend says whether this build can actually broadcast on that network, and it is NOT decoration:
    // a balance the wallet can read but not spend has to say so, or the holdings list quietly promises
    // a send that the Send screen will not offer. Reading a balance is a GET; spending needs a signer
    // whose fee model matches the chain, which is a much higher bar.
    private static readonly (string Symbol, string Network, bool CanSend, string[] Rpcs)[] EvmSideChains =
    [
        ("BNB",   "BSC",        true,  ["https://bsc-dataseed.binance.org", "https://bsc-dataseed1.defibit.io", "https://rpc.ankr.com/bsc"]),
        ("MATIC", "Polygon",    true,  ["https://polygon-rpc.com", "https://rpc.ankr.com/polygon"]),
        ("AVAX",  "Avalanche",  true,  ["https://api.avax.network/ext/bc/C/rpc", "https://rpc.ankr.com/avalanche"]),
        ("FTM",   "Fantom",     true,  ["https://rpc.ftm.tools", "https://rpc.ankr.com/fantom"]),
        ("CRO",   "Cronos",     true,  ["https://evm.cronos.org", "https://cronos-evm-rpc.publicnode.com"]),
        ("ETH",   "Arbitrum",   true,  ["https://arb1.arbitrum.io/rpc", "https://rpc.ankr.com/arbitrum"]),
        ("ETH",   "Optimism",   true,  ["https://mainnet.optimism.io", "https://rpc.ankr.com/optimism"]),
        ("ETH",   "Base",       true,  ["https://mainnet.base.org", "https://base.publicnode.com"]),
        // Linea is EVM-equivalent: EIP-155 signing and the same 21,000 intrinsic gas as mainnet, so
        // the existing signer covers it unchanged and sending is enabled alongside the balance.
        ("ETH",   "Linea",      true,  ["https://rpc.linea.build", "https://linea.drpc.org"]),
        // zkSync Era is READ ONLY here on purpose. Its balances are a normal eth_getBalance, but its
        // fee model is not Ethereum's: a plain transfer does not cost a flat 21,000 gas, so the shared
        // gas limit this wallet signs with would strand the transaction. Sending stays off until the
        // gas comes from the chain rather than from a constant.
        ("ETH",   "zkSync Era", false, ["https://mainnet.era.zksync.io", "https://zksync.drpc.org"]),
    ];

    /// <summary>
    /// Native balances of every major EVM network (BNB/MATIC/AVAX/FTM/CRO, plus ETH on the Arbitrum,
    /// Optimism, Base, Linea and zkSync Era L2s) at the SAME 0x address, so a MetaMask-imported wallet
    /// shows all of them — not just Ethereum mainnet. Queried in parallel; only non-zero balances are
    /// returned, each carrying whether this build can spend it as well as read it.
    /// </summary>
    public async Task<IReadOnlyList<(string Symbol, decimal Amount, string Network, bool CanSend)>> GetEvmSideBalancesAsync(
        string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address) || !address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return [];

        var tasks = EvmSideChains.Select(async chain =>
        {
            var amount = await EvmNativeBalanceAsync(chain.Rpcs, address, cancellationToken);
            return (chain.Symbol, Amount: amount ?? 0m, chain.Network, chain.CanSend);
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(r => r.Amount > 0m).ToList();
    }

    /// <summary>The EVM networks this build reads a native balance for, and whether it can spend each.
    /// Exposed so the UI and the tests read the same list the balance fetch does.</summary>
    public static IReadOnlyList<(string Symbol, string Network, bool CanSend)> EvmSideNetworks =>
        EvmSideChains.Select(c => (c.Symbol, c.Network, c.CanSend)).ToList();

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
        using var res = await Http.PostAsJsonAsync(
            ChainEndpoints.Resolve("SOL", "https://api.mainnet-beta.solana.com"), payload, ct);
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

    /// <summary>
    /// Every Jetton held at a TON address — USD&#8377; on TON above all, which is how a great many people
    /// actually hold dollars on Telegram's chain. Keyless, via toncenter's v3 index.
    /// </summary>
    public async Task<IReadOnlyList<TokenBalance>> GetTonJettonsAsync(
        string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address)) return [];
        var a = address.Trim();
        if (!a.StartsWith("UQ", StringComparison.Ordinal) && !a.StartsWith("EQ", StringComparison.Ordinal))
            return [];

        try
        {
            using var res = await Http.GetAsync(
                $"{TonRoot}/api/v3/jetton/wallets" +
                $"?owner_address={Uri.EscapeDataString(a)}&limit=50&offset=0",
                cancellationToken);
            if (!res.IsSuccessStatusCode) return [];

            return ParseTonJettons(await res.Content.ReadAsStringAsync(cancellationToken));
        }
        catch
        {
            return [];   // treated as "no jettons", never as zero balance
        }
    }

    /// <summary>
    /// Turns a toncenter v3 <c>jetton/wallets</c> payload into token balances. Pure, so the shape of a
    /// real response can be pinned by tests rather than discovered in production.
    ///
    /// Two details in this payload lose money if they are read carelessly:
    ///
    /// The balance and the DECIMALS both arrive as strings, and the decimals live in the jetton
    /// master's metadata rather than on the wallet row — so the amount has to be joined across two
    /// parts of the document. USD&#8377; on TON has 6 decimals while most jettons have 9; reading one as
    /// the other is wrong by a factor of a thousand.
    ///
    /// A jetton whose metadata is missing entirely is SKIPPED rather than guessed at. An unnamed row
    /// with an assumed 9 decimals would be a number the user cannot check against anything.
    /// </summary>
    public static IReadOnlyList<TokenBalance> ParseTonJettons(string json, int max = 40)
    {
        var result = new List<TokenBalance>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("jetton_wallets", out var wallets) ||
                wallets.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            root.TryGetProperty("metadata", out var metadata);
            root.TryGetProperty("address_book", out var addressBook);

            foreach (var wallet in wallets.EnumerateArray())
            {
                if (result.Count >= max) break;

                var master = wallet.TryGetProperty("jetton", out var j) ? j.GetString() : null;
                var rawBalance = wallet.TryGetProperty("balance", out var b) ? b.GetString() : null;
                if (string.IsNullOrWhiteSpace(master) || string.IsNullOrWhiteSpace(rawBalance)) continue;
                if (!BigInteger.TryParse(rawBalance, out var raw) || raw <= 0) continue;

                var info = MasterInfo(metadata, master!);
                if (info is null) continue;   // no metadata = nothing trustworthy to show

                var (symbol, name, decimals) = info.Value;
                if (string.IsNullOrWhiteSpace(symbol)) continue;

                decimal divisor = 1m;
                for (var i = 0; i < Math.Clamp(decimals, 0, 28); i++) divisor *= 10m;

                decimal amount;
                try { amount = (decimal)raw / divisor; }
                catch (OverflowException) { continue; }
                if (amount <= 0) continue;

                // The user-friendly EQ… form of the master is what a TON explorer shows, so it is the
                // contract string a user can actually look up; fall back to the raw form.
                var contract = master!;
                if (addressBook.ValueKind == JsonValueKind.Object &&
                    addressBook.TryGetProperty(master!, out var entry) &&
                    entry.TryGetProperty("user_friendly", out var friendly) &&
                    friendly.ValueKind == JsonValueKind.String)
                {
                    contract = friendly.GetString() ?? master!;
                }

                // The wallet row's own address is this owner's jetton wallet — the contract a
                // transfer is sent to (roadmap N.3). Without it the token is display-only.
                var jettonWallet = wallet.TryGetProperty("address", out var wa) ? wa.GetString() ?? "" : "";
                if (jettonWallet.Length > 0 &&
                    addressBook.ValueKind == JsonValueKind.Object &&
                    addressBook.TryGetProperty(jettonWallet, out var walletEntry) &&
                    walletEntry.TryGetProperty("user_friendly", out var walletFriendly) &&
                    walletFriendly.ValueKind == JsonValueKind.String)
                {
                    jettonWallet = walletFriendly.GetString() ?? jettonWallet;
                }

                result.Add(new TokenBalance(
                    NormaliseJettonSymbol(symbol!), string.IsNullOrWhiteSpace(name) ? symbol! : name!,
                    amount, contract, decimals, jettonWallet));
            }
        }
        catch
        {
            // malformed payload = no jettons
        }

        return result;
    }

    /// <summary>
    /// Tether on TON calls itself "USD₮" — with the tugrik sign standing in for the T, the way the
    /// brand writes it. Left as-is, that is a symbol no price feed has ever heard of, so the balance
    /// would render at $0 and land squarely in the "my USDT is missing" pile the guide already has a
    /// section about. The glyph is folded back to a plain T; the display name keeps whatever the token
    /// actually calls itself.
    /// </summary>
    private static string NormaliseJettonSymbol(string symbol) =>
        symbol.Replace('₮', 'T').Trim().ToUpperInvariant();

    /// <summary>Symbol, name and decimals for a jetton master, from the payload's metadata block.
    /// Null when the master is not described there at all.</summary>
    private static (string? Symbol, string? Name, int Decimals)? MasterInfo(JsonElement metadata, string master)
    {
        if (metadata.ValueKind != JsonValueKind.Object) return null;
        if (!metadata.TryGetProperty(master, out var node)) return null;
        if (!node.TryGetProperty("token_info", out var infos) || infos.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var info in infos.EnumerateArray())
        {
            var type = info.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (!string.Equals(type, "jetton_masters", StringComparison.Ordinal)) continue;

            var symbol = info.TryGetProperty("symbol", out var sy) ? sy.GetString() : null;
            var name = info.TryGetProperty("name", out var nm) ? nm.GetString() : null;

            // Decimals arrive as a STRING inside "extra", and are absent for jettons that use TON's
            // default of 9. Absent is fine; unparseable is not, and is treated as absent rather than
            // silently becoming zero — which would multiply the displayed balance by a billion.
            var decimals = 9;
            if (info.TryGetProperty("extra", out var extra) &&
                extra.ValueKind == JsonValueKind.Object &&
                extra.TryGetProperty("decimals", out var d))
            {
                if (d.ValueKind == JsonValueKind.String && int.TryParse(d.GetString(), out var parsed))
                    decimals = parsed;
                else if (d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var asNumber))
                    decimals = asNumber;
            }

            if (decimals is < 0 or > 28) continue;   // not a jetton this wallet will put a number on

            return (symbol, name, decimals);
        }

        return null;
    }
}

/// <summary>A fungible token balance (TRC-20 / ERC-20) held at an address.</summary>
public sealed record TokenBalance(
    string Symbol, string Name, decimal Amount, string Contract, int Decimals,
    /// <summary>
    /// For a jetton: the SENDER's own jetton-wallet contract, which is what a transfer message is
    /// addressed to. The master in <see cref="Contract"/> identifies the token; it cannot receive a
    /// transfer, and sending to it would be sending tokens to the issuer.
    /// </summary>
    string TokenWallet = "");

/// <summary>An NFT collection held at an address (name + count only — no image fetch, for privacy).</summary>
public sealed record NftHolding(string Name, string Symbol, int Count, string Standard, string Network);

/// <summary>
/// USD prices from public endpoints, no API key. CoinGecko first, Binance as a fallback so a
/// single provider being down or rate-limiting does not blank the whole wallet.
/// </summary>
/// <summary>One OHLC candle for the candlestick chart.</summary>
public readonly record struct PriceCandle(double Open, double High, double Low, double Close, double Volume = 0);

public sealed class PublicMarketRatesClient
{
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.Prices);

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
        ["ZEC"] = "zcash",
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
        ["ZEC"] = "ZECUSDT",
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
                        // [openTime, open(1), high(2), low(3), close(4), volume(5), closeTime(6), quoteVolume(7), ...]
                        if (k.GetArrayLength() > 4 &&
                            double.TryParse(k[1].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var o) &&
                            double.TryParse(k[2].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var h) &&
                            double.TryParse(k[3].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var l) &&
                            double.TryParse(k[4].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var c))
                        {
                            double vol = 0;
                            if (k.GetArrayLength() > 7)
                                double.TryParse(k[7].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out vol);
                            candles.Add(new PriceCandle(o, h, l, c, vol));
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
