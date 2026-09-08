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

    public static IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new("umbrella", "Umbrella · premium"),
        new("purple", "The fear · noir"),
        // One expressive accent on near-black — the editorial / crypto-app direction.
        new("signal", "Signal · red"),
        new("black", "Void · OLED black"),
        new("sunset", "Sunset · gradient"),
        new("uniswap", "Uniswap · hot pink"),
        new("ocean", "Ocean · teal"),
        new("binance", "Binance · gold"),
        new("telegram", "Telegram · sky blue"),
        new("whitebit", "WhiteBit · lime green"),
        new("bitcoin", "Bitcoin · signal orange"),
        new("kraken", "Kraken · royal violet"),
        new("nord", "Nord · arctic frost"),
        new("dracula", "Dracula · dev purple"),
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
        // Umbrella premium — the signature look: deep navy/graphite base (never pure black), matte
        // glass cards, cool-white text, and the cyan→blue→violet brand accent. This is the default.
        // bg        bgAlt      card       input      cardAlt    hover      bd         bd2        bd3        accent     accentBr   accentHv   accentSel  accentDim  text       textSoft   textDim    textMut    pos        inverse    inverseHv  inverseTx
        ["umbrella"] =
        [
            "#080D16", "#0B1220", "#111927", "#0F1826", "#151E2D", "#1B2740",
            "#1E2A3D", "#26344A", "#33455F",
            "#3478FF", "#3B82F6", "#4D8BFF", "#16233C", "#1E3A6B",
            "#F7F9FC", "#C7D0DE", "#8994A7", "#566174", "#45E6A5",
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
            "#F4F6F9", "#C4CBD4", "#8A929C", "#6C737C", "#7DCF8F",
            "#F4F6F9", "#FFFFFF", "#050506",
        ],
        // Signal / red — one bold crimson-red on true near-black (the Nixtio look). Green stays for
        // positive numbers; the accent is the app's identity colour, not the up/down colour.
        // bg        bgAlt      card       input      cardAlt    hover      bd         bd2        bd3        accent     accentBr   accentHv   accentSel  accentDim  text       textSoft   textDim    textMut    pos        inverse    inverseHv  inverseTx
        ["signal"] =
        [
            "#0A0A0B", "#101011", "#161617", "#121213", "#1E1E20", "#28282B",
            "#1F1F21", "#2B2B2E", "#3A3A3E",
            "#C42230", "#EE3244", "#FF4B5B", "#2E1013", "#260C0F",
            "#F5F5F6", "#CBCBCE", "#8C8C92", "#6A6A70", "#45D48A",
            "#F5F5F6", "#FFFFFF", "#0A0A0B",
        ],
        // Electric-cyan glass on near-black — the neon-glass wallet mood.
        ["black"] =
        [
            "#000000", "#070707", "#0D0D0D", "#0A0A0A", "#141414", "#1E1E1E",
            "#1A1A1A", "#262626", "#333333",
            "#3A3A3A", "#8A8A8A", "#4A4A4A", "#2A2A2A", "#222222",
            "#FFFFFF", "#CFCFCF", "#9A9A9A", "#7A7A7A", "#7DCF8F",
            "#FFFFFF", "#E8E8E8", "#000000",
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
        // Deep-ocean teal/cyan on midnight blue.
        ["ocean"] =
        [
            "#04090E", "#071119", "#0B1B26", "#08151F", "#102A3A", "#153A4F",
            "#0F2432", "#1B3B50", "#265069",
            "#1E7FA8", "#38C6E0", "#2AA6C0", "#123A4C", "#0F3040",
            "#E6F4FA", "#B6D0DC", "#7C96A2", "#647C86", "#43F5C0",
            "#E6F4FA", "#FFFFFF", "#04090E",
        ],
        // Binance — signature black + gold (#F0B90B).
        ["binance"] =
        [
            "#0B0E11", "#12161B", "#181D24", "#141920", "#20262F", "#2A323C",
            "#1C222A", "#2A323C", "#38424F",
            "#B88A08", "#F0B90B", "#F5C838", "#3A2F0A", "#2E2608",
            "#EAECEF", "#C7CDD4", "#848E9C", "#6A7482", "#7DCF8F",
            "#EAECEF", "#FFFFFF", "#0B0E11",
        ],
        // Bybit — gold-amber (#F7A600) on black.
        // OKX — stark monochrome, white on true black.
        // Telegram — its own blue (#2AABEE) on the Telegram-dark surface.
        ["telegram"] =
        [
            "#0E1621", "#17212B", "#1C2733", "#182430", "#22303C", "#2B3B47",
            "#1E2A36", "#2A3947", "#38495A",
            "#1E88C8", "#2AABEE", "#3FBEFF", "#123449", "#0F2A3A",
            "#EAF3FA", "#B9CFDD", "#7E96A6", "#647B8A", "#7DCF8F",
            "#EAF3FA", "#FFFFFF", "#0E1621",
        ],
        // TON / Gram — the Open Network blue (#0098EA).
        // TRON — the TRX red (#FF3B4E) on near-black.
        // WhiteBit — its bright green (#22C55E).
        ["whitebit"] =
        [
            "#08100C", "#0C1712", "#101F17", "#0D1B14", "#163021", "#1E402B",
            "#153024", "#22452F", "#2E5C3E",
            "#159550", "#22C55E", "#3BE07A", "#0F3A24", "#0C2E1D",
            "#EBFBF1", "#C0DECB", "#86A692", "#6E8677", "#22C55E",
            "#EBFBF1", "#FFFFFF", "#08100C",
        ],
        // Bitcoin — the orange (#F7931A) on warm near-black.
        ["bitcoin"] =
        [
            "#0D0A06", "#16110A", "#1D160D", "#18120A", "#2A2012", "#3A2C18",
            "#241C12", "#3A2D1D", "#4E3C27",
            "#C0700A", "#F7931A", "#FFAE42", "#3A2A0C", "#2E2109",
            "#FBF3EA", "#E2CFB8", "#AD9578", "#8E7C64", "#7DCF8F",
            "#FBF3EA", "#FFFFFF", "#0D0A06",
        ],
        // Solana — the purple→green gradient brand: violet accent, mint positives.
        // Ethereum — the #627EEA periwinkle on cool near-black.
        // Monero — the #FF6600 orange on warm near-black.
        // Kraken — the #7132F5 violet on deep indigo.
        ["kraken"] =
        [
            "#08060F", "#0D0A18", "#120D20", "#0F0A1C", "#201634", "#2C1F48",
            "#1D1636", "#2E2352", "#41306F",
            "#5A28D0", "#7132F5", "#9256FF", "#221542", "#190F31",
            "#EFEBFB", "#CFC6EC", "#978BBC", "#7B70A0", "#7CD0A0",
            "#EFEBFB", "#FFFFFF", "#08060F",
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

    /// <summary>Gradient themes paint the page as a sweep instead of a flat fill.</summary>
    private static IBrush GradientBackground(string id)
    {
        var stops = id == "sunset"
            ? [("#2A0F1B", 0.0), ("#1C1020", 0.5), ("#120C18", 1.0)]
            : new[] { ("#160B2E", 0.0), ("#0D1230", 0.55), ("#08202B", 1.0) };

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

    /// <summary>Light themes need dark artwork; the solid-white logo would vanish.</summary>
    public static bool IsLightTheme(string id) => id == "white";

    public static bool IsKnown(string id) => Palettes.ContainsKey(id);

    public static void Apply(string id)
    {
        if (!Palettes.TryGetValue(id, out var palette)) return;
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        for (var i = 0; i < Keys.Length; i++)
        {
            resources[Keys[i]] = new SolidColorBrush(Color.Parse(palette[i]));
        }

        if (id is "gradient" or "sunset") resources["UmBg"] = GradientBackground(id);

        // Card sheen: a GradientStop binds a Color, not a Brush, so these are published separately.
        // Derived from the card colour so every theme keeps the same subtle top-down lift.
        var card = Color.Parse(palette[Array.IndexOf(Keys, "UmCard")]);
        resources["UmCardTop"] = Lighten(card, id == "white" ? 1.0 : 1.10);
        resources["UmCardBottom"] = Lighten(card, id == "white" ? 0.985 : 0.90);

        // Readable text colour ON the accent, chosen by the accent's brightness — so accent-filled
        // buttons stay legible whether the theme accent is dark (red/violet) or light (mint/cyan).
        var accent = Color.Parse(palette[Array.IndexOf(Keys, "UmAccentBright")]);
        resources["UmAccentText"] = new SolidColorBrush(
            Luminance(accent) > 0.62 ? Color.Parse("#0A0A0B") : Colors.White);
        // A soft translucent wash of the accent, for hover fills and glows that follow the theme.
        resources["UmAccentWash"] = new SolidColorBrush(accent) { Opacity = 0.16 };

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

        Current = id;
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
