# Localization

Six languages: **English, Ukrainian, Russian, Chinese, Spanish, German**. English is the base;
everything else falls back to it for a missing key.

Almost 800 keys. A parity test keeps them honest.

## Where strings live

| File | What |
|---|---|
| `App/Localization.cs` | every UI string, one dictionary per language |
| `App/GuideContent.cs` | the long-form in-app guide (English + Ukrainian) |
| `App/ViewModels/*.cs` | must only reference keys, never literals |
| `App/Views/*.axaml` | must only bind keys, never literals |

## Using a string

In XAML:

```xml
<TextBlock Text="{Binding [send.amount], Source={x:Static app:Loc.Instance}}"/>
```

In a view model:

```csharp
StatusMessage = Loc.Instance["status.addressCopied"];
StatusMessage = string.Format(Loc.Instance["status.copiedSymbolAddress"], symbol);
```

`Loc.Instance[key]` falls back to English, then returns the key itself if nothing matches. That last
behaviour is load-bearing — several features use `value == key` to mean "no entry", so they can fall
back to something sensible instead of printing a slug.

## Adding a key

Add it to **all six** dictionaries in the same place. The parity test fails otherwise, which is the
point: a key present only in English silently renders English inside a translated wallet, and nobody
notices for months. That is exactly how 73 keys went missing before this test existed.

```csharp
["myfeature.title"] = "My feature",        // en
["myfeature.title"] = "Моя функція",        // uk
["myfeature.title"] = "Моя функция",        // ru
["myfeature.title"] = "我的功能",            // zh
["myfeature.title"] = "Mi función",         // es
["myfeature.title"] = "Meine Funktion",     // de
```

### Placeholders

Use `{0}`, `{1}` and `string.Format`. Never build a sentence by concatenation — word order differs by
language, and a concatenated sentence cannot be translated correctly.

```csharp
["status.live"] = "Live · {0} assets · updated {1}",
["status.live"] = "Наживо · активів: {0} · оновлено {1}",
```

A translation that drops a placeholder fails `Placeholders_survive_translation`, because
`string.Format` would otherwise render a sentence with a hole in it.

## What must NOT be translated

Some things are identifiers, not prose. Translating them makes the user's job harder:

| Keep as-is | Why |
|---|---|
| `Bitcoin`, `Ethereum`, `Solana`, … | chain proper nouns |
| `BIP84`, `ERC-20`, `TRC-20`, `SPL` | standards the user matches against an exchange's withdrawal screen |
| `1H`, `24H`, `7D`, `30D`, `1Y` | chart range codes |
| `UMBRELLA WALLET`, `the fear` | brand |
| Ticker symbols | universal |

These are listed explicitly in `MoneyFlowLocalizationTests.AllowedLiterals`. Anything else hardcoded
in a view fails the build.

## Translating well, not literally

Two real bugs that a literal approach produced:

**`24H` → `24Г`.** A letter-by-letter transliteration of an English abbreviation. In Ukrainian, hours
abbreviate to `год`, so `24Г` means nothing. Translate the *meaning*.

**`· USD` baked into a translated caption.** `common.total` used to read
`"ЗАГАЛЬНИЙ БАЛАНС · USD"` — so a user displaying hryvnia saw "USD". If a string contains a value that
varies at runtime, it belongs in a placeholder, not in the translation.

Also: **magnitude suffixes are words, not letters.** `B` for billion is English. Ukrainian and Russian
use `млрд`. See `num.thousand` / `num.million` / `num.billion` / `num.trillion`.

## Number and date formatting

Do not format money or dates with `InvariantCulture`. Use `Fx.Culture`, which follows the wallet's
selected language:

```csharp
value.ToString("N2", Fx.Culture);      // ✅  ₴15 590,68 in uk, $15,590.68 in en
value.ToString("N2", CultureInfo.InvariantCulture);  // ❌ mixes formats across one screen
```

This caused a visible bug: the hero balance read `₴16,161.25` directly above a holdings row reading
`₴15 590,68`.

If you split a formatted number (the hero shows whole and cents as separate text runs), split on
`Fx.Culture.NumberFormat.NumberDecimalSeparator`, never a literal `"."`.

## Strings that double as keys

Some values are both the display label *and* the comparison key — Activity filters (`All`,
`Confirmed`, `Last 7 days`) and event kinds (`Received`, `Security`, `Vault`).

Translating the collection itself would break every filter comparison and any persisted selection.
Instead they stay English and are translated **at display time** by `ActivityLabelConverter`:

```xml
<TextBlock Text="{Binding Kind, Converter={StaticResource ActivityLabel}}"/>
```

The converter looks up `activity.opt.<slug>` and falls through unchanged when there is no match —
which is exactly right for an asset ticker, an address or a timestamp.

The same reasoning applies to anything written into persistent history: translating at *write* time
would freeze each row in whichever language was active when it happened.

## The in-app guide

`GuideContent.cs` holds English and Ukrainian. Other languages fall back to English.

Sections must be numbered consecutively from 1 and both languages must have the same count — a test
enforces this, because a section added to English and forgotten in Ukrainian leaves a reader missing a
chapter with no fallback to reveal it.

## Adding a whole language

1. Add the code to `Fx.CultureName` so numbers and dates format correctly.
2. Copy the English dictionary in `Localization.cs` and translate it.
3. Add it to the language picker.
4. Run the tests — parity will tell you precisely what you missed.

```bash
dotnet test desktop/Umbrella.Wallet.sln -c Release --filter 'FullyQualifiedName~Localization'
```

Optionally translate `GuideContent.cs` too; without it, that language reads the guide in English,
which is a graceful degradation rather than a bug.
