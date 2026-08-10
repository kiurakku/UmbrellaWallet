using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the baked platform-fee configuration. The recipient address is obfuscated in the binary,
/// so this asserts it de-obfuscates to exactly the intended Solana address — a wrong blob would
/// silently send fees to the wrong (or an unspendable) address.
/// </summary>
public sealed class DeveloperFeeTests
{
    private const string SolFeeAddress = "ABX24FdKZb6nyW6eiQ3bE5TdZUPdypG9P23AZeutRXL5";

    [Fact]
    public void Baked_solana_fee_address_decodes_correctly()
    {
        var cfg = DeveloperFeeConfig.Load();
        Assert.Equal(SolFeeAddress, cfg.AddressFor("SOL"));
    }

    [Fact]
    public void Fee_is_half_a_percent_and_quotes_on_sol()
    {
        var cfg = DeveloperFeeConfig.Load();
        Assert.Equal(50, cfg.EffectiveBps);
        Assert.Equal(0.5m, cfg.FeePercent);

        var quote = cfg.QuoteFee("SOL", 10m);
        Assert.NotNull(quote);
        Assert.Equal(SolFeeAddress, quote!.Value.Address);
        Assert.Equal(0.05m, quote.Value.Amount); // 0.5% of 10
    }

    [Fact]
    public void No_fee_for_chains_without_a_baked_address()
    {
        var cfg = DeveloperFeeConfig.Load();
        // BTC is a routed chain but has no baked address yet -> no fee quoted.
        Assert.Null(cfg.QuoteFee("BTC", 1m));
    }
}
