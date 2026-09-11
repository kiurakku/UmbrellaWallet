using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the baked platform-fee configuration. The fee itself is OFF (see NoPlatformFeeTests); these
/// still check the obfuscated recipient addresses decode correctly, so the blobs cannot rot while the
/// feature is dormant. The recipient address is obfuscated in the binary,
/// so this asserts it de-obfuscates to exactly the intended Solana address — a wrong blob would
/// silently send fees to the wrong (or an unspendable) address.
/// </summary>
public sealed class DeveloperFeeTests
{
    private const string SolFeeAddress = "ABX24FdKZb6nyW6eiQ3bE5TdZUPdypG9P23AZeutRXL5";
    private const string TronFeeAddress = "TNvxWShQmqxskvFvh2TGYjskVwVWEisPCA";

    [Fact]
    public void Baked_solana_fee_address_decodes_correctly()
    {
        var cfg = DeveloperFeeConfig.Load();
        Assert.Equal(SolFeeAddress, cfg.AddressFor("SOL"));
    }

    [Fact]
    public void Baked_tron_and_usdt_fee_address_decodes_to_the_new_wallet()
    {
        var cfg = DeveloperFeeConfig.Load();
        Assert.Equal(TronFeeAddress, cfg.AddressFor("TRX"));
        Assert.Equal(TronFeeAddress, cfg.AddressFor("USDT"));
        Assert.Equal(TronFeeAddress, cfg.AddressFor("TRON"));         // canonicalized
        Assert.Equal(TronFeeAddress, cfg.AddressFor("USDT-TRC20"));   // canonicalized
    }

    [Fact]
    public void The_fee_is_switched_off_so_nothing_is_quoted_even_where_an_address_exists()
    {
        // Solana has a baked address AND is a routed chain, so it is the case most likely to still
        // charge if the rate were ever put back. It must quote nothing.
        var cfg = DeveloperFeeConfig.Load();
        Assert.Equal(0, cfg.EffectiveBps);
        Assert.Equal(0m, cfg.FeePercent);
        Assert.Null(cfg.QuoteFee("SOL", 10m));
    }

    [Fact]
    public void No_fee_for_chains_without_a_baked_address()
    {
        var cfg = DeveloperFeeConfig.Load();
        // BTC is a routed chain but has no baked address yet -> no fee quoted.
        Assert.Null(cfg.QuoteFee("BTC", 1m));
    }
}
