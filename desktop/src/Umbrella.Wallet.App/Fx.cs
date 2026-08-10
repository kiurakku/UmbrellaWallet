using System.Globalization;

namespace Umbrella.Wallet.App;

/// <summary>
/// The display currency. Prices and balances are computed in USD everywhere; this converts them to the
/// user's chosen fiat at display time (one USD→currency rate, refreshed with the market) and supplies
/// the symbol. A process-wide holder so the lightweight row records can format without a back-reference
/// to the view model — there is a single wallet window, so there is no cross-window contention.
/// </summary>
public static class Fx
{
    /// <summary>USD → selected currency. 1.0 while the currency is USD or the rate hasn't loaded.</summary>
    public static decimal Rate { get; set; } = 1m;

    /// <summary>The selected currency's symbol (e.g. "$", "€", "₴").</summary>
    public static string Symbol { get; set; } = "$";

    public sealed record Currency(string Code, string Symbol, string Name);

    /// <summary>The fiat currencies the wallet can display balances in.</summary>
    public static readonly IReadOnlyList<Currency> Currencies =
    [
        new("USD", "$", "US Dollar"),
        new("EUR", "€", "Euro"),
        new("UAH", "₴", "Ukrainian Hryvnia"),
        new("RUB", "₽", "Russian Ruble"),
        new("GBP", "£", "British Pound"),
        new("CNY", "¥", "Chinese Yuan"),
        new("JPY", "¥", "Japanese Yen"),
        new("PLN", "zł", "Polish Zloty"),
        new("TRY", "₺", "Turkish Lira"),
        new("INR", "₹", "Indian Rupee"),
        // + popular currencies (rates from open.er-api.com, which covers all of these).
        new("CAD", "C$", "Canadian Dollar"),
        new("AUD", "A$", "Australian Dollar"),
        new("CHF", "Fr", "Swiss Franc"),
        new("BRL", "R$", "Brazilian Real"),
        new("KRW", "₩", "South Korean Won"),
        new("MXN", "MX$", "Mexican Peso"),
        new("ZAR", "R", "South African Rand"),
        new("SEK", "kr", "Swedish Krona"),
        new("NOK", "kr", "Norwegian Krone"),
        new("AED", "د.إ", "UAE Dirham"),
        new("SGD", "S$", "Singapore Dollar"),
        new("HKD", "HK$", "Hong Kong Dollar"),
        new("KZT", "₸", "Kazakhstani Tenge"),
    ];

    public static string SymbolFor(string code) =>
        Currencies.FirstOrDefault(c => c.Code == code)?.Symbol ?? "$";

    /// <summary>A converted money amount with the current symbol, e.g. "₴1,234.56".</summary>
    public static string Money(double usd) =>
        Symbol + ((decimal)usd * Rate).ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>A converted price: 2 decimals at/above 1 unit, 6 below, so sub-cent coins still read.</summary>
    public static string Price(double usd)
    {
        var v = (decimal)usd * Rate;
        return Symbol + v.ToString(v >= 1 ? "N2" : "N6", CultureInfo.InvariantCulture);
    }
}
