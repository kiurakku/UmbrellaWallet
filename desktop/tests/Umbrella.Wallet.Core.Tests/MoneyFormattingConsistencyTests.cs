using System.Globalization;
using Umbrella.Wallet.App;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Every fiat figure in the wallet must be formatted the same way. It was not: the hero balance and the
/// chart's price axis used InvariantCulture while the holdings rows and stat cards used the user's
/// locale, so one screen showed "₴16,161.25" directly above "₴15 590,68".
///
/// These pin the shared formatter's behaviour per locale so a future call site that reaches for
/// InvariantCulture stands out.
/// </summary>
public sealed class MoneyFormattingConsistencyTests : IDisposable
{
    private readonly CultureInfo _original = Fx.Culture;
    private readonly decimal _originalRate = Fx.Rate;
    private readonly string _originalSymbol = Fx.Symbol;

    [Fact]
    public void Ukrainian_uses_a_space_for_thousands_and_a_comma_for_decimals()
    {
        Fx.SetLanguage("uk");
        Fx.Rate = 1m;
        Fx.Symbol = "₴";

        var money = Fx.Money(15590.68);

        Assert.StartsWith("₴", money, StringComparison.Ordinal);
        Assert.Contains(",", money, StringComparison.Ordinal);   // decimal comma
        Assert.DoesNotContain(".", money, StringComparison.Ordinal);
    }

    [Fact]
    public void English_uses_a_comma_for_thousands_and_a_point_for_decimals()
    {
        Fx.SetLanguage("en");
        Fx.Rate = 1m;
        Fx.Symbol = "$";

        Assert.Equal("$15,590.68", Fx.Money(15590.68));
    }

    [Fact]
    public void The_decimal_separator_the_hero_splits_on_follows_the_locale()
    {
        // The hero shows the whole part and the cents as two separate text runs, so it splits the
        // formatted string. Splitting on a literal "." meant a Ukrainian total never split at all.
        foreach (var (code, expected) in new[] { ("uk", ","), ("ru", ","), ("de", ","), ("en", ".") })
        {
            Fx.SetLanguage(code);
            Assert.Equal(expected, Fx.Culture.NumberFormat.NumberDecimalSeparator);
        }
    }

    [Fact]
    public void A_formatted_total_always_contains_its_own_separator_so_the_cents_are_never_lost()
    {
        foreach (var code in new[] { "en", "uk", "ru", "de", "es", "zh" })
        {
            Fx.SetLanguage(code);
            Fx.Rate = 1m;
            Fx.Symbol = string.Empty;

            var text = 16161.25.ToString("N2", Fx.Culture);
            var separator = Fx.Culture.NumberFormat.NumberDecimalSeparator;

            Assert.Contains(separator, text, StringComparison.Ordinal);

            var cut = text.LastIndexOf(separator, StringComparison.Ordinal);
            Assert.Equal("25", text[(cut + separator.Length)..]);
        }
    }

    [Fact]
    public void An_unknown_language_falls_back_rather_than_throwing()
    {
        Fx.SetLanguage("zz-not-a-locale");
        Assert.NotNull(Fx.Culture);
        Fx.Rate = 1m;
        Fx.Symbol = "$";
        Assert.Equal("$1,000.00", Fx.Money(1000));
    }

    [Fact]
    public void Sub_unit_prices_keep_six_decimals_so_cheap_coins_still_read()
    {
        Fx.SetLanguage("en");
        Fx.Rate = 1m;
        Fx.Symbol = "$";

        Assert.Equal("$0.083960", Fx.Price(0.08396));
        Assert.Equal("$52.35", Fx.Price(52.35));
    }

    public void Dispose()
    {
        Fx.SetLanguage("en");
        typeof(Fx).GetProperty(nameof(Fx.Culture))!.SetValue(null, _original);
        Fx.Rate = _originalRate;
        Fx.Symbol = _originalSymbol;
    }
}
