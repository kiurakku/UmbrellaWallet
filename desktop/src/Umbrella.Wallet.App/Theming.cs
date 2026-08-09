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
        new("purple", "The fear · noir"),
        new("blue", "Midnight · blue"),
        new("green", "Forest · green"),
        new("black", "Void · OLED black"),
        new("white", "Daylight · white"),
        new("gradient", "Nebula · violet gradient"),
        new("sunset", "Sunset · gradient"),
        new("ember", "Ember · coal red"),
        new("crimson", "Crimson · wine"),
        new("amber", "Amber · honey"),
        new("slate", "Slate · graphite"),
        new("uniswap", "Uniswap · hot pink"),
        new("ocean", "Ocean · teal"),
        new("binance", "Binance · gold"),
        new("bybit", "Bybit · sunrise amber"),
        new("okx", "OKX · mono"),
        new("telegram", "Telegram · sky blue"),
        new("ton", "TON · Gram diamond"),
        new("tron", "TRON · rubine red"),
        new("whitebit", "WhiteBit · lime green"),
        new("bitcoin", "Bitcoin · signal orange"),
        new("solana", "Solana · neon violet"),
        new("ethereum", "Ethereum · periwinkle"),
        new("monero", "Monero · lava orange"),
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
        // Electric-cyan glass on near-black — the neon-glass wallet mood.
        ["blue"] =
        [
            "#07090C", "#0C1016", "#111823", "#0E141C", "#16202E", "#1C2A3C",
            "#182430", "#23384A", "#2E4A63",
            "#1E6FA8", "#35C0FF", "#2AA0E0", "#143A54", "#123246",
            "#EAF4FB", "#B8CFDE", "#7E93A4", "#647686", "#6FD3A0",
            "#EAF4FB", "#FFFFFF", "#07090C",
        ],
        // Neon mint/emerald on near-black — the Neon Wallet mood.
        ["green"] =
        [
            "#06100B", "#0A1811", "#0F2016", "#0C1B13", "#16301F", "#1E4029",
            "#163024", "#22452F", "#2E5C3E",
            "#1E9E6A", "#43F5A5", "#35D08C", "#114A30", "#0F3A28",
            "#EBFBF1", "#C0DECB", "#86A692", "#6E8677", "#43F5A5",
            "#EBFBF1", "#FFFFFF", "#06100B",
        ],
        ["black"] =
        [
            "#000000", "#070707", "#0D0D0D", "#0A0A0A", "#141414", "#1E1E1E",
            "#1A1A1A", "#262626", "#333333",
            "#3A3A3A", "#8A8A8A", "#4A4A4A", "#2A2A2A", "#222222",
            "#FFFFFF", "#CFCFCF", "#9A9A9A", "#7A7A7A", "#7DCF8F",
            "#FFFFFF", "#E8E8E8", "#000000",
        ],
        // Light theme: text roles invert so contrast survives the flip.
        ["white"] =
        [
            "#F4F5F7", "#FFFFFF", "#FFFFFF", "#F0F1F4", "#E9EBF0", "#DFE3EC",
            "#DDE0E7", "#CBD0DA", "#B8BFCC",
            "#5B4CA8", "#6E5FB8", "#4C3F92", "#DAD5F2", "#C9C3E6",
            "#14161C", "#3A3F4B", "#5C6373", "#79808F", "#1E8E4A",
            "#1E2029", "#33374A", "#FFFFFF",
        ],
        // Vivid violet over a violet-black sweep — the purple wallet mood.
        ["gradient"] =
        [
            "#0E0A1C", "#140F26", "#1B1533", "#181229", "#251C46", "#2E2358",
            "#2A2148", "#382C63", "#443673",
            "#7C4DD6", "#B06BFF", "#9457E6", "#4B2F8E", "#402C6E",
            "#F6F3FB", "#CFC7DE", "#948CA6", "#7C748E", "#6FD3C5",
            "#F6F3FB", "#FFFFFF", "#0E0A1C",
        ],
        ["sunset"] =
        [
            "#1A0E14", "#221219", "#2B1720", "#26141C", "#3A1F2B", "#4A2836",
            "#3A2029", "#4E2C38", "#5F3746",
            "#A6455C", "#E8766A", "#C25A62", "#7E3446", "#6B3140",
            "#FBF2F3", "#E0C6C7", "#A98D93", "#8E757B", "#F0A05A",
            "#FBF2F3", "#FFFFFF", "#1A0E14",
        ],
        // Editorial noir with a hot red-orange accent — the mood of the reference posters.
        ["ember"] =
        [
            "#0B0A0A", "#131010", "#1B1514", "#171211", "#2A1E1A", "#33221C",
            "#2A201D", "#3A2A24", "#47332B",
            "#8A3320", "#FF4A24", "#C43E20", "#5C2417", "#4A241A",
            "#F6F1EF", "#D8C7C0", "#9A8A84", "#847670", "#7DCF8F",
            "#F6F1EF", "#FFFFFF", "#0B0A0A",
        ],
        ["crimson"] =
        [
            "#120809", "#1A0C0E", "#221012", "#1D0D0F", "#301619", "#3E1D21",
            "#2C1417", "#3D1D21", "#4C252A",
            "#8E2B33", "#D14A55", "#B03B45", "#6C2027", "#5A1F25",
            "#FBF1F2", "#DEC2C4", "#A78A8D", "#8C7174", "#7DCF8F",
            "#FBF1F2", "#FFFFFF", "#120809",
        ],
        ["amber"] =
        [
            "#120E05", "#1A1409", "#22190B", "#1D1509", "#312414", "#402F1A",
            "#2C2112", "#3D2E19", "#4C3A20",
            "#8A6417", "#D9A227", "#B4841F", "#6A4C11", "#59410F",
            "#FCF7EC", "#E0D2B4", "#A89673", "#8C7F63", "#7DCF8F",
            "#FCF7EC", "#FFFFFF", "#120E05",
        ],
        // Teal cyber on gunmetal — the tech/HUD mood.
        ["slate"] =
        [
            "#0A0C0D", "#0F1315", "#141A1C", "#111618", "#1B2528", "#243236",
            "#182123", "#243234", "#304446",
            "#1B8C7E", "#2DE0C0", "#23BEA6", "#124038", "#0F3730",
            "#EAF6F4", "#C0D4D0", "#849792", "#6C7E7A", "#2DE0C0",
            "#EAF6F4", "#FFFFFF", "#0A0C0D",
        ],
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
        ["bybit"] =
        [
            "#0A0B0D", "#121316", "#181A1E", "#141518", "#202329", "#2A2E35",
            "#1C1F24", "#2A2E35", "#383D46",
            "#C08400", "#F7A600", "#FFB92E", "#3A2C05", "#2E2304",
            "#EDEFF2", "#C4C9D0", "#8A909A", "#6C727C", "#7DCF8F",
            "#EDEFF2", "#FFFFFF", "#0A0B0D",
        ],
        // OKX — stark monochrome, white on true black.
        ["okx"] =
        [
            "#000000", "#0A0A0A", "#121212", "#0E0E0E", "#1A1A1A", "#242424",
            "#1A1A1A", "#2A2A2A", "#3A3A3A",
            "#B8B8B8", "#FFFFFF", "#EDEDED", "#242424", "#1C1C1C",
            "#F5F5F5", "#C8C8C8", "#8A8A8A", "#6C6C6C", "#7DCF8F",
            "#F5F5F5", "#FFFFFF", "#000000",
        ],
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
        ["ton"] =
        [
            "#0B131C", "#0F1A26", "#132030", "#101A28", "#1A2C3E", "#233A50",
            "#182838", "#25405A", "#33567A",
            "#0079BC", "#0098EA", "#2FB4FF", "#0E3350", "#0B2A44",
            "#E6F4FC", "#B4D2E4", "#7B99AC", "#627E90", "#7DCF8F",
            "#E6F4FC", "#FFFFFF", "#0B131C",
        ],
        // TRON — the TRX red (#FF3B4E) on near-black.
        ["tron"] =
        [
            "#0D0708", "#160A0C", "#1D0D10", "#180A0D", "#2A1216", "#3A181E",
            "#241216", "#3A1D22", "#4E272E",
            "#C41020", "#FF3B4E", "#FF5F6E", "#3A1015", "#2E0C11",
            "#FBEDEF", "#E2C2C6", "#AD888D", "#8E7075", "#7DCF8F",
            "#FBEDEF", "#FFFFFF", "#0D0708",
        ],
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
        ["solana"] =
        [
            "#0A0710", "#100A18", "#150E20", "#120B1C", "#241634", "#301E45",
            "#241634", "#392455", "#4E3170",
            "#7A2FD0", "#9945FF", "#B76BFF", "#2A1240", "#1F0D30",
            "#F2ECFB", "#D3C4E8", "#9C89B8", "#7F6E99", "#14F195",
            "#F2ECFB", "#FFFFFF", "#0A0710",
        ],
        // Ethereum — the #627EEA periwinkle on cool near-black.
        ["ethereum"] =
        [
            "#080A12", "#0D1019", "#111524", "#0E1220", "#1B2138", "#252D4C",
            "#1A2038", "#2A3350", "#3B466C",
            "#3C55C0", "#627EEA", "#89A0F5", "#161E3A", "#101630",
            "#EDF0FB", "#CBD3EF", "#8E97B8", "#727C9C", "#7CD0A0",
            "#EDF0FB", "#FFFFFF", "#080A12",
        ],
        // Monero — the #FF6600 orange on warm near-black.
        ["monero"] =
        [
            "#0C0805", "#140D07", "#1B120A", "#160F08", "#2C1C0E", "#3E2812",
            "#26190E", "#3C2818", "#513622",
            "#CC5200", "#FF6600", "#FF8A3D", "#3A2109", "#2C1907",
            "#FBF0E8", "#E6CDB6", "#B08F75", "#8C725D", "#7DCF8F",
            "#FBF0E8", "#FFFFFF", "#0C0805",
        ],
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

    public static string Current { get; private set; } = "purple";

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
