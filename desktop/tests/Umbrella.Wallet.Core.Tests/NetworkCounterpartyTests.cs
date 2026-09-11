using System.Text.RegularExpressions;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The wallet's list of who it talks to, checked against who it actually talks to.
///
/// A page that says "here is every server we contact" is worth something only if it cannot quietly
/// fall behind the code. A stale one is worse than none: it reassures without being true, and the
/// person reading it is reading it because they need the truth.
///
/// So this scans the Infrastructure sources for hostnames in URLs and fails if one is not declared in
/// the catalog. Adding a new explorer, RPC or price feed without declaring it breaks the build, which
/// is the only way this stays honest as the wallet grows.
/// </summary>
public sealed class NetworkCounterpartyTests
{
    private static string? FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "desktop", "src");
            if (Directory.Exists(Path.Combine(candidate, "Umbrella.Wallet.Infrastructure"))) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Method names that make a line an actual outbound request rather than a string.
    ///
    /// Everything in the Infrastructure project is the network layer, so every URL there counts. The
    /// App project is different: it is full of links the user may click — exchanges, on-ramps, docs —
    /// which the wallet never contacts, and demanding a catalog entry for each would drown the real
    /// disclosures. So outside Infrastructure a host only counts when it appears on a line that is
    /// making the call.
    /// </summary>
    private static readonly string[] CallMarkers =
        ["GetAsync(", "PostAsync(", "SendAsync(", "GetStringAsync(", "GetFromJsonAsync(", "PostAsJsonAsync("];

    /// <summary>Hosts that are not third parties at all.</summary>
    private static bool IsLocal(string host) =>
        host is "127.0.0.1" or "localhost" or "0.0.0.0" || host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);

    private static List<(string Host, string File, int Line)> HostsInSource(string sourceRoot)
    {
        var url = new Regex(@"https?://([A-Za-z0-9._-]+)", RegexOptions.Compiled);
        var found = new List<(string, string, int)>();

        foreach (var file in Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            // bin/obj hold generated copies and third-party sources; they are not this wallet's code.
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var isNetworkLayer = file.Contains("Umbrella.Wallet.Infrastructure", StringComparison.Ordinal);

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!isNetworkLayer && !CallMarkers.Any(marker => line.Contains(marker, StringComparison.Ordinal)))
                    continue;

                foreach (Match m in url.Matches(line))
                {
                    var host = m.Groups[1].Value;
                    if (IsLocal(host)) continue;
                    found.Add((host, Path.GetFileName(file), i + 1));
                }
            }
        }

        return found;
    }

    [Fact]
    public void Every_host_in_the_code_is_declared_in_the_catalog()
    {
        var root = FindSourceRoot();
        if (root is null) return; // not a source checkout

        var undeclared = HostsInSource(root)
            .Where(h => !NetworkCounterpartyCatalog.IsDeclared(h.Host))
            .Select(h => $"{h.Host} ({h.File}:{h.Line})")
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(undeclared);
    }

    [Fact]
    public void The_catalog_does_not_list_hosts_the_code_never_uses()
    {
        // The other direction. A host that was removed from the code but left in the list makes the
        // page look worse than reality, which erodes it just as surely as the opposite.
        var root = FindSourceRoot();
        if (root is null) return;

        var inCode = HostsInSource(root).Select(h => h.Host).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stale = NetworkCounterpartyCatalog.All
            .Select(NetworkCounterpartyCatalog.HostOf)
            .Where(h => !inCode.Contains(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(stale);
    }

    // --- the claims the catalog makes have to hold together ----------------------------------------

    [Fact]
    public void A_link_only_host_is_never_told_anything()
    {
        // An explorer the wallet only ever links to must not be listed as learning something, or the
        // page overstates the exposure and the real entries lose their weight.
        foreach (var party in NetworkCounterpartyCatalog.LinksOnly)
        {
            Assert.Equal(CounterpartyLearns.Nothing, party.Learns);
        }
    }

    [Fact]
    public void Anything_the_wallet_actually_contacts_learns_at_least_the_ip()
    {
        // There is no such thing as a request that reveals nothing. Claiming otherwise for a host the
        // wallet really calls would be the exact false reassurance this catalog exists to avoid.
        var silent = NetworkCounterpartyCatalog.All
            .Where(c => c.Contact != CounterpartyContact.LinkOnly)
            .Where(c => !c.Learns.HasFlag(CounterpartyLearns.YourIpAddress))
            .Select(c => c.Host)
            .ToList();

        Assert.Empty(silent);
    }

    [Fact]
    public void The_chain_reading_services_are_named_as_seeing_your_addresses()
    {
        // The heavy leak, and the one people are most surprised by: to ask "what is the balance of
        // this address", the wallet has to say the address. Tor hides the IP; it does not un-send it.
        foreach (var host in new[]
                 {
                     "blockstream.info", "litecoinspace.org", "api.blockcypher.com",
                     "api.haskoin.com", "api.trongrid.io", "api.mainnet-beta.solana.com",
                     "toncenter.com", "cloudflare-eth.com",
                 })
        {
            var party = NetworkCounterpartyCatalog.All.Single(c => c.Host == host);
            Assert.True(party.SeesAddresses, host);
        }
    }

    [Fact]
    public void A_price_feed_is_not_claimed_to_see_addresses()
    {
        // It never gets one, and saying it does would be crying wolf on the entries that matter.
        foreach (var party in NetworkCounterpartyCatalog.All.Where(c => c.Purpose == CounterpartyPurpose.Prices))
        {
            Assert.False(party.SeesAddresses, party.Host);
        }
    }

    [Fact]
    public void An_exchange_account_is_opt_in_and_nothing_else_is()
    {
        // Credentials only ever reach a service the user deliberately connected.
        foreach (var party in NetworkCounterpartyCatalog.All)
        {
            var holdsCredentials = party.Learns.HasFlag(CounterpartyLearns.YourAccountWithThem);
            Assert.Equal(holdsCredentials, party.Contact == CounterpartyContact.OptIn);
        }
    }

    [Fact]
    public void Nothing_is_listed_twice()
    {
        var hosts = NetworkCounterpartyCatalog.All.Select(c => c.Host).ToList();
        Assert.Equal(hosts.Distinct(StringComparer.OrdinalIgnoreCase).Count(), hosts.Count);
    }

    [Fact]
    public void Every_entry_names_the_operator()
    {
        // "Some server" is not a disclosure. The point is being able to decide whether you are willing
        // to be seen by that particular company.
        foreach (var party in NetworkCounterpartyCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(party.Operator), party.Host);
        }
    }

    [Fact]
    public void Tor_is_only_claimed_to_help_where_it_does()
    {
        // Tor removes the IP and nothing else. An entry that learns nothing at all gains nothing from
        // it either, and the UI must not imply otherwise.
        foreach (var party in NetworkCounterpartyCatalog.All)
        {
            Assert.Equal(party.Learns.HasFlag(CounterpartyLearns.YourIpAddress), party.TorHelps);
        }
    }
}
