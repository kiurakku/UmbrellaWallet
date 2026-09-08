using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// EIP-55 checksum validation. The point is to catch a corrupted or swapped EVM destination before a
/// send is signed: a single altered character breaks the casing checksum. All-lowercase and
/// all-uppercase addresses carry no checksum and must still be accepted.
/// </summary>
public sealed class EvmAddressTests
{
    // Canonical EIP-55 vectors from the standard.
    [Theory]
    [InlineData("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed")]
    [InlineData("0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359")]
    [InlineData("0xdbF03B407c01E7cD3CBea99509d93f8DDDC8C6FB")]
    [InlineData("0xD1220A0cf47c7B9Be7A2E6BA89F429762e7b9aDb")]
    public void A_correctly_checksummed_address_is_valid(string address)
    {
        Assert.Equal(EvmChecksumState.Valid, EvmAddress.Check(address));
    }

    [Fact]
    public void ToChecksum_produces_the_canonical_casing()
    {
        Assert.Equal("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed",
            EvmAddress.ToChecksum("0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed"));
    }

    /// <summary>The costly case: a mixed-case address whose checksum is wrong (a mistyped/altered char).</summary>
    [Fact]
    public void A_broken_checksum_is_flagged_invalid()
    {
        // Same as the first vector but one letter's case is flipped.
        var broken = "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAeD"; // final d -> D
        Assert.Equal(EvmChecksumState.Invalid, EvmAddress.Check(broken));
    }

    [Theory]
    [InlineData("0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed")] // all lowercase
    [InlineData("0x5AAEB6053F3E94C9B9A09F33669435E7EF1BEAED")] // all uppercase
    public void An_uncased_address_has_no_checksum_and_is_accepted(string address)
    {
        Assert.Equal(EvmChecksumState.NoChecksum, EvmAddress.Check(address));
    }

    [Theory]
    [InlineData("bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq")]
    [InlineData("0x123")]
    [InlineData("0xZZAeb6053F3E94C9b9A09f33669435E7Ef1BeAed")]
    [InlineData("")]
    [InlineData(null)]
    public void A_non_evm_string_is_reported_as_such(string? address)
    {
        Assert.Equal(EvmChecksumState.NotEvm, EvmAddress.Check(address));
    }

    /// <summary>Round-trip: any casing of a valid address checksums to the same canonical form.</summary>
    [Fact]
    public void Checksum_is_case_insensitive_on_input()
    {
        var canonical = "0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359";
        Assert.Equal(canonical, EvmAddress.ToChecksum(canonical.ToLowerInvariant()));
        Assert.Equal(canonical, EvmAddress.ToChecksum(canonical.ToUpperInvariant()));
        Assert.Equal(EvmChecksumState.Valid, EvmAddress.Check(canonical));
    }
}
