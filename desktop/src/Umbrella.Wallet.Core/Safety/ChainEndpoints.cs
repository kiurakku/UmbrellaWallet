using System.Collections.Concurrent;

namespace Umbrella.Wallet.Core.Safety;

/// <summary>One server that can answer for a chain, offered as a choice.</summary>
/// <param name="BaseUrl">The API root, exactly as the adapter will use it — no trailing slash.</param>
/// <param name="Label">Who runs it, in the user's words.</param>
public sealed record ChainEndpointOption(string BaseUrl, string Label)
{
    public string Host => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : BaseUrl;
}

/// <summary>Why a URL was rejected, so the UI can say something better than "invalid".</summary>
public enum EndpointRejection
{
    None,
    Empty,
    NotAUrl,
    /// <summary>Plain http:// to somebody else's machine. The wallet refuses to send addresses in clear.</summary>
    InsecureScheme,
    /// <summary>Credentials embedded in the URL. They would be logged by every hop.</summary>
    CredentialsInUrl,
    /// <summary>A query string. An API root is a path, not a request.</summary>
    HasQuery,
}

/// <summary>
/// Which server answers for each chain — and the user's right to change it.
///
/// The Monero node picker made this choice visible for one coin. It is the same choice on every other
/// chain, and on the transparent ones it is heavier: to ask "what is the balance of bc1q…", the wallet
/// has to SAY the address. Whoever answers learns that the address belongs to a wallet, and can tie
/// together every address asked about in one session. Tor hides the IP; it does not un-send the
/// address. The only real fix is being able to point the wallet at a server you trust — your own
/// Electrum or Esplora instance, a friend's, or simply a different company than the default.
///
/// Overrides live here rather than in each adapter because the adapters are built through static
/// factories from a dozen call sites. Same shape as <see cref="Umbrella.Wallet.Core"/>'s other
/// process-wide switches: set once at startup, read everywhere.
/// </summary>
public static class ChainEndpoints
{
    private static readonly ConcurrentDictionary<string, string> Overrides =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Public alternatives per chain, offered as a starting point. None is endorsed; each is named so
    /// the choice is about a company rather than a URL. The first is what the wallet used before this
    /// was configurable, so an untouched setting changes nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<ChainEndpointOption>> Known { get; } =
        new Dictionary<string, IReadOnlyList<ChainEndpointOption>>(StringComparer.OrdinalIgnoreCase)
        {
            // Three independent Esplora instances. Blockstream is the shipped default only because it
            // always has been; it was returning 429 while these two answered, which is the everyday
            // reason to have somewhere else to point besides privacy.
            ["BTC"] =
            [
                new("https://blockstream.info/api", "Blockstream"),
                new("https://mempool.space/api", "mempool.space"),
                new("https://mempool.emzy.de/api", "mempool.emzy.de"),
            ],
            ["LTC"] =
            [
                new("https://litecoinspace.org/api", "litecoinspace"),
            ],
            ["BCH"] =
            [
                new("https://api.haskoin.com", "Haskoin"),
            ],
            ["DOGE"] =
            [
                new("https://api.blockcypher.com", "BlockCypher"),
            ],
            ["ETH"] =
            [
                new("https://cloudflare-eth.com", "Cloudflare"),
                new("https://eth.drpc.org", "dRPC"),
                new("https://rpc.ankr.com/eth", "Ankr"),
            ],
            ["SOL"] =
            [
                new("https://api.mainnet-beta.solana.com", "Solana Foundation"),
            ],
            ["TON"] =
            [
                new("https://toncenter.com", "toncenter"),
            ],
            ["TRX"] =
            [
                new("https://api.trongrid.io", "TronGrid"),
            ],
            ["ADA"] =
            [
                new("https://api.koios.rest", "Koios"),
            ],
            // XRP Ledger JSON-RPC. The XRPL Labs cluster load-balances community nodes; Ripple runs the
            // other two. Any rippled or Clio server you run yourself works the same way.
            // Cosmos SDK REST (LCD), two independent operators. cosmos.directory also answers, but it is
            // already the THORChain swap route; keeping balances off it keeps the two apart.
            ["ATOM"] =
            [
                new("https://cosmos-rest.publicnode.com", "PublicNode (Allnodes)"),
                new("https://lcd-cosmoshub.keplr.app", "Keplr"),
            ],
            // Horizon, Stellar's REST API. SDF runs the default; LOBSTR runs a public one too.
            ["XLM"] =
            [
                new("https://horizon.stellar.org", "Stellar Development Foundation"),
                new("https://horizon.stellar.lobstr.co", "LOBSTR"),
            ],
            ["XRP"] =
            [
                new("https://xrplcluster.com", "XRPL Labs cluster"),
                new("https://s1.ripple.com:51234", "Ripple"),
                new("https://s2.ripple.com:51234", "Ripple (full history)"),
            ],
        };

    /// <summary>The chains whose endpoint this build can actually redirect.</summary>
    public static IReadOnlyList<string> Configurable { get; } =
        Known.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>The user's chosen base URL for a chain, or null when they have not chosen one.</summary>
    public static string? OverrideFor(string symbol) =>
        Overrides.TryGetValue(symbol, out var url) ? url : null;

    /// <summary>The base URL to actually use: the override when set, otherwise the caller's default.</summary>
    public static string Resolve(string symbol, string fallback) =>
        OverrideFor(symbol) ?? fallback;

    /// <summary>
    /// Records a choice. An empty or null URL clears it, putting the chain back on the shipped
    /// default — there must always be a way back from a server that stopped answering.
    /// </summary>
    /// <exception cref="ArgumentException">The URL is one the wallet will not send addresses to.</exception>
    public static void SetOverride(string symbol, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Overrides.TryRemove(symbol, out _);
            return;
        }

        var rejection = Validate(baseUrl, out var normalised);
        if (rejection != EndpointRejection.None)
            throw new ArgumentException($"Endpoint rejected: {rejection}", nameof(baseUrl));

        Overrides[symbol] = normalised;
    }

    /// <summary>Forgets every override. Used when switching wallets and by tests.</summary>
    public static void ClearAll() => Overrides.Clear();

    /// <summary>
    /// Whether a URL is one the wallet is willing to hand addresses to, and the tidied form if so.
    ///
    /// Plain <c>http://</c> is refused for anything but loopback and .onion. An address sent in clear
    /// is readable by every network in between, which defeats the entire point of choosing your own
    /// server — and somebody pasting an http:// endpoint is the person least likely to notice.
    /// Loopback is your own machine, and .onion is end-to-end encrypted by Tor itself.
    /// </summary>
    public static EndpointRejection Validate(string? baseUrl, out string normalised)
    {
        normalised = string.Empty;
        var text = (baseUrl ?? string.Empty).Trim();
        if (text.Length == 0) return EndpointRejection.Empty;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return EndpointRejection.NotAUrl;
        if (uri.Scheme is not ("http" or "https")) return EndpointRejection.NotAUrl;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return EndpointRejection.CredentialsInUrl;
        if (!string.IsNullOrEmpty(uri.Query)) return EndpointRejection.HasQuery;

        var host = uri.Host;
        if (host.Length == 0) return EndpointRejection.NotAUrl;

        var exemptFromTls =
            host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host is "127.0.0.1" or "::1";

        if (uri.Scheme == "http" && !exemptFromTls) return EndpointRejection.InsecureScheme;

        normalised = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return EndpointRejection.None;
    }

    /// <summary>True when a chain is being read through something other than the shipped default —
    /// worth showing, because it is the user's own decision and they should be able to see it.</summary>
    public static bool IsCustomised(string symbol) => OverrideFor(symbol) is not null;

    /// <summary>Every override, as "SYMBOL=url" pairs, for persisting to settings.</summary>
    public static string Serialise() =>
        string.Join(";", Overrides
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key + "=" + kv.Value));

    /// <summary>
    /// Restores overrides from settings. Anything unparseable or no longer acceptable is DROPPED
    /// rather than carried forward: a settings file is editable by hand, and an endpoint that fails
    /// validation today must not keep receiving addresses because it passed yesterday.
    /// </summary>
    public static void Restore(string? serialised)
    {
        Overrides.Clear();
        if (string.IsNullOrWhiteSpace(serialised)) return;

        foreach (var pair in serialised.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;

            var symbol = pair[..eq].Trim();
            var url = pair[(eq + 1)..].Trim();
            if (symbol.Length == 0) continue;

            try { SetOverride(symbol, url); }
            catch (ArgumentException) { /* refuse quietly; the default takes over */ }
        }
    }
}
