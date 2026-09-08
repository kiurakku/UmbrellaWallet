using System.Globalization;
using Umbrella.Wallet.Core.Amounts;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The amount field decides how much money leaves the wallet, so every accepted spelling is pinned
/// here — and so is the .NET behaviour that made the naive version unsafe.
/// </summary>
public sealed class AmountInputTests
{
    /// <summary>
    /// Why <see cref="AmountInput"/> exists. The wallet ships in six languages and formats figures per
    /// locale, so "0,5" is a normal thing to type — and the obvious parse turns it into FIVE, because
    /// .NET reads the comma as a group separator and never checks group sizes. Ten times the intended
    /// amount, in a send field.
    /// </summary>
    [Fact]
    public void The_naive_invariant_parse_reads_a_comma_decimal_as_grouping()
    {
        Assert.True(decimal.TryParse("0,5", NumberStyles.Number, CultureInfo.InvariantCulture, out var naive));
        Assert.Equal(5m, naive);

        // The wallet's own parser reads what the user meant.
        Assert.True(AmountInput.TryParse("0,5", out var safe));
        Assert.Equal(0.5m, safe);
    }

    [Theory]
    [InlineData("0.5", "0.5")]
    [InlineData("0,5", "0.5")]
    [InlineData("5", "5")]
    [InlineData("0", "0")]
    [InlineData("0.00000001", "0.00000001")]
    [InlineData("12.345678", "12.345678")]
    [InlineData("1,5", "1.5")]
    // A lone comma is always the decimal point: "1,234" is 1.234, never 1234 — ambiguity must never
    // resolve towards spending MORE.
    [InlineData("1,234", "1.234")]
    [InlineData("1.234", "1.234")]
    // Both separators: the rightmost is the decimal point, the other is grouping.
    [InlineData("1,234.56", "1234.56")]
    [InlineData("1.234,56", "1234.56")]
    [InlineData("1,234,567.89", "1234567.89")]
    [InlineData("1.234.567,89", "1234567.89")]
    // Repeated single separator can only be grouping.
    [InlineData("1.234.567", "1234567")]
    [InlineData("1,234,567", "1234567")]
    // Locale formatting groups with spaces, including non-breaking ones.
    [InlineData("1 234,56", "1234.56")]
    [InlineData("1 234,56", "1234.56")]
    [InlineData("  2.5  ", "2.5")]
    public void Every_spelling_a_user_might_type_reads_as_the_same_number(string typed, string expected)
    {
        Assert.True(AmountInput.TryParse(typed, out var amount), typed);
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("0.5 ETH")]
    [InlineData("$0.5")]
    [InlineData("-1")]
    [InlineData("-0.5")]
    [InlineData("+1")]
    [InlineData(".")]
    [InlineData(",")]
    [InlineData("1.2.3,4")]
    [InlineData("1,2,3.4.5")]
    [InlineData("1e5")]
    [InlineData("0x10")]
    public void Anything_ambiguous_or_not_a_number_is_refused(string? typed)
    {
        Assert.False(AmountInput.TryParse(typed, out var amount));
        Assert.Equal(0m, amount);
    }

    [Fact]
    public void Zero_parses_but_is_not_positive()
    {
        Assert.True(AmountInput.TryParse("0", out var zero));
        Assert.Equal(0m, zero);

        Assert.False(AmountInput.TryParsePositive("0", out _));
        Assert.False(AmountInput.TryParsePositive("0,0", out _));
        Assert.True(AmountInput.TryParsePositive("0,000001", out var tiny));
        Assert.Equal(0.000001m, tiny);
    }

    /// <summary>
    /// Nothing in the money flow may go back to the unsafe parse: it is silent, it succeeds, and it is
    /// wrong by a factor of ten. This scans the view models the same way the localization guard does.
    /// </summary>
    [Fact]
    public void No_view_model_parses_an_amount_with_group_separators_allowed()
    {
        var viewModels = FindViewModelsDirectory();
        if (viewModels is null) return; // not a source checkout

        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(viewModels, "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("NumberStyles.Number", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.Empty(offenders);
    }

    private static string? FindViewModelsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Umbrella.Wallet.App", "ViewModels");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
