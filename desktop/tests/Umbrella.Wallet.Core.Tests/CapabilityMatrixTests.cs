using System.Text.RegularExpressions;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap P0.4 — one capability matrix, not four.
///
/// What a coin can do is stated in four places: the code (<c>MainViewModel.SendableSymbols</c>, which
/// the Send picker and the send guard both read), `docs/12-coins-and-chains.md`, the README table
/// people skim before downloading, and the summary matrix in `docs/ROADMAP.md`. Only the first is
/// executable. The other three are prose, and prose drifts.
///
/// It has drifted twice already, in both directions: the coins document under-claimed Dogecoin for
/// months after it could send, then over-claimed USDT on nine networks where only receiving works.
/// <c>CoinsDocumentAccuracyTests</c> pinned that one document. These pin the other two, against the
/// same single source of truth, because a ✅ in a table somebody reads to decide which network to
/// move money over is not a documentation nit.
/// </summary>
public sealed class CapabilityMatrixTests
{
    /// <summary>
    /// Rows about a TOKEN rather than a native coin. "USDT (TRC-20)" is a row about a token on one
    /// network; the capability set is keyed by native ticker, so these are excluded rather than
    /// matched wrongly. (`CoinsDocumentAccuracyTests` checks the token claims by hand.)
    /// </summary>
    private static readonly HashSet<string> TokenRows = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDT", "USDC",
    };

    /// <summary>
    /// Ethereum's rollups carry the symbol ETH at the same address, so the capability set keys them
    /// by network. A row named "Linea (ETH)" must be checked against LINEA, not against ETH —
    /// otherwise every rollup inherits mainnet's answer and a network nothing can broadcast on reads
    /// as sendable. zkSync Era is deliberately absent: its balance is read, nothing is sent.
    /// </summary>
    private static readonly Dictionary<string, string> RowLabelToKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Linea"] = "LINEA",
        ["Arbitrum"] = "ARB",
        ["Arb"] = "ARB",
        ["Optimism"] = "OP",
        ["OP"] = "OP",
        ["Base"] = "BASE",
        // Deliberately mapped to a key nothing can send on, rather than omitted: a "✅" in the Send
        // column of a zkSync row has to FAIL, and a row that simply had no key would be skipped.
        ["zkSync Era"] = "ZKSYNC",
        ["zkSync"] = "ZKSYNC",
    };

    /// <summary>
    /// Every capability key a row covers. The roadmap's matrix groups networks into one row
    /// ("Arb / Base / OP / Linea", "AVAX / BNB / MATIC / FTM / CRO"), and a grouped row claims the
    /// SAME thing about each of them — so one unsendable member makes the whole row a lie.
    /// Token parts ("USDT TRC-20") are dropped: the capability set is keyed by native ticker, and
    /// CoinsDocumentAccuracyTests checks the token claims by hand.
    /// </summary>
    private static List<string> KeysFor(MatrixRow row)
    {
        var parts = row.Label
            .Split(['/', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Regex.Replace(p, @"\s*\(.*\)\s*", "").Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (parts.Count <= 1) return [RowLabelToKey.GetValueOrDefault(row.Label, row.Symbol)];

        return parts
            .Where(p => !TokenRows.Contains(p.Split(' ')[0]))
            .Select(p => RowLabelToKey.GetValueOrDefault(p, p.Split(' ')[0]))
            .ToList();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "desktop", "src"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root not found from " + AppContext.BaseDirectory);
    }

    private sealed record MatrixRow(string Label, string Symbol, bool ClaimsSend);

    /// <summary>
    /// Reads a capability table: rows of `| Label | … | Send | … |`, taking the ticker out of the
    /// label where one is given ("Bitcoin (BTC)" → BTC) and the Send column by index.
    /// </summary>
    private static List<MatrixRow> ReadTable(string path, string startsAfter, int sendColumn)
    {
        var rows = new List<MatrixRow>();
        var started = false;
        var inTable = false;

        foreach (var line in File.ReadAllLines(path))
        {
            if (!started)
            {
                started = line.Contains(startsAfter, StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith("|", StringComparison.Ordinal))
            {
                inTable = true;
                var cells = line.Split('|', StringSplitOptions.None)
                    .Select(c => c.Trim())
                    .ToList();

                // cells[0] is empty (leading pipe); the header and separator rows are skipped below.
                if (cells.Count < sendColumn + 2) continue;
                var label = cells[1];
                if (label.Length == 0 || label.StartsWith("---", StringComparison.Ordinal)) continue;
                if (label.Equals("Coin", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("Symbol", StringComparison.OrdinalIgnoreCase)) continue;

                var ticker = Regex.Match(label, @"\(([A-Za-z0-9]+)\)");
                var name = Regex.Replace(label, @"\s*\(.*\)\s*", "").Replace("*", "").Trim();
                var symbol = ticker.Success ? ticker.Groups[1].Value : name;

                rows.Add(new MatrixRow(name, symbol, cells[sendColumn + 1].Contains('✅')));
                continue;
            }

            // One blank line inside a table is fine; a non-table line after it ends the section.
            if (inTable && line.Trim().Length > 0) break;
        }

        return rows;
    }

    private static void AssertTableMatchesTheCode(List<MatrixRow> rows, string where)
    {
        Assert.NotEmpty(rows);   // a parse that finds nothing would pass every assertion below

        var wrong = new List<string>();
        foreach (var row in rows)
        {
            if (TokenRows.Contains(row.Symbol)) continue;

            var keys = KeysFor(row);
            if (keys.Count == 0) continue;   // a row entirely about tokens

            // A grouped row is only allowed its tick when EVERY network in it can send.
            var canSend = keys.All(k => MainViewModel.SendableSymbols.Contains(k));
            var anySend = keys.Any(k => MainViewModel.SendableSymbols.Contains(k));

            if (row.ClaimsSend && !canSend)
            {
                var cannot = string.Join(", ", keys.Where(k => !MainViewModel.SendableSymbols.Contains(k)));
                wrong.Add($"{where}: '{row.Label}' claims Send, the code cannot send {cannot}");
            }

            if (!row.ClaimsSend && anySend)
            {
                var can = string.Join(", ", keys.Where(k => MainViewModel.SendableSymbols.Contains(k)));
                wrong.Add($"{where}: the code sends {can}, but '{row.Label}' says it cannot");
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// The README table is the one most people read, and the only capability claim visible before
    /// anything is installed.
    /// </summary>
    [Fact]
    public void The_readme_coin_table_matches_what_the_code_can_send()
    {
        var path = Path.Combine(RepoRoot(), "README.md");
        // | Coin | Receive | Balance | Send | History | Swap | Notes |  → Send is the third column.
        AssertTableMatchesTheCode(ReadTable(path, "| Coin | Receive | Balance | Send |", 3), "README");
    }

    /// <summary>
    /// The roadmap's own summary matrix. It is the document that decides what gets built next, so a
    /// wrong row there does not just mislead a reader — it plans work that is already done, or calls
    /// something finished that is not.
    /// </summary>
    [Fact]
    public void The_roadmap_coin_matrix_matches_what_the_code_can_send()
    {
        var path = Path.Combine(RepoRoot(), "docs", "ROADMAP.md");
        // | Symbol | Receive | Balance | Send | History | Note |  → Send is the third column.
        AssertTableMatchesTheCode(ReadTable(path, "| Symbol | Receive | Balance | Send |", 3), "ROADMAP §4");
    }

    /// <summary>
    /// The parser has to be able to fail. A table read as zero rows, or with the wrong column taken
    /// for Send, would let both tests above pass while the documents said anything at all.
    /// </summary>
    [Fact]
    public void The_table_parser_reads_a_known_row_correctly()
    {
        var rows = ReadTable(
            Path.Combine(RepoRoot(), "README.md"), "| Coin | Receive | Balance | Send |", 3);

        var bitcoin = Assert.Single(rows, r => r.Symbol.Equals("BTC", StringComparison.OrdinalIgnoreCase));
        Assert.True(bitcoin.ClaimsSend);

        // And a row that says it cannot send is read as such — Zcash is transparent-receive only.
        var zcash = Assert.Single(rows, r => r.Symbol.Equals("ZEC", StringComparison.OrdinalIgnoreCase));
        Assert.False(zcash.ClaimsSend);
    }
}
