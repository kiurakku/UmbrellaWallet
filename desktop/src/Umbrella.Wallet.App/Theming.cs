using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace Umbrella.Wallet.App;

/// <summary>
/// Runtime colour themes.
///
/// Every themed surface binds a DynamicResource key, so swapping the palette repaints the whole
/// window without a restart. The QR code's white plate is deliberately NOT themed — a QR needs
/// dark-on-light contrast to scan, and tinting it would quietly break receiving.
/// </summary>
public static class Theming
{
    public sealed record ThemeOption(string Id, string Name);

    /// <summary>
    /// Every theme is meant to be its OWN place, not the same app with the accent swapped. Each one
    /// therefore sets its own base hue (warm charcoal, abyssal indigo, phosphor black…) and its own
    /// "positive" green rather than sharing one generic mint — brand themes use the real up-colour
    /// their exchange uses, so a Binance user sees Binance's green.
    /// </summary>
    public static IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new("umbrella", "Umbrella · honey gold"),
        new("navy", "Navy · the classic blue"),
        new("purple", "The fear · monochrome noir"),
        new("signal", "Ember · crimson editorial"),
        new("black", "Void · electric OLED"),
        new("sunset", "Sunset · dusk gradient"),
        new("matrix", "Matrix · phosphor terminal"),
        new("ocean", "Abyss · deep sea"),
        new("kraken", "Kraken · abyssal violet"),
        new("nord", "Nord · arctic frost"),
        new("dracula", "Dracula · midnight dev"),
        new("solarized", "Solarized · warm slate"),
        new("bitcoin", "Bitcoin · signal orange"),
        new("ethereum", "Ethereum · periwinkle"),
        new("solana", "Solana · neon gradient"),
        new("monero", "Monero · furnace orange"),
        new("uniswap", "Uniswap · hot pink"),
        new("binance", "Binance · gold"),
        new("telegram", "Telegram · sky"),
        new("whitebit", "WhiteBit · lime"),
    ];

    /// <summary>Order matters only for readability; every theme must define every key.</summary>
    private static readonly string[] Keys =
    [
        "UmBg", "UmBgAlt", "UmCard", "UmInput", "UmCardAlt", "UmHover",
        "UmBorder", "UmBorder2", "UmBorder3",
        "UmAccent", "UmAccentBright", "UmAccentHover", "UmAccentSel", "UmAccentDim",
        "UmText", "UmTextSoft", "UmTextDim", "UmTextMuted", "UmPos",
        "UmInverse", "UmInverseHover", "UmInverseText",
    ];

    private static readonly Dictionary<string, string[]> Palettes = new()
    {
        // Umbrella — the signature look, and the default: honey gold on a warm near-black. The base
        // carries a little warmth (never flat brown, never pure black) so the gold sits in it rather than
        // on it; text is ivory rather than cold white; gains are jade, which reads clearly beside gold
        // without fighting it. The accent is a honey yellow, so accent buttons take dark labels.
        // bg        bgAlt      card       input      cardAlt    hover      bd         bd2        bd3        accent     accentBr   accentHv   accentSel  accentDim  text       textSoft   textDim    textMut    pos        inverse    inverseHv  inverseTx
        ["umbrella"] =
        [
            "#0C0A07", "#120F0A", "#18140D", "#15110B", "#201A10", "#2A2214",
            "#262015", "#362D1C", "#4A3D25",
            "#E3A72F", "#F8C846", "#FFD766", "#3B2D0D", "#33260B",
            "#FCF7EC", "#E6DCC6", "#AFA285", "#8A7E66", "#4FD39B",
            "#FCF7EC", "#FFFFFF", "#0C0A07",
        ],
        // Navy — the original signature: deep navy/graphite base (never pure black), matte glass cards,
        // cool-white text and the cyan→blue→violet accent. Kept for everyone who chose it.
        ["navy"] =
        [
            "#080D16", "#0B1220", "#111927", "#0F1826", "#151E2D", "#1B2740",
            "#1E2A3D", "#26344A", "#33455F",
            "#3478FF", "#2563EB", "#3B82F6", "#16233C", "#1E3A6B",
            "#F7F9FC", "#C7D0DE", "#8994A7", "#626D80", "#45E6A5",
            "#F7F9FC", "#FFFFFF", "#080D16",
        ],
        // bg       bgAlt    card     input    cardAlt  hover    bd       bd2      bd3      accent   accentBr accentHv accentSel accentDim text    textSoft textDim  textMut  pos
        // Primary "the fear" look: monochrome noir — near-black with cool white accents (the
        // FROSTFREED / reference mood). The accent is near-white, so accent-filled buttons read
        // white-on-black like the references. The red editorial look lives on as the Ember theme.
        ["purple"] =
        [
            "#050506", "#0C0D0F", "#131417", "#101114", "#1C1E22", "#24272C",
            "#1E2024", "#2A2D33", "#373B42",
            "#AEB6C2", "#EDF1F6", "#FFFFFF", "#2A2E36", "#22262C",
            "#F4F6F9", "#C4CBD4", "#8A929C", "#6C737C", "#7FD69A",
            "#F4F6F9", "#FFFFFF", "#050506",
        ],
        // Ember — crimson editorial on a WARM charcoal (the base carries a red undertone, so the theme
        // reads as one warm room rather than a red accent dropped on neutral grey).
        // bg        bgAlt      card       input      cardAlt    hover      bd         bd2        bd3        accent     accentBr   accentHv   accentSel  accentDim  text       textSoft   textDim    textMut    pos        inverse    inverseHv  inverseTx
        ["signal"] =
        [
            "#0A0708", "#120C0E", "#181012", "#140D0F", "#221618", "#2E1D20",
            "#201417", "#2E1E21", "#40292D",
            "#C42230", "#DE1F33", "#EE3244", "#2E1013", "#260C0F",
            "#F7F3F4", "#D6C9CB", "#9A8A8D", "#746668", "#3FD98A",
            "#F7F3F4", "#FFFFFF", "#0A0708",
        ],
        // Void — a true OLED black (#000000 really is off pixels) with the electric-cyan glass accent
        // the theme always promised. It used to be flat grey on grey, which made it the least
        // distinctive theme in the list despite the boldest name.
        ["black"] =
        [
            "#000000", "#050607", "#0B0D0E", "#08090A", "#121517", "#1A1F22",
            "#16191B", "#232829", "#333A3C",
            "#0FA8BF", "#22D3EE", "#4AE3F7", "#072A31", "#052126",
            "#FFFFFF", "#C9D2D4", "#93A0A3", "#6E7A7D", "#34D399",
            "#FFFFFF", "#E8F6F8", "#000000",
        ],
        // Matrix — phosphor green on dead black, the terminal/CRT mood. The accent IS the text colour
        // family here, which is what makes it feel like a screen rather than an app.
        ["matrix"] =
        [
            "#000000", "#030603", "#061006", "#040B04", "#0A1A0A", "#0F2810",
            "#0B1A0B", "#143214", "#1D4A1E",
            "#16A34A", "#00FF41", "#5CFF7A", "#062A0E", "#04200A",
            "#D6FFD9", "#9FE8A8", "#6FB877", "#528A58", "#00FF41",
            "#D6FFD9", "#FFFFFF", "#000000",
        ],
        ["sunset"] =
        [
            "#1A0E14", "#221219", "#2B1720", "#26141C", "#3A1F2B", "#4A2836",
            "#3A2029", "#4E2C38", "#5F3746",
            "#A6455C", "#E8766A", "#C25A62", "#7E3446", "#6B3140",
            "#FBF2F3", "#E0C6C7", "#A98D93", "#8E757B", "#F0A05A",
            "#FBF2F3", "#FFFFFF", "#1A0E14",
        ],
        // Teal cyber on gunmetal — the tech/HUD mood.
        // Uniswap: the exact hot-pink (#FF007A) on Uniswap's neutral near-black (app.uniswap.org dark).
        ["uniswap"] =
        [
            "#0D0E0E", "#131415", "#191A1C", "#141517", "#202224", "#2A2D30",
            "#1E2022", "#2C2F33", "#3A3E43",
            "#D6006B", "#FF007A", "#FF4D9E", "#3A0B22", "#2E091B",
            "#F5F6F7", "#CBD0D4", "#8D9499", "#727980", "#21C77A",
            "#F5F6F7", "#FFFFFF", "#0D0E0E",
        ],
        // Abyss — sunless-zone water: the base goes almost black-blue, and the positive is a
        // bioluminescent aqua rather than a leaf green.
        ["ocean"] =
        [
            "#02070C", "#051019", "#091A26", "#06141E", "#0F2A3A", "#14394E",
            "#0D2331", "#193A4F", "#245069",
            "#1E7FA8", "#38C6E0", "#57DAF2", "#0E3547", "#0B2B3A",
            "#E6F4FA", "#B6D0DC", "#7C96A2", "#647C86", "#43F5C0",
            "#E6F4FA", "#FFFFFF", "#02070C",
        ],
        // Binance — signature black + gold (#F0B90B), with Binance's OWN up-green (#0ECB81) rather
        // than a generic mint, so gains look the way they do on the exchange itself.
        ["binance"] =
        [
            "#0B0E11", "#12161B", "#181D24", "#141920", "#20262F", "#2A323C",
            "#1C222A", "#2A323C", "#38424F",
            "#B88A08", "#F0B90B", "#F5C838", "#3A2F0A", "#2E2608",
            "#EAECEF", "#C7CDD4", "#848E9C", "#6A7482", "#0ECB81",
            "#EAECEF", "#FFFFFF", "#0B0E11",
        ],
        // Telegram — its own blue (#2AABEE) on the Telegram-dark surface, and Telegram's own green.
        ["telegram"] =
        [
            "#0E1621", "#17212B", "#1C2733", "#182430", "#22303C", "#2B3B47",
            "#1E2A36", "#2A3947", "#38495A",
            "#1E88C8", "#2AABEE", "#3FBEFF", "#123449", "#0F2A3A",
            "#EAF3FA", "#B9CFDD", "#7E96A6", "#647B8A", "#4DCD5E",
            "#EAF3FA", "#FFFFFF", "#0E1621",
        ],
        // WhiteBit — its bright green (#22C55E).
        ["whitebit"] =
        [
            "#08100C", "#0C1712", "#101F17", "#0D1B14", "#163021", "#1E402B",
            "#153024", "#22452F", "#2E5C3E",
            "#159550", "#22C55E", "#3BE07A", "#0F3A24", "#0C2E1D",
            "#EBFBF1", "#C0DECB", "#86A692", "#6E8677", "#5BF0A0",
            "#EBFBF1", "#FFFFFF", "#08100C",
        ],
        // Bitcoin — the orange (#F7931A) on warm near-black.
        ["bitcoin"] =
        [
            "#0D0A06", "#16110A", "#1D160D", "#18120A", "#2A2012", "#3A2C18",
            "#241C12", "#3A2D1D", "#4E3C27",
            "#C0700A", "#F7931A", "#FFAE42", "#3A2A0C", "#2E2109",
            "#FBF3EA", "#E2CFB8", "#AD9578", "#8E7C64", "#6FD08C",
            "#FBF3EA", "#FFFFFF", "#0D0A06",
        ],
        // Kraken — the brand violet (#7132F5), but the theme is built to earn the NAME: an abyssal
        // indigo base, as if lit from below, with a bioluminescent teal for gains. Previously it was
        // the brand violet on a generic dark card, which is where "just a purple theme" came from.
        ["kraken"] =
        [
            "#06050D", "#0B0916", "#100C1E", "#0D091A", "#1D1531", "#291D45",
            "#1B1533", "#2C224F", "#3F2F6B",
            "#5A28D0", "#7132F5", "#9256FF", "#201441", "#180E30",
            "#EFEBFB", "#CFC6EC", "#978BBC", "#7B70A0", "#2DE0A6",
            "#EFEBFB", "#FFFFFF", "#06050D",
        ],
        // Solana — the brand IS a gradient, so this theme paints the page as one (see
        // GradientBackground) and uses Solana's own #14F195 green for gains.
        ["solana"] =
        [
            "#07060A", "#0C0A12", "#121019", "#0E0C15", "#1C1826", "#262034",
            "#1A1626", "#282236", "#3A3250",
            "#7C3AED", "#9945FF", "#B06BFF", "#241546", "#1B1035",
            "#F4F1FA", "#D2CCE4", "#9B93B0", "#7A7291", "#14F195",
            "#F4F1FA", "#FFFFFF", "#07060A",
        ],
        // Ethereum — the #627EEA periwinkle on a cool blue-grey near-black.
        ["ethereum"] =
        [
            "#06070C", "#0B0D15", "#10131D", "#0D1018", "#1A1E2B", "#232839",
            "#181C28", "#262B3B", "#363D52",
            "#4C63C7", "#627EEA", "#8098FF", "#182046", "#121936",
            "#F2F4FB", "#CBD1E4", "#939BB4", "#737B94", "#5BD6A0",
            "#F2F4FB", "#FFFFFF", "#06070C",
        ],
        // Monero — the #FF6600 furnace orange on a warm smoke-grey base, the darkest and most
        // "industrial" of the warm themes (deliberately grittier than Bitcoin's polished orange).
        ["monero"] =
        [
            "#0B0908", "#14100D", "#1A1512", "#16110E", "#261E18", "#332821",
            "#221A15", "#33281F", "#46372B",
            "#CC5200", "#FF6600", "#FF8533", "#3A1A06", "#2D1405",
            "#F7F2ED", "#DCCFC2", "#A89887", "#86786A", "#5FD08A",
            "#F7F2ED", "#FFFFFF", "#0B0908",
        ],
        // Solarized Dark — the real base03/base02 teal-slate with the #268BD2 blue and #859900 olive
        // green. The only theme whose base is a colour rather than a near-black, which is the point.
        ["solarized"] =
        [
            "#002028", "#002B36", "#073642", "#05303B", "#0C4351", "#10505F",
            "#0A3B48", "#0F4C5B", "#16606F",
            "#1F6FA8", "#268BD2", "#4BA3E3", "#052F42", "#042634",
            "#FDF6E3", "#C3CDCB", "#93A1A1", "#7A8A8A", "#859900",
            "#FDF6E3", "#FFFFFF", "#002028",
        ],
        // Nord — the arctic palette: cool slate greys, frost-blue accent.
        ["nord"] =
        [
            "#242933", "#2E3440", "#3B4252", "#353C4A", "#434C5E", "#4C566A",
            "#434C5E", "#4C566A", "#616E88",
            "#5E81AC", "#88C0D0", "#8FBCBB", "#3B4252", "#2E3440",
            "#ECEFF4", "#D8DEE9", "#A9B3C4", "#8892A4", "#A3BE8C",
            "#ECEFF4", "#FFFFFF", "#242933",
        ],
        // Dracula — the classic dev theme: #BD93F9 purple, #50FA7B green, on #282A36.
        ["dracula"] =
        [
            "#21222C", "#282A36", "#343746", "#2C2E3A", "#44475A", "#4E5267",
            "#343746", "#44475A", "#565A70",
            "#9B72E0", "#BD93F9", "#D0AEFF", "#3A2F55", "#2E2545",
            "#F8F8F2", "#DCDCE4", "#A8A8B8", "#8A8A9C", "#50FA7B",
            "#F8F8F2", "#FFFFFF", "#21222C",
        ],
    };

    /// <summary>The themes whose page background is a sweep rather than a flat fill. Solana is here
    /// because its brand identity IS a gradient; Sunset because dusk has no single colour.</summary>
    private static readonly HashSet<string> GradientThemes = new(StringComparer.Ordinal) { "sunset", "solana", "umbrella" };

    /// <summary>
    /// Themes with the BOLD signature treatment: the balance card is a solid, glowing slab of the
    /// accent with dark type on it, the round action keys are filled rather than outlined, and the
    /// active navigation item is a solid accent pill. The gold default is built around this — a gold
    /// theme that only warmed the greys would be the same app in a different tint.
    /// </summary>
    private static readonly HashSet<string> BoldThemes = new(StringComparer.Ordinal) { "umbrella" };

    public static bool IsBoldTheme(string id) => BoldThemes.Contains(id);

    /// <summary>True when the balance card is light, so the logo on it must be the dark one.</summary>
    public static bool HeroIsLight(string id) => IsBoldTheme(id) || IsLightTheme(id);

    /// <summary>Gradient themes paint the page as a sweep instead of a flat fill.</summary>
    private static IBrush GradientBackground(string id)
    {
        var stops = id switch
        {
            "sunset" => [("#2A0F1B", 0.0), ("#1C1020", 0.5), ("#120C18", 1.0)],
            // Gold: warm light pooling in from the top-left corner and fading into near-black, so
            // the page itself glows faintly rather than sitting flat behind the gold.
            "umbrella" => [("#2B1E07", 0.0), ("#16110A", 0.38), ("#0C0A07", 0.72), ("#0A0806", 1.0)],
            // Solana's purple → teal sweep, darkened to stay a background rather than a poster.
            _ => new[] { ("#1A0B33", 0.0), ("#120C28", 0.5), ("#07211D", 1.0) },
        };

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        };
        foreach (var (colour, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(Color.Parse(colour), offset));
        }

        return brush;
    }

    public static string Current { get; private set; } = "umbrella";

    /// <summary>Light themes need dark artwork; the solid-white logo would vanish. Decided from the
    /// palette's own background rather than a hardcoded id — the id this used to compare against
    /// ("white") named a theme that no longer exists, so it silently answered false for everything.</summary>
    public static bool IsLightTheme(string id) =>
        Palettes.TryGetValue(id, out var palette)
        && Luminance(Color.Parse(palette[Array.IndexOf(Keys, "UmBg")])) > 0.5;

    public static bool IsKnown(string id) => Palettes.ContainsKey(id);

    /// <summary>
    /// A theme's raw colours keyed by resource name, or null for an unknown id. Exposed so the palettes
    /// can be checked offline — contrast, distinctness, completeness — without standing up an Avalonia
    /// Application just to read <c>Application.Current.Resources</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? PaletteOf(string id) =>
        Palettes.TryGetValue(id, out var palette)
            ? Keys.Select((k, i) => (k, palette[i])).ToDictionary(x => x.k, x => x.Item2, StringComparer.Ordinal)
            : null;

    /// <summary>Near-black, the dark option for a label sitting on an accent fill.</summary>
    private static readonly Color AccentTextDark = Color.Parse("#0A0A0B");

    /// <summary>
    /// The label colour an accent-filled button uses: whichever of near-black or white actually reads
    /// better ON that accent, measured with the WCAG contrast formula.
    ///
    /// This used to be a brightness threshold ("brighter than 0.62 → dark text"). A mid-bright accent
    /// such as Sunset's coral fell just under the line and got white text at 2.9:1, when dark text on
    /// the same colour gives 6.8:1. Comparing the two candidates directly removes the guess, and keeps
    /// working for any accent added later. Shared with the palette tests so the rule is verified where
    /// it is defined.
    /// </summary>
    public static Color AccentTextFor(Color accent) =>
        WcagContrast(AccentTextDark, accent) >= WcagContrast(Colors.White, accent)
            ? AccentTextDark
            : Colors.White;

    /// <summary>WCAG 2.x relative luminance (gamma-corrected), for contrast ratios.</summary>
    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    /// <summary>WCAG contrast ratio between two colours, 1:1 (identical) to 21:1 (black on white).</summary>
    private static double WcagContrast(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>True when this theme paints its page as a gradient rather than a flat colour.</summary>
    public static bool IsGradientTheme(string id) => GradientThemes.Contains(id);

    public static void Apply(string id)
    {
        if (!Palettes.TryGetValue(id, out var palette)) return;
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        for (var i = 0; i < Keys.Length; i++)
        {
            resources[Keys[i]] = new SolidColorBrush(Color.Parse(palette[i]));
        }

        if (GradientThemes.Contains(id)) resources["UmBg"] = GradientBackground(id);

        // Card sheen: a GradientStop binds a Color, not a Brush, so these are published separately.
        // Derived from the card colour so every theme keeps the same subtle top-down lift. A light
        // theme has to lift DOWNWARD (darken) or the sheen would blow out to white.
        var light = IsLightTheme(id);
        var card = Color.Parse(palette[Array.IndexOf(Keys, "UmCard")]);
        resources["UmCardTop"] = Lighten(card, light ? 1.0 : 1.10);
        resources["UmCardBottom"] = Lighten(card, light ? 0.985 : 0.90);

        // Readable text colour ON the accent, chosen by the accent's brightness — so accent-filled
        // buttons stay legible whether the theme accent is dark (red/violet) or light (mint/cyan).
        var accent = Color.Parse(palette[Array.IndexOf(Keys, "UmAccentBright")]);
        resources["UmAccentText"] = new SolidColorBrush(AccentTextFor(accent));
        // A soft translucent wash of the accent, for hover fills and glows that follow the theme.
        resources["UmAccentWash"] = new SolidColorBrush(accent) { Opacity = 0.16 };

        // Glow colours for the splash and aurora effects, which used to be a fixed brand blue and
        // showed as blue smudges on every other theme. GradientStops take Colors, not brushes.
        var bg = Color.Parse(palette[Array.IndexOf(Keys, "UmBg")]);
        resources["UmGlow"] = accent;
        resources["UmGlowSoft"] = Color.FromArgb(0x55, accent.R, accent.G, accent.B);
        resources["UmGlowFaint"] = Color.FromArgb(0x1A, accent.R, accent.G, accent.B);
        resources["UmGlowClear"] = Color.FromArgb(0x00, accent.R, accent.G, accent.B);
        resources["UmBgClear"] = Color.FromArgb(0x00, bg.R, bg.G, bg.B);

        // Hero balance-card gradient, DERIVED from the theme so the card never clashes with the palette.
        // A fixed violet gradient used to sit under every theme, which "spoiled" the reds/greens/etc.
        // Card → a dark tint of the theme accent → card-alt, on a diagonal — a premium, on-theme surface.
        var heroCard = Color.Parse(palette[Array.IndexOf(Keys, "UmCard")]);
        var heroTint = Color.Parse(palette[Array.IndexOf(Keys, "UmAccentDim")]);
        var heroEnd = Color.Parse(palette[Array.IndexOf(Keys, "UmCardAlt")]);
        var hero = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        };
        hero.GradientStops.Add(new GradientStop(heroCard, 0.0));
        hero.GradientStops.Add(new GradientStop(heroTint, 0.55));
        hero.GradientStops.Add(new GradientStop(heroEnd, 1.0));
        resources["UmHeroGradient"] = hero;

        PublishSignature(resources, id, palette, hero, accent);

        Current = id;
    }

    /// <summary>
    /// The bold balance card's gold slab, corner highlight to far edge: pale highlight, honey through the
    /// middle, deep amber at the edge — a metal card, not a flat yellow rectangle. Public, with the two
    /// inks printed on it, so the contrast tests read exactly what <see cref="Apply"/> paints.
    /// </summary>
    public static readonly IReadOnlyList<(string Hex, double Offset)> GoldSlabStops =
        [("#FFEFA8", 0.0), ("#FBD24A", 0.34), ("#F4B526", 0.72), ("#E39A14", 1.0)];

    /// <summary>The balance and the currency sign on the gold card.</summary>
    public const string GoldInk = "#1A1206";

    /// <summary>The card's small print — "Total balance", the cents, the Hide chip.</summary>
    public const string GoldInkSoft = "#4A360C";

    /// <summary>
    /// The balance card, the round action keys and the active navigation item, per theme. Every theme
    /// publishes every one of these, so switching away from a bold theme restores the quiet look —
    /// nothing from the previous theme can linger.
    /// </summary>
    private static void PublishSignature(
        Avalonia.Controls.IResourceDictionary resources, string id, string[] palette, IBrush hero, Color accent)
    {
        IBrush Solid(string key) => new SolidColorBrush(Color.Parse(palette[Array.IndexOf(Keys, key)]));
        IBrush Hex(string hex) => new SolidColorBrush(Color.Parse(hex));
        var accentText = new SolidColorBrush(AccentTextFor(accent));

        if (BoldThemes.Contains(id))
        {
            var slab = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            };
            foreach (var (stop, offset) in GoldSlabStops)
                slab.GradientStops.Add(new GradientStop(Color.Parse(stop), offset));

            resources["UmHeroSurface"] = slab;
            resources["UmHeroWash"] = Brushes.Transparent;
            resources["UmHeroEdge"] = Hex("#FFE183");
            resources["UmHeroText"] = Hex(GoldInk);
            resources["UmHeroTextSoft"] = Hex(GoldInkSoft);
            resources["UmHeroAccent"] = Hex(GoldInk);
            resources["UmHeroUnderlay"] = Hex("#E61A1206");
            // Depth instead of darkness: a deeper amber pooling bottom-left, never a grey film.
            resources["UmHeroShade"] = Color.Parse("#66B8700A");
            resources["UmHeroShadeClear"] = Color.Parse("#00B8700A");
            resources["UmHeroVideoOpacity"] = 0.22;

            resources["UmDiscFill"] = Solid("UmAccentBright");
            resources["UmDiscEdge"] = Solid("UmAccentHover");
            resources["UmDiscIcon"] = accentText;
            resources["UmDiscFillHover"] = Solid("UmAccentHover");
            resources["UmDiscIconHover"] = accentText;

            resources["UmNavActiveFill"] = Solid("UmAccentBright");
            resources["UmNavActiveIcon"] = accentText;
            resources["UmNavActiveText"] = accentText;
            return;
        }

        resources["UmHeroSurface"] = hero;
        resources["UmHeroWash"] = new SolidColorBrush(accent) { Opacity = 0.16 };
        resources["UmHeroEdge"] = Solid("UmBorder");
        resources["UmHeroText"] = Solid("UmText");
        resources["UmHeroTextSoft"] = Solid("UmTextDim");
        resources["UmHeroAccent"] = Solid("UmAccentBright");
        resources["UmHeroUnderlay"] = Brushes.Transparent;
        resources["UmHeroShade"] = Color.Parse("#B3060B14");
        resources["UmHeroShadeClear"] = Color.Parse("#00060B14");
        resources["UmHeroVideoOpacity"] = 0.6;

        resources["UmDiscFill"] = new SolidColorBrush(accent) { Opacity = 0.16 };
        resources["UmDiscEdge"] = Solid("UmBorder3");
        resources["UmDiscIcon"] = Solid("UmAccentBright");
        resources["UmDiscFillHover"] = Solid("UmAccentBright");
        resources["UmDiscIconHover"] = accentText;

        resources["UmNavActiveFill"] = Solid("UmAccentSel");
        resources["UmNavActiveIcon"] = Solid("UmAccentBright");
        resources["UmNavActiveText"] = Solid("UmText");
    }

    /// <summary>Perceptual-ish brightness in 0..1, to decide dark-vs-light text on a colour.</summary>
    private static double Luminance(Color c) => ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255.0;

    /// <summary>Scales a colour's channels, clamped so bright themes don't wrap around to black.</summary>
    private static Color Lighten(Color colour, double factor) => Color.FromRgb(
        (byte)Math.Clamp(colour.R * factor, 0, 255),
        (byte)Math.Clamp(colour.G * factor, 0, 255),
        (byte)Math.Clamp(colour.B * factor, 0, 255));

    /// <summary>Seeds the default palette before the first window is shown.</summary>
    public static void ApplyDefaults() => Apply(Current);

    public static string NameOf(string id) =>
        Themes.FirstOrDefault(t => t.Id == id)?.Name ?? id;
}
