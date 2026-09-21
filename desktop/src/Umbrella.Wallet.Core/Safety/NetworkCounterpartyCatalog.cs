namespace Umbrella.Wallet.Core.Safety;

/// <summary>
/// Every server this wallet can talk to, declared.
///
/// The keys stay on your machine — that part is true and every wallet says it. What almost none of
/// them say is that a wallet still has to ASK somebody what is on the chain, and on a transparent
/// chain asking means handing over the very addresses you were trying to keep to yourself. Whoever
/// answers "what is the balance of bc1q…" learns that address belongs to a wallet, and can tie every
/// address asked about in one session to one person. Tor hides the IP. It does not un-send the
/// address.
///
/// So this is not a marketing page. It is the list, with what each party learns stated plainly, and
/// <c>NetworkCounterpartyTests</c> scans the Infrastructure sources and fails if a host appears in the
/// code without appearing here. A transparency page that can silently fall behind the code is worse
/// than none, because it reassures without being true.
/// </summary>
public static class NetworkCounterpartyCatalog
{
    private const CounterpartyLearns Chain =
        CounterpartyLearns.YourIpAddress
        | CounterpartyLearns.YourWalletAddresses
        | CounterpartyLearns.WhenYouAreOnline;

    private const CounterpartyLearns ChainAndBroadcast =
        Chain | CounterpartyLearns.WhereYourTransactionEntered;

    public static IReadOnlyList<NetworkCounterparty> All { get; } =
    [
        // --- Balances, unspent coins and history. These are handed your addresses. ------------------
        new("blockstream.info", "Blockstream", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "BTC"),
        // Offered in the endpoint picker, and used as a fallback when the default is rate-limited —
        // which means they are contacted for real and belong on this list like everything else.
        new("mempool.space", "mempool.space", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "BTC"),
        new("mempool.emzy.de", "mempool.emzy.de", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "BTC"),
        new("litecoinspace.org", "litecoinspace", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "LTC"),
        new("api.blockcypher.com", "BlockCypher", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "DOGE"),
        new("api.haskoin.com", "Haskoin", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "BCH"),
        new("api.blockchair.com", "Blockchair", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, Chain, "BCH, ZEC"),
        new("api.trongrid.io", "TronGrid (Tron Foundation)", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "TRX, USDT"),
        new("apilist.tronscanapi.com", "TronScan", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, Chain | CounterpartyLearns.WhichCoinsYouHold, "TRC-20 tokens"),
        new("api.mainnet-beta.solana.com", "Solana Foundation", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "SOL"),
        new("toncenter.com", "toncenter", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast | CounterpartyLearns.WhichCoinsYouHold,
            "TON, Jettons"),
        new("api.koios.rest", "Koios", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "ADA"),
        new("cosmos-rest.publicnode.com", "PublicNode (Allnodes)", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, Chain, "ATOM"),
        new("polkadot-asset-hub-rpc.polkadot.io", "Parity", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, Chain, "DOT (Asset Hub)"),
        new("statemint.api.onfinality.io", "OnFinality", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, Chain, "DOT (Asset Hub)"),
        new("rpc.polkadot.io", "Parity", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, Chain, "DOT (relay chain)"),
        new("polkadot-rpc.publicnode.com", "PublicNode (Allnodes)", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, Chain, "DOT (relay chain)"),
        new("polkadot.api.onfinality.io", "OnFinality", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, Chain, "DOT (relay chain)"),
        new("rpc.mainnet.near.org", "NEAR Foundation", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "NEAR"),
        new("free.rpc.fastnear.com", "FastNEAR", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "NEAR"),
        new("lcd-cosmoshub.keplr.app", "Keplr", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, Chain, "ATOM"),
        new("horizon.stellar.org", "Stellar Development Foundation", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "XLM"),
        new("horizon.stellar.lobstr.co", "LOBSTR", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "XLM"),
        new("xrplcluster.com", "XRPL Labs", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "XRP"),
        // Offered in the endpoint picker: contacted for real once chosen.
        new("s1.ripple.com", "Ripple", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "XRP"),
        new("s2.ripple.com", "Ripple", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "XRP"),
        new("eth.blockscout.com", "Blockscout", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, Chain | CounterpartyLearns.WhichCoinsYouHold,
            "ERC-20 tokens, NFTs"),

        // --- EVM JSON-RPC. Balance reads and broadcasts for Ethereum and everything shaped like it. --
        new("cloudflare-eth.com", "Cloudflare", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "ETH"),
        new("rpc.ankr.com", "Ankr", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "ETH, BSC, Polygon, Avalanche, Fantom, Arbitrum, Optimism"),
        new("eth.drpc.org", "dRPC", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "ETH"),
        new("bsc-dataseed.binance.org", "Binance", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "BNB"),
        new("bsc-dataseed1.defibit.io", "defibit", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "BNB"),
        new("polygon-rpc.com", "Polygon", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "MATIC"),
        new("api.avax.network", "Ava Labs", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "AVAX"),
        new("rpc.ftm.tools", "Fantom", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "FTM"),
        new("evm.cronos.org", "Cronos", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "CRO"),
        new("cronos-evm-rpc.publicnode.com", "PublicNode", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "CRO"),
        new("arb1.arbitrum.io", "Offchain Labs", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "ETH on Arbitrum"),
        new("mainnet.base.org", "Base", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "ETH on Base"),
        new("base.publicnode.com", "PublicNode", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "ETH on Base"),
        new("mainnet.optimism.io", "Optimism", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "ETH on Optimism"),
        new("rpc.linea.build", "Linea (ConsenSys)", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "ETH on Linea"),
        new("linea.drpc.org", "dRPC", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, ChainAndBroadcast, "ETH on Linea"),
        new("mainnet.era.zksync.io", "Matter Labs", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, Chain, "ETH on zkSync Era"),
        new("zksync.drpc.org", "dRPC", CounterpartyPurpose.Balances,
            CounterpartyContact.Automatic, Chain, "ETH on zkSync Era"),

        // --- Prices. No addresses here - but the symbols asked about are the coins you hold. ---------
        new("api.coingecko.com", "CoinGecko", CounterpartyPurpose.Prices,
            CounterpartyContact.Automatic,
            CounterpartyLearns.YourIpAddress | CounterpartyLearns.WhenYouAreOnline
            | CounterpartyLearns.WhichCoinsYouHold),
        new("api.binance.com", "Binance", CounterpartyPurpose.Prices,
            CounterpartyContact.Automatic,
            CounterpartyLearns.YourIpAddress | CounterpartyLearns.WhenYouAreOnline
            | CounterpartyLearns.WhichCoinsYouHold),
        new("open.er-api.com", "exchangerate-api", CounterpartyPurpose.Prices,
            CounterpartyContact.Automatic,
            CounterpartyLearns.YourIpAddress | CounterpartyLearns.WhenYouAreOnline),

        // --- Swaps. Only when you ask for a quote. ---------------------------------------------------
        new("thornode.ninerealms.com", "Nine Realms (THORChain)", CounterpartyPurpose.Swaps,
            CounterpartyContact.OnDemand, Chain | CounterpartyLearns.WhichCoinsYouHold),
        new("thornode.thorchain.liquify.com", "Liquify (THORChain)", CounterpartyPurpose.Swaps,
            CounterpartyContact.OnDemand, Chain | CounterpartyLearns.WhichCoinsYouHold),
        new("track.ninerealms.com", "Nine Realms (THORChain)", CounterpartyPurpose.Swaps,
            CounterpartyContact.OnDemand, Chain),
        new("rest.cosmos.directory", "cosmos.directory", CounterpartyPurpose.Swaps,
            CounterpartyContact.OnDemand, Chain),

        // --- Your own exchange accounts. Nothing is sent unless you connect one. ----------------------
        new("api.binance.com/exchange", "Binance", CounterpartyPurpose.ExchangeAccount,
            CounterpartyContact.OptIn, CounterpartyLearns.YourIpAddress | CounterpartyLearns.YourAccountWithThem),
        new("api.bybit.com", "Bybit", CounterpartyPurpose.ExchangeAccount,
            CounterpartyContact.OptIn, CounterpartyLearns.YourIpAddress | CounterpartyLearns.YourAccountWithThem),
        new("api.kraken.com", "Kraken", CounterpartyPurpose.ExchangeAccount,
            CounterpartyContact.OptIn, CounterpartyLearns.YourIpAddress | CounterpartyLearns.YourAccountWithThem),
        new("api.kucoin.com", "KuCoin", CounterpartyPurpose.ExchangeAccount,
            CounterpartyContact.OptIn, CounterpartyLearns.YourIpAddress | CounterpartyLearns.YourAccountWithThem),
        new("api.gateio.ws", "Gate.io", CounterpartyPurpose.ExchangeAccount,
            CounterpartyContact.OptIn, CounterpartyLearns.YourIpAddress | CounterpartyLearns.YourAccountWithThem),
        new("api.mexc.com", "MEXC", CounterpartyPurpose.ExchangeAccount,
            CounterpartyContact.OptIn, CounterpartyLearns.YourIpAddress | CounterpartyLearns.YourAccountWithThem),
        new("api.bitget.com", "Bitget", CounterpartyPurpose.ExchangeAccount,
            CounterpartyContact.OptIn, CounterpartyLearns.YourIpAddress | CounterpartyLearns.YourAccountWithThem),
        new("www.okx.com", "OKX", CounterpartyPurpose.ExchangeAccount,
            CounterpartyContact.OptIn, CounterpartyLearns.YourIpAddress | CounterpartyLearns.YourAccountWithThem),
        new("pay.crypt.bot", "Crypto Bot (Telegram)", CounterpartyPurpose.ExchangeAccount,
            CounterpartyContact.OptIn, CounterpartyLearns.YourIpAddress | CounterpartyLearns.YourAccountWithThem),

        // --- Updates and the Tor check. -------------------------------------------------------------
        new("github.com", "GitHub", CounterpartyPurpose.Updates,
            CounterpartyContact.OnDemand,
            CounterpartyLearns.YourIpAddress | CounterpartyLearns.WhenYouAreOnline),
        new("raw.githubusercontent.com", "GitHub", CounterpartyPurpose.Updates,
            CounterpartyContact.OnDemand,
            CounterpartyLearns.YourIpAddress | CounterpartyLearns.WhenYouAreOnline),
        new("check.torproject.org", "Tor Project", CounterpartyPurpose.TorCheck,
            CounterpartyContact.OnDemand, CounterpartyLearns.YourIpAddress),

        // --- Explorers. Never contacted by the wallet; your browser goes there if you click. ----------
        new("etherscan.io", "Etherscan", CounterpartyPurpose.ExplorerLink,
            CounterpartyContact.LinkOnly, CounterpartyLearns.Nothing, "ETH"),
        new("blockchair.com", "Blockchair", CounterpartyPurpose.ExplorerLink,
            CounterpartyContact.LinkOnly, CounterpartyLearns.Nothing, "BCH"),
        new("solscan.io", "Solscan", CounterpartyPurpose.ExplorerLink,
            CounterpartyContact.LinkOnly, CounterpartyLearns.Nothing, "SOL"),
        new("tronscan.org", "TronScan", CounterpartyPurpose.ExplorerLink,
            CounterpartyContact.LinkOnly, CounterpartyLearns.Nothing, "TRX"),
        new("cardanoscan.io", "Cardanoscan", CounterpartyPurpose.ExplorerLink,
            CounterpartyContact.LinkOnly, CounterpartyLearns.Nothing, "ADA"),
        new("tonviewer.com", "Tonviewer", CounterpartyPurpose.ExplorerLink,
            CounterpartyContact.LinkOnly, CounterpartyLearns.Nothing, "TON"),
    ];

    /// <summary>Contacted by the wallet on its own, without being asked. The ones worth reading first.</summary>
    public static IEnumerable<NetworkCounterparty> Automatic =>
        All.Where(c => c.Contact == CounterpartyContact.Automatic);

    /// <summary>The parties handed your actual wallet addresses — the leak Tor does not fix.</summary>
    public static IEnumerable<NetworkCounterparty> SeeingAddresses =>
        All.Where(c => c.SeesAddresses);

    /// <summary>Hosts the wallet never contacts by itself, so nothing about you reaches them unless
    /// you click through.</summary>
    public static IEnumerable<NetworkCounterparty> LinksOnly =>
        All.Where(c => c.Contact == CounterpartyContact.LinkOnly);

    /// <summary>True when this host is declared here. Hosts are matched on the bare name, so an entry
    /// that carries a path suffix for disambiguation still answers to its host.</summary>
    public static bool IsDeclared(string host) =>
        All.Any(c => HostOf(c).Equals(host, StringComparison.OrdinalIgnoreCase));

    /// <summary>The bare hostname of an entry, without any disambiguating path.</summary>
    public static string HostOf(NetworkCounterparty counterparty)
    {
        var slash = counterparty.Host.IndexOf('/');
        return slash < 0 ? counterparty.Host : counterparty.Host[..slash];
    }
}
