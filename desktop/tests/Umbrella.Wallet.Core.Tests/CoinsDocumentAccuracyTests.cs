using System.Text.RegularExpressions;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// `docs/12-coins-and-chains.md` opens with a table of what the wallet can send. Users read that table
/// to decide which network to move money over, so a wrong ✅ in it is not a documentation nit — it is
/// the wallet telling somebody a transfer will work when it will not.
///
/// It has been wrong twice. Once it under-claimed, saying Dogecoin could not send months after it
/// could. Once it over-claimed far more dangerously, promising USDT sends on nine networks where only
/// receiving works.
///
/// So the table is checked against the code rather than maintained by hand. `SendableSymbols` is the
/// single source of truth the Send picker and the send guard both read; if the table disagrees with
/// it, this fails and names the row.
/// </summary>
public sealed class CoinsDocumentAccuracyTests
{
    /// <summary>
    /// Rows whose Send column is about a TOKEN rather than a native coin. The table lists these by
    /// network for the reader's benefit ("USDT on Tron"), which does not map onto a bare ticker, so
    /// they are checked by hand below instead of by symbol lookup.
    /// </summary>
    private static readonly HashSet<string> TokenRows = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDT", "USDC",
    };

    private static string? FindDocument()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "12-coins-and-chains.md");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>The table uses display names for some coins and tickers for others.</summary>
    private static readonly Dictionary<string, string> NameToSymbol = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bitcoin"] = "BTC",
        ["Ethereum"] = "ETH",
        ["Litecoin"] = "LTC",
        ["Dogecoin"] = "DOGE",
        ["Monero"] = "XMR",
        ["Cardano"] = "ADA",
        ["Solana"] = "SOL",
        ["Tron"] = "TRX",
    };

    /// <summary>
    /// Rows of the LEADING capability table only — the one under "Coins you can send and receive
    /// today". The document has other tables further down (fees, comparisons) whose columns mean
    /// entirely different things, so parsing the whole file would compare nonsense.
    /// </summary>
    private static List<(string Symbol, string Network, bool ClaimsSend)> TableRows(string path)
    {
        var rows = new List<(string, string, bool)>();
        var row = new Regex(@"^\|\s*\*\*([A-Za-z]+)\*\*\s*\|\s*([^|]+?)\s*\|\s*([^|]*?)\s*\|");

        var inTable = false;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("### Coins you can send and receive today", StringComparison.Ordinal))
            {
                inTable = true;
                continue;
            }

            if (!inTable) continue;
            if (line.StartsWith("---", StringComparison.Ordinal)) break;   // end of that section

            var m = row.Match(line);
            if (!m.Success) continue;

            var label = m.Groups[1].Value;
            var symbol = NameToSymbol.TryGetValue(label, out var mapped) ? mapped : label;
            rows.Add((symbol, m.Groups[2].Value, m.Groups[3].Value.Contains('✅')));
        }

        return rows;
    }

    [Fact]
    public void Every_native_coin_the_table_says_can_send_really_can()
    {
        var path = FindDocument();
        if (path is null) return; // not a source checkout

        var wrong = new List<string>();
        foreach (var (symbol, network, claimsSend) in TableRows(path))
        {
            if (TokenRows.Contains(symbol)) continue;

            var canSend = MainViewModel.SendableSymbols.Contains(symbol);
            if (claimsSend && !canSend)
                wrong.Add($"{symbol} ({network}): the table says it sends, the code says it cannot");
            if (!claimsSend && canSend)
                wrong.Add($"{symbol} ({network}): the code sends it, the table says it cannot");
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void The_table_does_not_promise_token_sends_that_do_not_exist()
    {
        // USDT on Tron is the ONLY token send this build implements. The table once claimed nine more.
        var path = FindDocument();
        if (path is null) return;

        var wrong = new List<string>();
        foreach (var (symbol, network, claimsSend) in TableRows(path))
        {
            if (!TokenRows.Contains(symbol) || !claimsSend) continue;

            var isTronUsdt = symbol.Equals("USDT", StringComparison.OrdinalIgnoreCase)
                             && network.Contains("Tron", StringComparison.OrdinalIgnoreCase);
            if (!isTronUsdt)
                wrong.Add($"{symbol} on {network}: token sending is not implemented for this network");
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void Every_sendable_symbol_appears_somewhere_in_the_document()
    {
        // The other direction: a coin the wallet can send but the guide never mentions is a coin
        // nobody discovers.
        var path = FindDocument();
        if (path is null) return;

        var text = File.ReadAllText(path);
        var missing = MainViewModel.SendableSymbols
            .Where(s => !text.Contains(s, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s)
            .ToList();

        Assert.Empty(missing);
    }
}
