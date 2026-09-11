# Theming

19 themes. Adding one is about twenty lines of colour — but the tests will hold you to a standard,
and the standard exists because a wallet you cannot read is a wallet that loses you money.

## Where

`App/Theming.cs` — the theme list and every palette.
`App/App.axaml` — the styles that consume the tokens.

Every themed surface binds a `DynamicResource`, so switching a theme repaints the whole window with no
restart.

## Adding a theme

**1. Register it**

```csharp
public static IReadOnlyList<ThemeOption> Themes { get; } =
[
    …
    new("yourtheme", "Your Theme · what it feels like"),
];
```

The name is `Identity · character`, not `Identity · colour`. "Kraken · abyssal violet" tells you what
the theme feels like; "Kraken · purple" tells you nothing you couldn't see.

**2. Add the palette** — 22 colours, in the order `Keys` declares:

```csharp
["yourtheme"] =
[
    // bg        bgAlt      card       input      cardAlt    hover
    "#07060A", "#0C0A12", "#121019", "#0E0C15", "#1C1826", "#262034",
    // bd        bd2        bd3
    "#1A1626", "#282236", "#3A3250",
    // accent    accentBr   accentHv   accentSel  accentDim
    "#7C3AED", "#9945FF", "#B06BFF", "#241546", "#1B1035",
    // text      textSoft   textDim    textMut    pos
    "#F4F1FA", "#D2CCE4", "#9B93B0", "#7A7291", "#14F195",
    // inverse   inverseHv  inverseTx
    "#F4F1FA", "#FFFFFF", "#07060A",
],
```

**3. Run the tests.** They will tell you exactly what is wrong.

```bash
dotnet test desktop/Umbrella.Wallet.sln -c Release --filter 'FullyQualifiedName~Theming'
```

## What the tests require

| Test | Requirement |
|---|---|
| Completeness | Every theme in the picker has a palette with all 22 colours, all parseable |
| Uniqueness | No two themes share ids or names |
| Distinctness | No two themes share the same `UmBg` + `UmAccentBright` + `UmPos` |
| **No shared gain colour** | Every theme has its own `UmPos` |
| Body contrast | `UmText` on `UmBgAlt` ≥ **4.5:1** (WCAG AA) |
| Muted contrast | `UmTextMuted` on `UmCard` ≥ **3:1** |
| Button contrast | The auto-chosen label on `UmAccentBright` ≥ **4.5:1** |
| Accent ≠ gain | The accent and `UmPos` must be visibly different |

### Why "no shared gain colour"

Five themes once shared the exact same green. The result was that picking a different theme mostly
meant "the same app with another accent" — the number people look at most never changed. Give your
theme its own green.

For a brand theme, use that brand's real up-colour. Binance gains are Binance green (`#0ECB81`),
Telegram's are Telegram green, Solana's are `#14F195`. It is a small detail that makes the theme feel
authentic rather than approximated.

### Button label colour is automatic

Do not set it. `Theming.AccentTextFor` compares near-black and white against your accent by actual
WCAG contrast and picks whichever reads better.

This used to be a brightness threshold, and a mid-bright accent (Sunset's coral) fell just under the
line and got white text at 2.9:1 — when dark text on the same colour gives 6.8:1. Comparing the two
candidates removes the guess and keeps working for any accent added later.

## Give it a real identity

The difference between a theme and a recoloured default is the **base**, not the accent.

| Theme | What makes it itself |
|---|---|
| Ember | warm charcoal — the base carries the red, rather than red dropped on neutral grey |
| Void | true `#000000` OLED black with electric cyan |
| Matrix | phosphor green on dead black; the accent *is* the text colour family |
| Kraken | abyssal indigo lit from below, bioluminescent teal for gains |
| Abyss | sunless-zone water |
| Solarized | the only theme whose base is a colour rather than a near-black |

If your palette is the default navy with a different `UmAccentBright`, the distinctness test may pass
but you have not really added a theme.

## Gradients

A theme can paint the page as a sweep instead of a flat fill:

```csharp
private static readonly HashSet<string> GradientThemes =
    new(StringComparer.Ordinal) { "sunset", "solana" };
```

Add your id and a stop list in `GradientBackground`. Keep it dark enough to stay a background — a
gradient that competes with the content makes every number harder to read.

## Light themes

`IsLightTheme` is derived from the palette's own background luminance, not a hardcoded id. If you add
a light theme it is detected automatically, and the card sheen inverts so it darkens instead of
blowing out to white.

Be aware the brand logo assets are light-on-transparent, so a genuinely light theme needs dark
artwork. That is why there is no light theme shipped yet.

## The one thing that is never themed

**The QR plate in Receive stays white.** A QR code needs dark-on-light contrast to scan. Tinting it
would break receiving for some scanners — silently, and only for some users.
