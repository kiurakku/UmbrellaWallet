using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Address-poisoning is a live scam: the attacker seeds your history with a dust transfer from a
/// vanity address that matches a real recipient's first and last characters, betting you'll later
/// copy the wrong one. These tests pin the defence — it must catch the lookalike, must not cry wolf on
/// ordinary different addresses, and must never block a send on its own.
/// </summary>
public sealed class AddressSafetyTests
{
    // A real recipient and a poisoned twin: same head "0x742d3" and tail "8f44e", different middle.
    private const string Real = "0x742d35Cc6634C0532925a3b844Bc454e4438f44e";
    private const string Poison = "0x742d00000000000000000000000000000008f44e";

    [Fact]
    public void A_poisoned_lookalike_of_a_known_address_is_flagged()
    {
        var r = AddressSafetyInspector.Inspect(Poison, ownAddresses: [], knownAddresses: [Real]);

        Assert.Equal(AddressSafetyLevel.Lookalike, r.Level);
        Assert.Equal(Real, r.SimilarTo);
        Assert.True(r.IsWarning);
    }

    [Fact]
    public void A_poisoned_lookalike_of_your_own_address_is_flagged()
    {
        var r = AddressSafetyInspector.Inspect(Poison, ownAddresses: [Real], knownAddresses: []);

        Assert.Equal(AddressSafetyLevel.Lookalike, r.Level);
    }

    [Fact]
    public void The_exact_known_address_is_trusted_not_a_lookalike()
    {
        var r = AddressSafetyInspector.Inspect(Real, ownAddresses: [], knownAddresses: [Real]);

        Assert.Equal(AddressSafetyLevel.Known, r.Level);
        Assert.False(r.IsWarning);
    }

    [Fact]
    public void Sending_to_your_own_address_is_flagged()
    {
        var r = AddressSafetyInspector.Inspect(Real, ownAddresses: [Real], knownAddresses: []);

        Assert.Equal(AddressSafetyLevel.OwnAddress, r.Level);
        Assert.True(r.IsWarning);
    }

    [Fact]
    public void A_brand_new_destination_is_reported_as_a_new_recipient()
    {
        var stranger = "0x1111111111111111111111111111111111111111";
        var r = AddressSafetyInspector.Inspect(stranger, ownAddresses: [Real], knownAddresses: [Real]);

        Assert.Equal(AddressSafetyLevel.NewRecipient, r.Level);
        Assert.False(r.IsWarning);
    }

    /// <summary>Two genuinely different addresses that merely share a scheme prefix must NOT trip the alarm.</summary>
    [Theory]
    [InlineData("bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq", "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4")]
    [InlineData("0x742d35Cc6634C0532925a3b844Bc454e4438f44e", "0x111122223333444455556666777788889999aaaa")]
    public void Different_addresses_sharing_only_a_scheme_prefix_are_not_lookalikes(string a, string b)
    {
        Assert.False(AddressSafetyInspector.IsLookalike(a, b));
        var r = AddressSafetyInspector.Inspect(b, ownAddresses: [], knownAddresses: [a]);
        Assert.NotEqual(AddressSafetyLevel.Lookalike, r.Level);
    }

    /// <summary>Addresses of different lengths are never lookalikes (a poisoned twin matches the length).</summary>
    [Fact]
    public void Addresses_of_different_length_are_never_lookalikes()
    {
        Assert.False(AddressSafetyInspector.IsLookalike(Real, Real + "00"));
    }

    /// <summary>The matching is case-insensitive (checksummed vs lowercased EVM addresses).</summary>
    [Fact]
    public void Matching_ignores_case()
    {
        var r = AddressSafetyInspector.Inspect(Real.ToLowerInvariant(), ownAddresses: [], knownAddresses: [Real]);
        Assert.Equal(AddressSafetyLevel.Known, r.Level);
    }

    [Fact]
    public void No_known_or_own_addresses_means_new_recipient_never_a_crash()
    {
        var r = AddressSafetyInspector.Inspect(Real, ownAddresses: null, knownAddresses: null);
        Assert.Equal(AddressSafetyLevel.NewRecipient, r.Level);
    }

    [Fact]
    public void An_empty_destination_is_not_a_warning()
    {
        var r = AddressSafetyInspector.Inspect("   ", ownAddresses: [Real], knownAddresses: [Real]);
        Assert.False(r.IsWarning);
    }
}
