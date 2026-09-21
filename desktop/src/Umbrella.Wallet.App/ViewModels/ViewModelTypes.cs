using Avalonia.Controls;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Seed;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

// View-model model types (records + small display/helper classes) extracted verbatim from
// MainViewModel.cs to shrink that monolith. Same namespace and assembly, so nothing else changes;
// no behaviour change (roadmap §8).
/// <summary>One slice of the portfolio-overview breakdown: an asset, its share, value and colour.</summary>
public sealed record PortfolioSlice(string Symbol, double Percent, double Value, Avalonia.Media.IBrush Brush)
{
    public string PercentLabel => $"{Percent:0.#}%";
    public string ValueLabel => Fx.Money(Value);
    /// <summary>Segment width for a fixed 232 px stacked bar (min 3 px so a tiny slice stays visible).</summary>
    public double BarWidth => System.Math.Max(3, Percent / 100.0 * 232.0);
}

/// <summary>A stakeable coin: symbol, name, typical (approximate) reward and how staking is done.</summary>
public sealed record StakingOption(string Symbol, string Name, string Apr, string Method);

/// <summary>A coin on/off toggle for restricting which coins a wallet shows.</summary>
public sealed record CoinToggle(string Symbol, string Name, bool Enabled)
{
    public string BadgeColor => CoinBadge.Color(Symbol);
    public string BadgeGlyph => CoinGlyphs.For(Symbol);
    public Bitmap? BadgeLogo => CoinBadge.Logo(Symbol);
    public bool HasBadgeLogo => CoinBadge.HasLogo(Symbol);
    /// <summary>The disc behind the coin mark: transparent when we have a real round logo (it is
    /// already a complete brand icon), else the brand colour behind the letter-glyph fallback.</summary>
    public string BadgeBg => HasBadgeLogo ? "Transparent" : BadgeColor;
}

/// <summary>A staking row personalised to the user's holdings.</summary>
public sealed record StakingRowViewModel(
    string Symbol, string Name, string Apr, string Method, string HoldLabel, bool HasHolding)
{
    public string BadgeColor => CoinBadge.Color(Symbol);
    public string BadgeGlyph => CoinGlyphs.For(Symbol);
    public Bitmap? BadgeLogo => CoinBadge.Logo(Symbol);
    public bool HasBadgeLogo => CoinBadge.HasLogo(Symbol);
    /// <summary>The disc behind the coin mark: transparent when we have a real round logo (it is
    /// already a complete brand icon), else the brand colour behind the letter-glyph fallback.</summary>
    public string BadgeBg => HasBadgeLogo ? "Transparent" : BadgeColor;
}

public sealed record WalletAccountViewModel(
    string Symbol,
    string Name,
    string SupportStatus,
    string Address,
    string Derivation,
    double Price,
    double Amount,
    string Chain,
    double Change24h,
    /// <summary>True when this row is an unsolicited airdrop token rather than an asset the user
    /// chose to hold — see <see cref="Umbrella.Wallet.Core.Safety.SpamTokenInspector"/>. The row is
    /// kept, never deleted; Holdings simply folds it away behind a count the user can open.</summary>
    bool IsSuspectedSpam = false,
    /// <summary>
    /// How much the wallet knows about <see cref="Amount"/>. A freshly derived account starts
    /// <see cref="BalanceRead.Unknown"/> — zero is what it holds in the absence of an answer, not
    /// what the chain said — and only a successful read promotes it (roadmap P0.6).
    /// </summary>
    BalanceRead Balance = BalanceRead.Unknown,
    /// <summary>The token's contract address, for a token row; empty for a native coin. A ticker does
    /// not identify a token — two contracts can call themselves USDC — so the send path routes on
    /// this, never on the symbol (roadmap N.1).</summary>
    string Contract = "",
    /// <summary>The token's decimals as its own contract reports them. Sending with the wrong value
    /// is wrong by powers of ten, so a row whose decimals were never read stays at -1 and the send
    /// path refuses rather than assuming 18.</summary>
    int TokenDecimals = -1,
    /// <summary>For a jetton: the sender's OWN jetton-wallet contract, which is where a transfer
    /// message is addressed. <see cref="Contract"/> holds the master, which identifies the token and
    /// cannot receive one (roadmap N.3).</summary>
    string TokenWallet = "")
{
    /// <summary>The amount as the row may honestly state it: a dash while nothing has been read.</summary>
    public string AmountLabel => BalanceReadout.AmountText(Amount, Balance, Symbol);

    /// <summary>True when this row has no reading at all, so the UI can say so instead of showing 0.</summary>
    public bool IsBalanceUnknown => Balance == BalanceRead.Unknown;

    /// <summary>True when the number on screen is the last known one rather than a current one.</summary>
    public bool IsBalanceStale => Balance == BalanceRead.Cached;

    /// <summary>True for a token row the wallet knows enough about to spend: a contract and the
    /// decimals that contract reports.</summary>
    public bool IsSpendableToken => Contract.Length > 0 && TokenDecimals >= 0;

    /// <summary>A jetton can only be sent when the wallet also knows which jetton-wallet contract
    /// holds it — the master cannot receive a transfer.</summary>
    public bool IsSpendableJetton => IsSpendableToken && TokenWallet.Length > 0;

    /// <summary>Colour hint for the Receive list so status reads at a glance.</summary>
    public string StatusColor => SupportStatus switch
    {
        "Ready" => "#8FCB9B",        // green — real address, balance tracked
        "Receive only" => "#8FB8CB", // teal — real address, no balance sync (Monero)
        "Watch" => "#9AB0D6",        // blue — external watch-only
        _ => "#8A9099",               // muted — adapter pending
    };

    public bool IsReady => SupportStatus is "Ready" or "Watch" or "Receive only";
    public string StatusLabel => SupportStatus == "Planned" ? "Not ready" : SupportStatus;

    /// <summary>
    /// Which chain this address actually lives on. Sending a coin over the wrong network is one
    /// of the most common ways people lose funds, so every row states it explicitly.
    /// </summary>
    public string NetworkLabel => CoinNetworks.For(Symbol, Chain);

    /// <summary>Coin badge (brand-coloured disc + glyph), matching Holdings/Market.</summary>
    public string BadgeColor => CoinBadge.Color(Symbol);
    public string BadgeGlyph => CoinGlyphs.For(Symbol);
    public Bitmap? BadgeLogo => CoinBadge.Logo(Symbol);
    public bool HasBadgeLogo => CoinBadge.HasLogo(Symbol);
    /// <summary>The disc behind the coin mark: transparent when we have a real round logo (it is
    /// already a complete brand icon), else the brand colour behind the letter-glyph fallback.</summary>
    public string BadgeBg => HasBadgeLogo ? "Transparent" : BadgeColor;
}

/// <summary>
/// Single source of truth for the human-readable network behind a ticker, so Holdings, Receive
/// and the pickers can never disagree about which chain a coin is on.
/// </summary>
public static class CoinNetworks
{
    /// <summary>
    /// The network line under a coin, translated. It sits on the Receive screen, where sending on the
    /// wrong network is the most common way people lose funds — so it has to be readable in the
    /// user's own language. The chain names and the standards (BIP84, ERC-20, TRC-20, SPL) stay as
    /// they are: those are identifiers the user matches against an exchange’s withdrawal screen.
    /// </summary>
    public static string For(string symbol, string fallback)
    {
        var key = "net." + symbol.ToUpperInvariant();
        var value = Umbrella.Wallet.App.Loc.Instance[key];
        // Loc echoes the key back when nothing matches — the "coin we have no line for" case.
        return value == key ? fallback : value;
    }
}

/// <summary>One candlestick, pre-computed to pixel coordinates: the container sits at (ItemX,ItemY)
/// with size W×H; inside, the wick is a thin bar at WickLocalX and the body starts at BodyLocalY.</summary>
public sealed record CandleVm(
    double ItemX, double ItemY, double W, double H,
    double WickLocalX, double BodyLocalY, double BodyH, string Color);

/// <summary>One volume bar under the price chart: a rectangle placed by Canvas.Left/Top.</summary>
public sealed record VolumeBarVm(double X, double Y, double W, double H, string Color);

/// <summary>
/// One line in the Security Center: a protection, what it is doing right now, and (when the answer is
/// "nothing") where to go and switch it on. Every field is derived from real wallet state — this
/// screen is a mirror, never a reassurance.
/// </summary>
public sealed record SecurityCheckVm(
    string Glyph,
    string Title,
    string Detail,
    string StateLabel,
    string StateColor,
    bool IsGood,
    string ActionLabel = "",
    string ActionTarget = "");

/// <summary>One row of the Send review's Privacy Radar: a glyph, a short title, the plain-language
/// explanation, and an accent colour (green for a strength, amber/red for a leak).</summary>
public sealed record SendPrivacyFindingVm(string Glyph, string Title, string Detail, string Color);

/// <summary>One row in the Ctrl+K command palette: a glyph, a label, a hint, and a target the
/// view model resolves (a section name, "lock", or "coin:SYMBOL").</summary>
/// <summary>
/// One row in the Ctrl+K command palette. An observable object rather than a record because the row
/// has to light up as the user walks the list with ↑/↓ — the keyboard selection is what makes the
/// palette usable without the mouse.
/// </summary>
public sealed partial class PaletteCommand : ObservableObject
{
    public PaletteCommand(string glyph, string label, string hint, string target)
    {
        Glyph = glyph;
        Label = label;
        Hint = hint;
        Target = target;
    }

    public string Glyph { get; }
    public string Label { get; }
    public string Hint { get; }
    public string Target { get; }

    /// <summary>True while this row is the keyboard-highlighted one (Enter runs it).</summary>
    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// One spendable coin (UTXO) on the coin-control panel, with a checkbox the user ticks to decide
/// whether it may fund the current send. Wraps the underlying <see cref="OwnedUtxo"/> so the send
/// path can re-match the exact coins the user picked by (TxId, Vout) — coin control never lets the
/// planner reach a coin the user didn't select (roadmap §3.4, privacy: don't link identities).
/// </summary>
public sealed partial class CoinControlUtxoVm : ObservableObject
{
    public CoinControlUtxoVm(OwnedUtxo utxo, string symbol, bool selected)
    {
        Utxo = utxo;
        _isSelected = selected;
        Amount = $"{utxo.ValueSat / 100_000_000m:0.########} {symbol}";
        // Which address this coin sits on is the whole point of coin control: a change (internal)
        // output is already unlinked; a receive (external) output ties to whoever you gave it to.
        Kind = utxo.Path.Change == 1 ? "change" : "receive";
        var a = utxo.Address ?? string.Empty;
        AddressShort = a.Length > 16 ? $"{a[..8]}…{a[^6..]}" : a;
        FullAddress = a;
        Status = utxo.Confirmed ? "confirmed" : "pending";
        IsPending = !utxo.Confirmed;
    }

    public OwnedUtxo Utxo { get; }
    public string TxId => Utxo.TxId;
    public int Vout => Utxo.Vout;
    public long ValueSat => Utxo.ValueSat;
    public bool Confirmed => Utxo.Confirmed;

    [ObservableProperty] private bool _isSelected;

    public string Amount { get; }
    public string Kind { get; }
    public string AddressShort { get; }
    public string FullAddress { get; }
    public string Status { get; }
    public bool IsPending { get; }
}

/// <summary>One row in the P2P & DEX directory — a self-custody venue the user opens externally.</summary>
public sealed record P2pVenue(
    string Name, string Kind, string Custody, string Description, string Url, string Tag, string Accent);

/// <summary>A searchable Settings entry: a label, the pane it lives in, and hidden keywords.</summary>
public sealed record SettingsShortcut(string Label, string Tab, string Keywords)
{
    public string TabLabel => Tab;
}

/// <summary>One wallet in the multi-wallet switcher.</summary>
public sealed record WalletListItemViewModel(
    string Id, string Label, bool IsActive, bool IsLegacy, string? Color = null)
{
    public string Badge => IsLegacy ? "MAIN" : Label.Length > 0 ? Label[..1].ToUpperInvariant() : "W";
    public string StatusLabel => IsActive
        ? Umbrella.Wallet.App.Loc.Instance["settings.walletActive"]
        : Umbrella.Wallet.App.Loc.Instance["settings.walletLocked"];
    public bool CanRemove => !IsActive; // the active wallet (and the Main seed file) are protected

    /// <summary>Optional colour tag as a brush (null when untagged), for a dot in the list.</summary>
    public bool HasColor => !string.IsNullOrWhiteSpace(Color);
    public Avalonia.Media.IBrush? ColorBrush =>
        HasColor ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Color!)) : null;
}

public sealed record HoldingRowViewModel(
    string Symbol,
    string Name,
    string Chain,
    double Price,
    double Amount,
    double Value,
    double Change24h,
    string Address,
    string SupportStatus,
    /// <summary>What the wallet knows about this amount — see <see cref="BalanceRead"/> (P0.6).
    /// Defaults to <see cref="BalanceRead.Live"/> so a row built from a real reading reads normally;
    /// the account list passes its own state through.</summary>
    BalanceRead Balance = BalanceRead.Live)
{
    public string PriceLabel => Fx.Price(Price);
    public string AmountLabel => BalanceReadout.AmountText(Amount, Balance, Symbol);
    public string ValueLabel => Balance == BalanceRead.Unknown ? "—" : Fx.Money(Value);

    /// <summary>No reading at all: the row says so rather than showing a confident zero.</summary>
    public bool IsBalanceUnknown => Balance == BalanceRead.Unknown;

    /// <summary>A number from the last successful read, not from now.</summary>
    public bool IsBalanceStale => Balance == BalanceRead.Cached;

    /// <summary>The short note under an unread or stale amount, in the user's language.</summary>
    public string BalanceNote => Balance switch
    {
        BalanceRead.Unknown => Loc.Instance["balance.unavailable"],
        BalanceRead.Cached => Loc.Instance["balance.stale"],
        _ => string.Empty,
    };
    public string ChangeLabel =>
        $"{(Change24h > 0 ? "▲" : Change24h < 0 ? "▼" : "·")} {Math.Abs(Change24h):0.00}%";
    public string ChangeColor =>
        Change24h > 0 ? "#8FCB9B" : Change24h < 0 ? "#E09A9A" : "#8A9099";

    /// <summary>The chain this holding sits on — shown under the coin name.</summary>
    public string NetworkLabel => CoinNetworks.For(Symbol, Chain);

    /// <summary>Each coin's own brand colour, so the token badges read at a glance instead of
    /// being ten identical grey discs. Falls back to the app violet for anything unmapped.</summary>
    public string BadgeColor => Symbol.ToUpperInvariant() switch
    {
        "BTC" => "#F7931A",
        "ETH" => "#627EEA",
        "LTC" => "#4C6FB1",
        "DOGE" => "#C2A633",
        "TRX" => "#EF0027",
        "SOL" => "#14A87A",
        "TON" => "#0098EA",
        "ADA" => "#2A5ED9",
        "XMR" => "#F26822",
        "USDT" => "#26A17B",
        _ => "#6E5FB8",
    };

    /// <summary>A soft glow of the coin's own colour under its badge — the thematic, bolder touch.
    /// Typed as <see cref="Avalonia.Media.BoxShadows"/> so the binding is an identity assignment;
    /// a raw string would not runtime-convert to the BoxShadow property the way a colour string does.</summary>
    public Avalonia.Media.BoxShadows BadgeShadow =>
        Avalonia.Media.BoxShadows.Parse($"0 0 15 0 #59{BadgeColor[1..]}");

    /// <summary>A single recognizable mark for each coin, replacing the 3-letter ticker on the badge.
    /// Uses each coin's own currency/symbol glyph where one exists (rendered in a symbol-capable
    /// font); anything unmapped falls back to its initial.</summary>
    public string BadgeGlyph => CoinGlyphs.For(Symbol);
    public Bitmap? BadgeLogo => CoinBadge.Logo(Symbol);
    public bool HasBadgeLogo => CoinBadge.HasLogo(Symbol);
    /// <summary>The disc behind the coin mark: transparent when we have a real round logo (it is
    /// already a complete brand icon), else the brand colour behind the letter-glyph fallback.</summary>
    public string BadgeBg => HasBadgeLogo ? "Transparent" : BadgeColor;
}

/// <summary>Coin badge marks — the coins' own currency symbols, so the token badges read as logos
/// rather than tickers, without shipping any external icon set.</summary>
public static class CoinGlyphs
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = "₿",   // ₿
        ["ETH"] = "Ξ",   // Ξ
        ["LTC"] = "Ł",   // Ł
        ["DOGE"] = "Ð",  // Ð
        ["ADA"] = "₳",   // ₳
        ["USDT"] = "₮",  // ₮
        ["XMR"] = "ɱ",   // ɱ
        ["SOL"] = "◎",   // ◎
        ["TON"] = "◈",   // ◈
        ["TRX"] = "▲",   // ▲
        ["BNB"] = "◆",   // ◆
        ["MATIC"] = "⬡", // ⬡
    };

    public static string For(string symbol) =>
        Map.TryGetValue(symbol ?? "", out var g) ? g
        : string.IsNullOrEmpty(symbol) ? "?" : symbol[..1].ToUpperInvariant();
}

/// <summary>Each coin's brand colour for its badge disc, so Market/Receive/Holdings all read the
/// same way. Falls back to the app violet for anything unmapped.</summary>
public static class CoinBadge
{
    public static string Color(string symbol) => (symbol ?? "").ToUpperInvariant() switch
    {
        "BTC" => "#F7931A",
        "ETH" => "#627EEA",
        "LTC" => "#4C6FB1",
        "DOGE" => "#C2A633",
        "TRX" => "#EF0027",
        "SOL" => "#14A87A",
        "TON" => "#0098EA",
        "ADA" => "#2A5ED9",
        "XMR" => "#F26822",
        "USDT" => "#26A17B",
        "USDC" => "#2775CA",
        "BNB" => "#F3BA2F",
        "MATIC" => "#8247E5",
        "AVAX" => "#E84142",
        "FTM" => "#1969FF",
        "CRO" => "#103F68",
        "XRP" => "#23292F",
        "DOT" => "#E6007A",
        "BCH" => "#0AC18E",
        "ZEC" => "#ECB244",
        "LINK" => "#2A5ADA",
        "UNI" => "#FF007A",
        _ => "#6E5FB8",
    };

    // Coin logos in Assets/coins/<SYMBOL>.png. A symbol NOT here falls back to the coloured glyph
    // badge, which renders cleanly — a symbol here with no file renders an empty square, so
    // CoinLogoAssetTests pins the two to each other.
    private static readonly HashSet<string> LogoSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "BTC", "ETH", "LTC", "DOGE", "TRX", "SOL", "TON", "ADA", "XMR", "USDT",
        "BCH", "DOT", "XRP", "UNI", "LINK", "USDC", "CRO", "FTM", "AVAX", "MATIC", "BNB",
        // Zcash shipped as a supported chain with no logo, so it drew a letter glyph next to
        // eleven real brand marks.
        "ZEC",
    };
    private static readonly Dictionary<string, Bitmap> LogoCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool HasLogo(string? symbol) => LogoSymbols.Contains((symbol ?? "").ToUpperInvariant());

    /// <summary>The round logo bitmap for a symbol (cached), or null when there is no bundled logo.</summary>
    public static Bitmap? Logo(string? symbol)
    {
        var key = (symbol ?? "").ToUpperInvariant();
        if (!LogoSymbols.Contains(key)) return null;
        if (LogoCache.TryGetValue(key, out var cached)) return cached;
        try
        {
            using var s = Avalonia.Platform.AssetLoader.Open(
                new Uri($"avares://Umbrella.Wallet.App/Assets/coins/{key}.png"));
            var bmp = new Bitmap(s);
            LogoCache[key] = bmp;
            return bmp;
        }
        catch { return null; }
    }
}

/// <summary>
/// One entry in an asset / network picker.
///
/// <paramref name="Symbol"/> is the KEY the send path switches on. For a native coin that is the
/// ticker; for an ERC-20 it is <c>ERC20:0x…</c>, because a ticker does not identify a token — two
/// contracts can call themselves USDC, and only one of them is the one you hold.
/// <paramref name="Ticker"/> is what the user reads.
/// </summary>
public sealed record SendOption(string Symbol, string Name, string Network, string? Ticker = null)
{
    /// <summary>What to show for this asset: the ticker, never the routing key.</summary>
    public string DisplayTicker => Ticker ?? Symbol;

    public string Display => $"{DisplayTicker} · {Name}";
}

public sealed record ActivityRowViewModel(
    string Kind,
    string Asset,
    string Amount,
    string Counterparty,
    string When,
    string? Explorer = null,
    string Status = "Confirmed",
    long UnixMs = 0,
    string? RetryTo = null,
    string? RetryAmount = null,
    string? RetryChain = null,
    string? TxId = null,
    string? Note = null)
{
    /// <summary>This row is a real on-chain transaction the user can attach a private note to.</summary>
    public bool CanHaveNote => !string.IsNullOrWhiteSpace(TxId) && IsTransaction;

    /// <summary>A private note has been saved for this transaction.</summary>
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    /// <summary>A block-explorer link exists, so the row is actionable (copy the URL).</summary>
    public bool HasLink => !string.IsNullOrWhiteSpace(Explorer);

    /// <summary>True for money movements (used by the Transactions section and the "Transactions" filter).</summary>
    public bool IsTransaction => Kind is "Sent" or "Received" or "Swap";

    /// <summary>Which activity filter tab this row belongs to.</summary>
    public string Category => Kind switch
    {
        "Sent" or "Received" or "Swap" => "Transactions",
        "Connected" => "Connections",
        "Theme" or "Settings" => "Settings",
        _ => "System",
    };

    /// <summary>Colour the type at a glance: outgoing red, incoming/connected teal, swap violet,
    /// theme/settings/security/sync housekeeping muted.</summary>
    public string KindColor => Kind switch
    {
        "Sent" => "#E08A8A",
        "Received" => "#5AC8B4",
        "Connected" => "#5AC8B4",
        "Swap" => "#8A5FD6",
        "Theme" => "#E7CA83",
        _ => "#8A9099",
    };

    // ---- Confirmation status (roadmap §6: pending / confirmed / failed) ----

    public bool IsPending => string.Equals(Status, "Pending", StringComparison.OrdinalIgnoreCase);
    public bool IsFailed => string.Equals(Status, "Failed", StringComparison.OrdinalIgnoreCase);

    /// <summary>A status pill is only meaningful on money movements; housekeeping rows stay quiet.</summary>
    public bool ShowStatus => IsTransaction;

    /// <summary>Amber for in-flight, red for failed, teal for confirmed — read the outcome at a glance.</summary>
    public string StatusColor => Status switch
    {
        "Pending" => "#E7CA83",
        "Failed" => "#E08A8A",
        _ => "#5AC8B4",
    };

    /// <summary>Localized status text for the pill (falls back to English via Loc's missing-key handling).</summary>
    public string StatusLabel => Loc.Instance[$"status.{Status.ToLowerInvariant()}"];

    /// <summary>A failed broadcast never left this device, so re-sending it is safe (and offered).</summary>
    public bool CanRetry => IsFailed && !string.IsNullOrWhiteSpace(RetryTo) && !string.IsNullOrWhiteSpace(RetryChain);
}

/// <summary>One entry in the News section: a tagged, dated product note.</summary>
/// <summary>
/// One product-news entry. <paramref name="Key"/> makes an entry translatable: when
/// <c>news.&lt;key&gt;.title</c> / <c>.body</c> exist in the translation table they are shown, otherwise
/// the English text passed in is used as-is.
///
/// The fallback is deliberate. Release notes are long, and copying English into all six language
/// tables just to satisfy a lookup would bloat the file without helping anyone — historical entries
/// stay English until someone translates them, while the current release reads in the user's language.
/// </summary>
public sealed record NewsItemViewModel(string Tag, string TitleText, string BodyText, string Date, string? Key = null)
{
    public string Title => Translated("title", TitleText);
    public string Body => Translated("body", BodyText);

    private string Translated(string part, string fallback)
    {
        if (string.IsNullOrEmpty(Key)) return fallback;
        var slug = $"news.{Key}.{part}";
        var value = Umbrella.Wallet.App.Loc.Instance[slug];
        // Loc echoes the key back when it has no entry in any language.
        return value == slug ? fallback : value;
    }

    /// <summary>Tag accent colour, so update/security/guide read at a glance.</summary>
    public string TagColor => Tag switch
    {
        "SECURITY" => "#E7CA83",
        "GUIDE" => "#5AC8B4",
        _ => "#8A5FD6",
    };
}

/// <summary>
/// One coin in the market list. <see cref="IsSupported"/> states plainly whether this wallet can
/// actually hold the coin today — a "Planned" coin has no address adapter, so claiming
/// otherwise would invite someone to send funds nowhere.
/// </summary>
public sealed record MarketRowViewModel(
    string Symbol,
    string Name,
    double Price,
    double Change24h,
    bool IsSupported,
    bool HasPrice)
{
    public static MarketRowViewModel Pending(ChainInfo chain) =>
        new(chain.Symbol, chain.Name, 0, 0, chain.Support == ChainSupportLevel.Supported, false);

    public static MarketRowViewModel Live(ChainInfo chain, double price, double change) =>
        new(chain.Symbol, chain.Name, price, change, chain.Support == ChainSupportLevel.Supported, price > 0);

    /// <summary>A market-only coin (priced + charted, held via the token/EVM balance paths, not a
    /// separate wallet chain). Marked supported since its balance already surfaces in Holdings.</summary>
    public static MarketRowViewModel PendingCoin(string symbol, string name, bool holdable) =>
        new(symbol, name, 0, 0, holdable, false);

    public static MarketRowViewModel LiveCoin(string symbol, string name, double price, double change, bool holdable) =>
        new(symbol, name, price, change, holdable, price > 0);

    /// <summary>On the user's local watchlist. Carried across refreshes by the view model, which owns
    /// the list — the row itself stays a value.</summary>
    public bool IsWatched { get; init; }

    /// <summary>Filled star when watched, hollow when not — the whole control is one toggle.</summary>
    public string WatchGlyph => IsWatched ? "★" : "☆";

    public string WatchColor => IsWatched ? "#E7CA83" : "#8A9099";

    public string PriceLabel => HasPrice ? Fx.Price(Price) : "—";

    public string ChangeLabel => HasPrice
        ? $"{(Change24h > 0 ? "▲" : Change24h < 0 ? "▼" : "·")} {Math.Abs(Change24h):0.00}%"
        : "";

    public string ChangeColor =>
        !HasPrice ? "#8A9099" : Change24h > 0 ? "#8FCB9B" : Change24h < 0 ? "#E09A9A" : "#8A9099";

    public string Accepts => IsSupported ? "Accepted · address ready" : "Not yet · adapter pending";
    public string AcceptsColor => IsSupported ? "#8FCB9B" : "#8A9099";

    /// <summary>Inline sparkline drawn in every row, so no coin is left without a chart.</summary>
    public System.Collections.Generic.List<Avalonia.Point> Spark { get; init; } = new();

    public bool HasSpark => Spark.Count > 1;

    /// <summary>The chain this coin settles on — same wording as Holdings and Receive.</summary>
    public string NetworkLabel => CoinNetworks.For(Symbol, Name);

    /// <summary>Coin badge (brand-coloured disc + glyph), matching Holdings.</summary>
    public string BadgeColor => CoinBadge.Color(Symbol);
    public string BadgeGlyph => CoinGlyphs.For(Symbol);
    public Bitmap? BadgeLogo => CoinBadge.Logo(Symbol);
    public bool HasBadgeLogo => CoinBadge.HasLogo(Symbol);
    /// <summary>The disc behind the coin mark: transparent when we have a real round logo (it is
    /// already a complete brand icon), else the brand colour behind the letter-glyph fallback.</summary>
    public string BadgeBg => HasBadgeLogo ? "Transparent" : BadgeColor;
}

/// <summary>
/// One line of the send review’s "what will happen": a translated label, the amount already
/// formatted with its symbol, an optional clarifying hint, and whether it is one of the two
/// numbers that matter most (what leaves, and what is left).
/// </summary>
public sealed record SendSimulationRow(string Label, string Amount, string Hint, bool Emphasis)
{
    public bool HasHint => Hint.Length > 0;
}

/// <summary>
/// One line of the Privacy Radar: what is on (or off), and — always, never optionally — what that
/// does not do. Roadmap P1.11 / MANIFESTO §2: both halves render together or neither does.
/// </summary>
public sealed record PrivacyFindingVm(string Text, string Limit, bool IsStrength)
{
    public string Glyph => IsStrength ? "✓" : "!";
    public string Color => IsStrength ? "#8FCB9B" : "#E7CA83";
}

/// <summary>One address the wallet has handed out, and what the last complete scan found on it
/// (roadmap P0.1, §3.4). <see cref="Address"/> is what Copy copies — never the status text.</summary>
public sealed record ReceiveHistoryRow(string Address, string Status);
