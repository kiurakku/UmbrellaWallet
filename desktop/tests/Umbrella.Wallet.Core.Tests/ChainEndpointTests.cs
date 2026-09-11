using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pointing a chain at a different server.
///
/// On a transparent chain this is the whole privacy story. To ask "what is the balance of bc1q…", the
/// wallet has to SAY the address — so whoever answers learns that the address belongs to a wallet, and
/// can tie together every address asked about in one session. Tor hides the IP; it does not un-send
/// the address. The only real fix is being able to point the wallet at a server you trust.
///
/// The rule that matters most here is the refusal of plain http. Someone deliberately choosing their
/// own endpoint for privacy, and sending their addresses over the wire in clear, would have made
/// things worse while believing they had made them better — and would be the least likely person to
/// notice. Loopback and .onion are exempt because they are not on the wire in the first place.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class ChainEndpointTests : IDisposable
{
    public ChainEndpointTests() => ChainEndpoints.ClearAll();
    /// <summary>Restores the offline state the whole run is set up with. Leaving the registry merely
    /// CLEARED would hand real network access back to every app-state test scheduled after this one —
    /// silently, and only sometimes, depending on ordering.</summary>
    public void Dispose() => TestDataIsolation.GoOffline();

    // --- what the wallet will and will not send addresses to ---------------------------------------

    [Theory]
    [InlineData("https://mempool.space/api", "https://mempool.space/api")]
    [InlineData("https://mempool.space/api/", "https://mempool.space/api")]       // trailing slash trimmed
    [InlineData("  https://esplora.example.org  ", "https://esplora.example.org")] // trimmed
    [InlineData("http://127.0.0.1:3002", "http://127.0.0.1:3002")]                // your own machine
    [InlineData("http://localhost:50001", "http://localhost:50001")]
    [InlineData("http://abcdefghij.onion/api", "http://abcdefghij.onion/api")]    // Tor encrypts it anyway
    public void An_acceptable_endpoint_is_normalised_not_rewritten(string input, string expected)
    {
        Assert.Equal(EndpointRejection.None, ChainEndpoints.Validate(input, out var normalised));
        Assert.Equal(expected, normalised);
    }

    [Fact]
    public void Plain_http_to_somebody_elses_machine_is_refused()
    {
        // The failure this exists to prevent: a user choosing their own endpoint FOR privacy, and
        // handing every address they own to whatever sits between them and that host.
        Assert.Equal(
            EndpointRejection.InsecureScheme,
            ChainEndpoints.Validate("http://esplora.example.org/api", out _));
    }

    [Theory]
    [InlineData("", EndpointRejection.Empty)]
    [InlineData("   ", EndpointRejection.Empty)]
    [InlineData("not a url", EndpointRejection.NotAUrl)]
    [InlineData("ftp://example.org", EndpointRejection.NotAUrl)]
    [InlineData("https://user:secret@example.org", EndpointRejection.CredentialsInUrl)]
    [InlineData("https://example.org/api?key=abc", EndpointRejection.HasQuery)]
    public void A_bad_endpoint_is_refused_with_a_reason(string input, EndpointRejection expected)
    {
        Assert.Equal(expected, ChainEndpoints.Validate(input, out _));
    }

    [Fact]
    public void A_refused_endpoint_never_becomes_the_active_one()
    {
        Assert.Throws<ArgumentException>(() => ChainEndpoints.SetOverride("BTC", "http://evil.example.org"));
        Assert.Null(ChainEndpoints.OverrideFor("BTC"));
        Assert.Equal("default", ChainEndpoints.Resolve("BTC", "default"));
    }

    // --- resolution ---------------------------------------------------------------------------------

    [Fact]
    public void With_no_choice_made_the_shipped_default_is_used()
    {
        Assert.Equal("https://blockstream.info/api", ChainEndpoints.Resolve("BTC", "https://blockstream.info/api"));
        Assert.False(ChainEndpoints.IsCustomised("BTC"));
    }

    [Fact]
    public void A_choice_replaces_the_default_for_that_chain_only()
    {
        ChainEndpoints.SetOverride("BTC", "https://mempool.space/api");

        Assert.Equal("https://mempool.space/api", ChainEndpoints.Resolve("BTC", "https://blockstream.info/api"));
        Assert.True(ChainEndpoints.IsCustomised("BTC"));

        // LTC was not touched and must not have moved.
        Assert.Equal("https://litecoinspace.org/api", ChainEndpoints.Resolve("LTC", "https://litecoinspace.org/api"));
        Assert.False(ChainEndpoints.IsCustomised("LTC"));
    }

    [Fact]
    public void Clearing_a_choice_returns_the_chain_to_the_default()
    {
        // There must always be a way back. A server that stops answering would otherwise leave the
        // user with a wallet that reads no balance and no obvious way to undo it.
        ChainEndpoints.SetOverride("BTC", "https://mempool.space/api");
        ChainEndpoints.SetOverride("BTC", "");

        Assert.False(ChainEndpoints.IsCustomised("BTC"));
        Assert.Equal("https://blockstream.info/api", ChainEndpoints.Resolve("BTC", "https://blockstream.info/api"));
    }

    [Fact]
    public void A_symbol_is_matched_however_it_is_cased()
    {
        ChainEndpoints.SetOverride("btc", "https://mempool.space/api");
        Assert.Equal("https://mempool.space/api", ChainEndpoints.Resolve("BTC", "x"));
    }

    // --- persistence --------------------------------------------------------------------------------

    [Fact]
    public void Choices_survive_a_round_trip_through_settings()
    {
        ChainEndpoints.SetOverride("BTC", "https://mempool.space/api");
        ChainEndpoints.SetOverride("ETH", "https://eth.drpc.org");

        var saved = ChainEndpoints.Serialise();
        ChainEndpoints.ClearAll();
        Assert.False(ChainEndpoints.IsCustomised("BTC"));

        ChainEndpoints.Restore(saved);
        Assert.Equal("https://mempool.space/api", ChainEndpoints.Resolve("BTC", "x"));
        Assert.Equal("https://eth.drpc.org", ChainEndpoints.Resolve("ETH", "x"));
    }

    [Fact]
    public void A_settings_file_holding_something_unacceptable_is_dropped_not_honoured()
    {
        // The settings file is plain text on disk and can be edited — by the user, or by anything
        // running as them. An endpoint that fails validation today must not keep receiving addresses
        // because it was written yesterday.
        ChainEndpoints.Restore("BTC=http://evil.example.org;ETH=https://eth.drpc.org");

        Assert.False(ChainEndpoints.IsCustomised("BTC"));
        Assert.Equal("https://eth.drpc.org", ChainEndpoints.Resolve("ETH", "x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("=;=;=")]
    [InlineData("BTC")]
    public void A_malformed_settings_value_leaves_every_chain_on_its_default(string? saved)
    {
        ChainEndpoints.Restore(saved);
        Assert.Empty(ChainEndpoints.Configurable.Where(ChainEndpoints.IsCustomised));
    }

    // --- the offered list ---------------------------------------------------------------------------

    [Fact]
    public void Every_offered_endpoint_is_one_the_wallet_would_accept()
    {
        // An option the picker shows but the validator refuses would be a dead button.
        foreach (var (symbol, options) in ChainEndpoints.Known)
        foreach (var option in options)
        {
            Assert.Equal(EndpointRejection.None, ChainEndpoints.Validate(option.BaseUrl, out var normalised));
            Assert.Equal(option.BaseUrl, normalised);
            Assert.False(string.IsNullOrWhiteSpace(option.Label), $"{symbol} {option.BaseUrl}");
        }
    }

    [Fact]
    public void No_chain_offers_the_same_server_twice()
    {
        foreach (var (symbol, options) in ChainEndpoints.Known)
        {
            var urls = options.Select(o => o.BaseUrl).ToList();
            Assert.Equal(urls.Distinct(StringComparer.OrdinalIgnoreCase).Count(), urls.Count);
        }
    }

    [Fact]
    public void Every_offered_endpoint_is_reached_over_tls()
    {
        // These are somebody else's machines on the open internet. The loopback and .onion exemptions
        // exist for what a USER pastes, not for what this build ships.
        foreach (var option in ChainEndpoints.Known.SelectMany(k => k.Value))
        {
            Assert.StartsWith("https://", option.BaseUrl, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_configurable_chain_has_somewhere_to_go()
    {
        Assert.NotEmpty(ChainEndpoints.Configurable);
        foreach (var symbol in ChainEndpoints.Configurable)
        {
            Assert.NotEmpty(ChainEndpoints.Known[symbol]);
        }
    }
}
