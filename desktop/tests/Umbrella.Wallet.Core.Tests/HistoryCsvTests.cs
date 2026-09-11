using Umbrella.Wallet.App;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the transaction-history CSV export: a stable header, RFC-4180 quoting so a comma or quote in a
/// label/address can never shift the columns, and a CSV-injection guard so a crafted value can't run as
/// a spreadsheet formula.
/// </summary>
public sealed class HistoryCsvTests
{
    [Fact]
    public void Build_writes_a_header_and_one_row_per_transaction()
    {
        var csv = HistoryCsv.Build(
        [
            new HistoryCsvRow("Aug 14, 10:30", "Received", "BTC", "+0.01", "", "Confirmed", "https://x/tx/a"),
            new HistoryCsvRow("Aug 15, 09:00", "Sent", "BTC", "-0.005", "bc1qmerchant", "Confirmed", "https://x/tx/b"),
        ]);

        var lines = csv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        Assert.Equal(3, lines.Length); // header + 2 rows
        Assert.Equal("Date,Type,Asset,Amount,Counterparty,Status,Explorer link", lines[0]);
        // The date holds a comma, so the whole field is quoted; the empty counterparty is two commas.
        Assert.Equal("\"Aug 14, 10:30\",Received,BTC,+0.01,,Confirmed,https://x/tx/a", lines[1]);
        Assert.Contains("bc1qmerchant", lines[2]);
        Assert.EndsWith("\r\n", csv); // CRLF line endings for spreadsheets
    }

    [Fact]
    public void Build_quotes_fields_that_contain_a_comma_quote_or_newline()
    {
        var csv = HistoryCsv.Build(
        [
            new HistoryCsvRow("d", "Sent", "BTC", "1", "Acme, Inc", "Confirmed", "x"),
            new HistoryCsvRow("d", "Sent", "BTC", "1", "say \"hi\"", "Confirmed", "x"),
        ]);

        Assert.Contains("\"Acme, Inc\"", csv);       // comma → whole field quoted
        Assert.Contains("\"say \"\"hi\"\"\"", csv);   // embedded quotes doubled and field quoted
    }

    [Fact]
    public void Build_neutralises_a_field_that_looks_like_a_spreadsheet_formula()
    {
        // A counterparty label beginning with '=' must not execute as a formula when opened in Excel.
        var csv = HistoryCsv.Build(
        [
            new HistoryCsvRow("d", "Received", "BTC", "1", "=1+2", "Confirmed", "x"),
        ]);

        Assert.Contains("'=1+2", csv); // prefixed with an apostrophe so it stays text
        Assert.DoesNotContain(",=1+2,", csv);
    }

    [Fact]
    public void Build_with_no_rows_is_just_the_header()
    {
        var csv = HistoryCsv.Build([]);
        Assert.Equal("Date,Type,Asset,Amount,Counterparty,Status,Explorer link\r\n", csv);
    }

    [Fact]
    public void SuggestedFileName_is_a_dated_csv()
    {
        var name = HistoryCsv.SuggestedFileName();
        Assert.StartsWith("umbrella-history-", name);
        Assert.EndsWith(".csv", name);
    }
}
