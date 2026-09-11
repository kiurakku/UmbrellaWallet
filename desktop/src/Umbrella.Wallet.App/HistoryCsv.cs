using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Umbrella.Wallet.App;

/// <summary>One transaction as it is written to the exported CSV — plain strings only, so the writer is
/// independent of the view-model row type and can be unit-tested on its own.</summary>
public sealed record HistoryCsvRow(
    string Date, string Type, string Asset, string Amount, string Counterparty, string Status, string Link);

/// <summary>
/// Serialises the transaction history to a CSV a spreadsheet (Excel, LibreOffice, Google Sheets) or a
/// tax tool can open. Pure and offline — the wallet never uploads history anywhere; the user picks the
/// file and it is written locally. RFC 4180 quoting so an address or a label containing a comma, quote
/// or newline can never shift the columns.
/// </summary>
public static class HistoryCsv
{
    private static readonly string[] Header =
        ["Date", "Type", "Asset", "Amount", "Counterparty", "Status", "Explorer link"];

    public static string Build(IEnumerable<HistoryCsvRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", Header)).Append("\r\n"); // CRLF: the line ending spreadsheets expect
        foreach (var r in rows)
        {
            sb.Append(Escape(r.Date)).Append(',')
              .Append(Escape(r.Type)).Append(',')
              .Append(Escape(r.Asset)).Append(',')
              .Append(Escape(r.Amount)).Append(',')
              .Append(Escape(r.Counterparty)).Append(',')
              .Append(Escape(r.Status)).Append(',')
              .Append(Escape(r.Link)).Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>RFC 4180: wrap a field in quotes when it contains a comma, quote or newline, and double
    /// any embedded quote. A leading '=', '+', '-' or '@' is prefixed with a single quote so a spreadsheet
    /// treats a crafted value as text, not a formula (CSV-injection defence) — but a plain signed number
    /// like "+0.01" or "-0.005" is left alone, since amounts legitimately begin with a sign and are not a
    /// formula risk.</summary>
    private static string Escape(string? value)
    {
        var s = value ?? string.Empty;
        if (s.Length > 0 && (s[0] is '=' or '+' or '-' or '@') && !LooksNumeric(s))
            s = "'" + s;

        if (s.IndexOfAny([',', '"', '\n', '\r']) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"", System.StringComparison.Ordinal) + "\"";
    }

    private static bool LooksNumeric(string s) =>
        decimal.TryParse(s, System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out _);

    /// <summary>The default filename offered in the save dialog (dated, so exports don't collide).</summary>
    public static string SuggestedFileName() =>
        $"umbrella-history-{System.DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv";
}
