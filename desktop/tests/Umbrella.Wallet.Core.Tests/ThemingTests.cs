using Avalonia.Media;
using Umbrella.Wallet.App;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Themes are data, and data drifts. Every failure described here had actually happened in this file:
/// ids referenced in code that named no theme at all ("white", "gradient"), a theme whose comment
/// promised an electric-cyan look while the palette was flat grey, and — the reason the set felt
/// unfinished — five palettes sharing one identical green, so different themes rendered the same.
///
/// These read the palettes as data (<see cref="Theming.PaletteOf"/>) rather than applying them, so they
/// stay pure and need no Avalonia Application.
/// </summary>
public sealed class ThemingTests
{
    [Fact]
    public void Every_theme_in_the_picker_can_actually_be_applied()
    {
        // A theme offered in Settings with no palette behind it is a dead entry: selecting it would
        // silently do nothing, because Apply returns early on an unknown id.
        foreach (var theme in Theming.Themes)
        {
            Assert.True(Theming.IsKnown(theme.Id), $"theme '{theme.Id}' is offered but has no palette");
        }
    }

    [Fact]
    public void Theme_ids_and_names_are_unique()
    {
        Assert.Equal(Theming.Themes.Count, Theming.Themes.Select(t => t.Id).Distinct().Count());
        Assert.Equal(Theming.Themes.Count, Theming.Themes.Select(t => t.Name).Distinct().Count());
    }

    [Fact]
    public void Every_palette_defines_every_colour_and_all_of_them_parse()
    {
        var expected = Theming.PaletteOf("umbrella")!.Keys.OrderBy(k => k).ToList();

        foreach (var theme in Theming.Themes)
        {
            var palette = Theming.PaletteOf(theme.Id);
            Assert.NotNull(palette);
            Assert.Equal(expected, palette!.Keys.OrderBy(k => k).ToList());

            foreach (var (key, hex) in palette)
            {
                var parsed = Color.TryParse(hex, out _);
                Assert.True(parsed, $"theme '{theme.Id}' key '{key}' is not a colour: '{hex}'");
            }
        }
    }

    [Fact]
    public void Every_theme_is_visually_distinct_from_every_other()
    {
        // The complaint this encodes: themes that were "just a purple one" and "just a green one",
        // sharing a base and differing only in accent.
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var theme in Theming.Themes)
        {
            var p = Theming.PaletteOf(theme.Id)!;
            var signature = $"{p["UmBg"]}|{p["UmAccentBright"]}|{p["UmPos"]}";

            Assert.False(seen.TryGetValue(signature, out var twin),
                $"themes '{theme.Id}' and '{twin}' are the same palette wearing two names ({signature})");
            seen[signature] = theme.Id;
        }
    }

    [Fact]
    public void No_two_themes_share_the_same_positive_colour()
    {
        // Gains are the number a user looks at most. When five themes shared one generic mint, the app
        // felt identical whichever theme was picked.
        var byColour = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var theme in Theming.Themes)
        {
            var pos = Theming.PaletteOf(theme.Id)!["UmPos"];
            if (!byColour.TryGetValue(pos, out var list)) byColour[pos] = list = [];
            list.Add(theme.Id);
        }

        var shared = byColour.Where(kv => kv.Value.Count > 1)
            .Select(kv => $"{kv.Key} used by {string.Join(", ", kv.Value)}")
            .ToList();

        Assert.Empty(shared);
    }

    [Fact]
    public void Body_text_stays_readable_on_every_theme_background()
    {
        // A palette is only finished if you can read it. WCAG AA for body text is 4.5:1.
        foreach (var theme in Theming.Themes)
        {
            var p = Theming.PaletteOf(theme.Id)!;
            var ratio = Contrast(Color.Parse(p["UmText"]), Color.Parse(p["UmBgAlt"]));
            Assert.True(ratio >= 4.5,
                $"theme '{theme.Id}': body text on background is only {ratio:F1}:1 (needs 4.5:1)");
        }
    }

    [Fact]
    public void Secondary_text_stays_readable_on_cards()
    {
        // The dimmest text the UI uses, on the surface it most often sits on. 3:1 is the large-text /
        // secondary bar — below that, hints and captions start disappearing into the card.
        foreach (var theme in Theming.Themes)
        {
            var p = Theming.PaletteOf(theme.Id)!;
            var ratio = Contrast(Color.Parse(p["UmTextMuted"]), Color.Parse(p["UmCard"]));
            Assert.True(ratio >= 3.0,
                $"theme '{theme.Id}': muted text on a card is only {ratio:F1}:1 (needs 3:1)");
        }
    }

    [Fact]
    public void Label_text_on_an_accent_filled_button_stays_readable()
    {
        // Accent-filled buttons pick their label colour from the accent's brightness. That rule has to
        // clear the contrast bar for every accent, dark crimson or neon phosphor alike.
        foreach (var theme in Theming.Themes)
        {
            var accent = Color.Parse(Theming.PaletteOf(theme.Id)!["UmAccentBright"]);
            var ratio = Contrast(Theming.AccentTextFor(accent), accent);
            Assert.True(ratio >= 4.5,
                $"theme '{theme.Id}': button label on accent is only {ratio:F1}:1 (needs 4.5:1)");
        }
    }

    [Fact]
    public void Gains_stay_distinguishable_from_the_accent()
    {
        // If a theme's accent and its positive colour are near-identical, a rising number stops reading
        // as "up" and just looks like more chrome.
        foreach (var theme in Theming.Themes)
        {
            // Matrix is the deliberate exception: a phosphor terminal is meant to be one colour.
            if (theme.Id == "matrix") continue;

            var p = Theming.PaletteOf(theme.Id)!;
            var accent = Color.Parse(p["UmAccentBright"]);
            var pos = Color.Parse(p["UmPos"]);
            var distance = Math.Abs(accent.R - pos.R) + Math.Abs(accent.G - pos.G) + Math.Abs(accent.B - pos.B);

            Assert.True(distance > 60,
                $"theme '{theme.Id}': accent and gain colour are nearly the same (distance {distance})");
        }
    }

    [Fact]
    public void Gradient_themes_name_real_themes()
    {
        // "gradient" used to be checked for here and matched nothing, so that branch was dead code.
        var gradients = Theming.Themes.Where(t => Theming.IsGradientTheme(t.Id)).ToList();
        Assert.NotEmpty(gradients);
    }

    [Fact]
    public void An_unknown_theme_is_ignored_rather_than_half_applied()
    {
        Assert.False(Theming.IsKnown("not-a-real-theme"));
        Assert.Null(Theming.PaletteOf("not-a-real-theme"));
        Assert.False(Theming.IsLightTheme("not-a-real-theme"));
    }

    // --- helpers -------------------------------------------------------------------------------

    /// <summary>WCAG relative luminance.</summary>
    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    private static double Contrast(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }
}
